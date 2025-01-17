using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Thinktecture.Relay.Acknowledgement;
using Thinktecture.Relay.Server.Diagnostics;
using Thinktecture.Relay.Server.Interceptor;
using Thinktecture.Relay.Server.Persistence;
using Thinktecture.Relay.Server.Transport;
using Thinktecture.Relay.Transport;

namespace Thinktecture.Relay.Server.Middleware;

/// <inheritdoc/>
public partial class RelayMiddleware<TRequest, TResponse, TAcknowledge> : IMiddleware
	where TRequest : IClientRequest
	where TResponse : class, ITargetResponse, new()
	where TAcknowledge : IAcknowledgeRequest
{
	private readonly IBodyStore _bodyStore;
	private readonly IEnumerable<IClientRequestInterceptor<TRequest, TResponse>> _clientRequestInterceptors;
	private readonly ConnectorRegistry<TRequest> _connectorRegistry;
	private readonly ILogger<RelayMiddleware<TRequest, TResponse, TAcknowledge>> _logger;

	// TODO move to LoggerMessage source generator when destructuring is supported
	// (see https://github.com/dotnet/runtime/issues/69490)
	private readonly Action<ILogger, IClientRequest, Exception?> _logRequestParsed =
		LoggerMessage.Define<IClientRequest>(LogLevel.Trace, 20602, "Parsed request {@Request}");

	private readonly int _maximumBodySize;
	private readonly IRelayContext<TRequest, TResponse> _relayContext;
	private readonly IRelayRequestLogger<TRequest, TResponse> _relayRequestLogger;
	private readonly IDistributedCache _cache;
	private readonly RelayServerOptions _relayServerOptions;
	private readonly IRequestCoordinator<TRequest> _requestCoordinator;
	private readonly IRelayClientRequestFactory<TRequest> _requestFactory;
	private readonly IResponseCoordinator<TResponse> _responseCoordinator;
	private readonly IRelayTargetResponseWriter<TResponse> _responseWriter;
	private readonly IEnumerable<ITargetResponseInterceptor<TRequest, TResponse>> _targetResponseInterceptors;
	private readonly ITenantService _tenantService;

	/// <summary>
	/// Initializes a new instance of the <see cref="RelayMiddleware{TRequest,TResponse,TAcknowledge}"/> class.
	/// </summary>
	/// <param name="logger">An <see cref="ILogger{TCategoryName}"/>.</param>
	/// <param name="requestFactory">An <see cref="IRelayClientRequestFactory{TRequest}"/>.</param>
	/// <param name="connectorRegistry">The <see cref="ConnectorRegistry{T}"/>.</param>
	/// <param name="tenantService">An <see cref="ITenantService"/>.</param>
	/// <param name="bodyStore">An <see cref="IBodyStore"/>.</param>
	/// <param name="requestCoordinator">An <see cref="IRequestCoordinator{TRequest}"/>.</param>
	/// <param name="responseWriter">An <see cref="IRelayTargetResponseWriter{T}"/>.</param>
	/// <param name="responseCoordinator">The <see cref="IResponseCoordinator{T}"/>.</param>
	/// <param name="relayContext">An <see cref="IRelayContext{TRequest,TResponse}"/>.</param>
	/// <param name="tenantTransport">An <see cref="ITenantTransport{T}"/>.</param>
	/// <param name="connectorTransportLimit">An <see cref="IConnectorTransportLimit"/>.</param>
	/// <param name="relayServerOptions">An <see cref="IOptions{TOptions}"/>.</param>
	/// <param name="clientRequestInterceptors">An enumeration of <see cref="IClientRequestInterceptor{TRequest,TResponse}"/>.</param>
	/// <param name="targetResponseInterceptors">
	/// An enumeration of <see cref="ITargetResponseInterceptor{TRequest,TResponse}"/>
	/// .
	/// </param>
	/// <param name="relayRequestLogger">An <see cref="IRelayRequestLogger{TRequest,TResponse}"/>.</param>
	/// <param name="cache">An implementation of <see cref="IDistributedCache"/></param>
	public RelayMiddleware(ILogger<RelayMiddleware<TRequest, TResponse, TAcknowledge>> logger,
		IRelayClientRequestFactory<TRequest> requestFactory, ConnectorRegistry<TRequest> connectorRegistry,
		ITenantService tenantService, IBodyStore bodyStore, IRequestCoordinator<TRequest> requestCoordinator,
		IRelayTargetResponseWriter<TResponse> responseWriter, IResponseCoordinator<TResponse> responseCoordinator,
		IRelayContext<TRequest, TResponse> relayContext, ITenantTransport<TRequest> tenantTransport,
		IConnectorTransportLimit connectorTransportLimit, IOptions<RelayServerOptions> relayServerOptions,
		IEnumerable<IClientRequestInterceptor<TRequest, TResponse>> clientRequestInterceptors,
		IEnumerable<ITargetResponseInterceptor<TRequest, TResponse>> targetResponseInterceptors,
		IRelayRequestLogger<TRequest, TResponse> relayRequestLogger, IDistributedCache cache)
	{
		if (relayServerOptions == null) throw new ArgumentNullException(nameof(relayServerOptions));
		if (tenantTransport == null) throw new ArgumentNullException(nameof(tenantTransport));
		if (connectorTransportLimit == null) throw new ArgumentNullException(nameof(connectorTransportLimit));

		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_requestFactory = requestFactory ?? throw new ArgumentNullException(nameof(requestFactory));
		_connectorRegistry = connectorRegistry ?? throw new ArgumentNullException(nameof(connectorRegistry));
		_tenantService = tenantService ?? throw new ArgumentNullException(nameof(tenantService));
		_bodyStore = bodyStore ?? throw new ArgumentNullException(nameof(bodyStore));
		_requestCoordinator = requestCoordinator ?? throw new ArgumentNullException(nameof(requestCoordinator));
		_responseWriter = responseWriter ?? throw new ArgumentNullException(nameof(responseWriter));
		_responseCoordinator = responseCoordinator ?? throw new ArgumentNullException(nameof(responseCoordinator));
		_relayContext = relayContext ?? throw new ArgumentNullException(nameof(relayContext));
		_clientRequestInterceptors = clientRequestInterceptors ??
			throw new ArgumentNullException(nameof(clientRequestInterceptors));
		_targetResponseInterceptors = targetResponseInterceptors ??
			throw new ArgumentNullException(nameof(targetResponseInterceptors));
		_relayRequestLogger = relayRequestLogger ?? throw new ArgumentNullException(nameof(relayRequestLogger));
		_cache = cache ?? throw new ArgumentNullException(nameof(cache));

		_relayServerOptions = relayServerOptions.Value;
		_maximumBodySize = Math.Min(tenantTransport.BinarySizeThreshold.GetValueOrDefault(int.MaxValue),
			connectorTransportLimit.BinarySizeThreshold.GetValueOrDefault(int.MaxValue));
	}

	[LoggerMessage(20600, LogLevel.Warning, "Invalid request received {Path}{Query}, does not contain a tenant id")]
	partial void LogInvalidRequest(string path, QueryString query);

	[LoggerMessage(20601, LogLevel.Information, "Unknown tenant {Tenant} in request received {Path}{Query}")]
	partial void LogUnknownTenant(string tenant, string path, QueryString query);

	[LoggerMessage(20603, LogLevel.Trace, "Received response for request {RequestId}")]
	partial void LogResponseReceived(Guid requestId);

	[LoggerMessage(20604, LogLevel.Debug, "Client aborted request {RequestId}")]
	partial void LogClientAborted(Guid requestId);

	[LoggerMessage(20605, LogLevel.Information, "Request {RequestId} expired")]
	partial void LogRequestExpired(Guid requestId);

	/// <inheritdoc/>
	public async Task InvokeAsync(HttpContext context, RequestDelegate next)
	{
		var tenantName = context.Request.Path.Value?.Split('/').Skip(1).FirstOrDefault();
		if (string.IsNullOrEmpty(tenantName))
		{
			LogInvalidRequest(context.Request.Path, context.Request.QueryString);
			await next.Invoke(context);
			return;
		}

		var tenantId = await LoadTenantByIdAsync(tenantName);
		if (tenantId == null)
		{
			LogUnknownTenant(tenantName, context.Request.Path, context.Request.QueryString);
			await next.Invoke(context);
			return;
		}

		using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
		if (_relayServerOptions.RequestExpiration != null)
		{
			cts.CancelAfter(_relayServerOptions.RequestExpiration.Value);
		}

		_relayContext.ResponseDisposables.Add(_responseCoordinator.RegisterRequest(_relayContext.RequestId));

		try
		{
			context.Request.EnableBuffering();
			await context.Request.Body.DrainAsync(cts.Token);

			_relayContext.ClientRequest =
				await _requestFactory.CreateAsync(tenantId.Value, _relayContext.RequestId, context.Request, cts.Token);

			if (_logger.IsEnabled(LogLevel.Trace))
				_logRequestParsed(_logger, _relayContext.ClientRequest, null);

			await InterceptClientRequestAsync(cts.Token);

			if (_relayContext.TargetResponse == null || _relayContext.ForceConnectorDelivery)
			{
				await DeliverToConnectorAsync(cts.Token);

				if (_relayContext.TargetResponse == null)
				{
					await WaitForConnectorResponseAsync(cts.Token);
				}
			}

			LogResponseReceived(_relayContext.RequestId);
			await InterceptTargetResponseAsync(cts.Token);

			await _relayRequestLogger.LogSuccessAsync(_relayContext);
			await _responseWriter.WriteAsync(_relayContext.TargetResponse, context.Response, cts.Token);
		}
		catch (TransportException)
		{
			await _relayRequestLogger.LogFailAsync(_relayContext);
			await WriteErrorResponse(HttpStatusCode.ServiceUnavailable, context.Response, cts.Token);
		}
		catch (OperationCanceledException)
		{
			if (context.RequestAborted.IsCancellationRequested)
			{
				await _relayRequestLogger.LogAbortAsync(_relayContext);
				LogClientAborted(_relayContext.RequestId);
			}
			else
			{
				await _relayRequestLogger.LogExpiredAsync(_relayContext);
				await WriteErrorResponse(HttpStatusCode.RequestTimeout, context.Response, cts.Token);
				LogRequestExpired(_relayContext.RequestId);
			}
		}
		catch (Exception ex)
		{
			await _relayRequestLogger.LogErrorAsync(_relayContext);
			_logger.LogError(20606, ex, "Could not handle request {RequestId}", _relayContext.RequestId);
		}
	}

	[LoggerMessage(20607, LogLevel.Debug, "Executing client request interceptors for request {RequestId}")]
	partial void LogExecutingRequestInterceptors(Guid requestId);

	[LoggerMessage(20608, LogLevel.Trace, "Executing interceptor {Interceptor} for request {RequestId}")]
	partial void LogExecutingRequestInterceptor(string? interceptor, Guid requestId);

	private async Task InterceptClientRequestAsync(CancellationToken cancellationToken)
	{
		LogExecutingRequestInterceptors(_relayContext.RequestId);

		var bodyContent = _relayContext.HttpContext.Request.Body;

		foreach (var interceptor in _clientRequestInterceptors)
		{
			LogExecutingRequestInterceptor(interceptor.GetType().FullName, _relayContext.RequestId);
			await interceptor.OnRequestReceivedAsync(_relayContext, cancellationToken);

			if (_relayContext.ClientRequest.BodyContent != null && bodyContent != _relayContext.ClientRequest.BodyContent)
			{
				// an interceptor changed the body content - need to dispose it properly
				_relayContext.ResponseDisposables.Add(_relayContext.ClientRequest.BodyContent);
				bodyContent = _relayContext.ClientRequest.BodyContent;
			}
		}
	}

	[LoggerMessage(20609, LogLevel.Trace, "Delivering request {RequestId} to connector")]
	partial void LogDeliveringRequest(Guid requestId);

	private async Task DeliverToConnectorAsync(CancellationToken cancellationToken)
	{
		LogDeliveringRequest(_relayContext.RequestId);

		if (_relayContext.ClientRequest.BodyContent != null &&
		    await TryInlineBodyContentAsync(_relayContext.ClientRequest, cancellationToken))
		{
			_relayContext.ResponseDisposables.Add(_relayContext.ClientRequest.BodyContent);
		}

		if (_relayServerOptions.EnableConnectorTransportShortcut &&
		    await _connectorRegistry.TryDeliverRequestAsync(_relayContext.ClientRequest, cancellationToken)) return;

		await _requestCoordinator.ProcessRequestAsync(_relayContext.ClientRequest, cancellationToken);
	}

	[LoggerMessage(20610, LogLevel.Trace, "Waiting for connector response for request {RequestId}")]
	partial void LogWaitForResponse(Guid requestId);

	private async Task WaitForConnectorResponseAsync(CancellationToken cancellationToken)
	{
		LogWaitForResponse(_relayContext.RequestId);

		var context = await _responseCoordinator.GetResponseAsync(_relayContext.RequestId, cancellationToken);
		if (context == null) return;

		_relayContext.TargetResponse = context.Response;

		if (context.Disposable != null)
		{
			_relayContext.ResponseDisposables.Add(context.Disposable);
		}
	}


	[LoggerMessage(20611, LogLevel.Debug, "Executing target response interceptors for request {RequestId}")]
	partial void LogExecutingResponseInterceptors(Guid requestId);

	[LoggerMessage(20612, LogLevel.Trace, "Executing interceptor {Interceptor} for request {RequestId}")]
	partial void LogExecutingResponseInterceptor(string? interceptor, Guid? requestId);

	private async Task InterceptTargetResponseAsync(CancellationToken cancellationToken)
	{
		LogExecutingResponseInterceptors(_relayContext.RequestId);

		foreach (var interceptor in _targetResponseInterceptors)
		{
			LogExecutingResponseInterceptor(interceptor.GetType().FullName, _relayContext.RequestId);
			await interceptor.OnResponseReceivedAsync(_relayContext, cancellationToken);
		}
	}

	[LoggerMessage(20613, LogLevel.Debug,
		"Outsourcing from request {BodySize} bytes because of a maximum of {BinarySizeThreshold} for request {RequestId}")]
	partial void LogOutsourcingRequestBody(long? bodySize, int binarySizeThreshold, Guid requestId);

	[LoggerMessage(20614, LogLevel.Trace, "Outsourced from request {BodySize} bytes for request {RequestId}")]
	partial void LogOutsourcedRequestBody(long? bodySize, Guid requestId);

	[LoggerMessage(20615, LogLevel.Debug, "Inlined from request {BodySize} bytes for request {RequestId}")]
	partial void LogInlinedRequestBody(long? bodySize, Guid requestId);

	private async Task<bool> TryInlineBodyContentAsync(TRequest request, CancellationToken cancellationToken)
	{
		if (request.BodyContent == null) return false;

		if (request.BodySize > _maximumBodySize)
		{
			LogOutsourcingRequestBody(request.BodySize, _maximumBodySize, request.RequestId);
			await _bodyStore.StoreRequestBodyAsync(request.RequestId, request.BodyContent, cancellationToken);

			request.BodyContent = null;
			LogOutsourcedRequestBody(request.BodySize, request.RequestId);

			return false;
		}

		request.BodyContent = await request.BodyContent.CopyToMemoryStreamAsync(cancellationToken);
		LogInlinedRequestBody(request.BodySize, request.RequestId);

		return true;
	}

	private Task WriteErrorResponse(HttpStatusCode httpStatusCode, HttpResponse response,
		CancellationToken cancellationToken)
		=> _responseWriter.WriteAsync(_relayContext.ClientRequest.CreateResponse<TResponse>(httpStatusCode), response,
			cancellationToken);

	private async Task<Guid?> LoadTenantByIdAsync(string name, CancellationToken cancellationToken = default)
	{
		var normalizedName = _tenantService.NormalizeName(name);
		var cacheKey = $"tenant_{normalizedName}_id";

		var result = Guid.Empty;

		var cachedId = await _cache.GetAsync(cacheKey, cancellationToken);
		if (cachedId != null)
		{
			result = new Guid(cachedId);
			return result == Guid.Empty ? null : result;
		}

		var tenant = await _tenantService.LoadTenantByNameAsync(name);
		if (tenant != null)
		{
			result = tenant.Id;
		}

		var cacheEntryOptions = new DistributedCacheEntryOptions()
			.SetAbsoluteExpiration(_relayServerOptions.TenantIdCacheTimeout);

		cachedId = result.ToByteArray();
		await _cache.SetAsync(cacheKey, cachedId, cacheEntryOptions, cancellationToken);

		return result == Guid.Empty ? null : result;
	}
}
