using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Thinktecture.Relay.Transport;

namespace Thinktecture.Relay.Connector.RelayTargets
{
	internal class ClientRequestHandler<TRequest, TResponse> : IClientRequestHandler<TRequest, TResponse>, IDisposable
		where TRequest : IClientRequest
		where TResponse : ITargetResponse
	{
		private readonly Dictionary<string, RelayTargetRegistration<TRequest, TResponse>> _targets
			= new Dictionary<string, RelayTargetRegistration<TRequest, TResponse>>(StringComparer.InvariantCultureIgnoreCase);

		private readonly IServiceProvider _serviceProvider;
		private readonly HttpClient _httpClient;
		private readonly Uri _requestEndpoint;
		private readonly Uri _responseEndpoint;

		private class CountingStreamContent : StreamContent
		{
			private readonly int _bufferSize;
			private readonly Stream _content;

			public long BytesWritten { get; private set; }

			public CountingStreamContent(Stream content, int bufferSize = 80 * 1024) : base(content, bufferSize)
			{
				_content = content;
				_bufferSize = bufferSize;
			}

			protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
			{
				var buffer = new byte[_bufferSize];

				while (true)
				{
					var length = await _content.ReadAsync(buffer, 0, buffer.Length);
					if (length <= 0)
					{
						break;
					}

					BytesWritten += length;

					await stream.WriteAsync(buffer, 0, length);
				}
			}
		}

		public ClientRequestHandler(IServiceProvider serviceProvider, IEnumerable<RelayTargetRegistration<TRequest, TResponse>> targets,
			IHttpClientFactory httpClientFactory, IOptions<RelayConnectorOptions> options)
		{
			if (httpClientFactory == null)
			{
				throw new ArgumentNullException(nameof(httpClientFactory));
			}

			if (options == null)
			{
				throw new ArgumentNullException(nameof(options));
			}

			_serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
			_httpClient = httpClientFactory.CreateClient(Constants.RelayServerHttpClientName);
			_requestEndpoint = new Uri($"{options.Value.DiscoveryDocument.RequestEndpoint}/");
			_responseEndpoint = new Uri($"{options.Value.DiscoveryDocument.ResponseEndpoint}/");

			foreach (var target in targets)
			{
				_targets[target.Id] = target;
			}
		}

		public async Task<TResponse> HandleAsync(TRequest request, int? binarySizeThreshold, CancellationToken cancellationToken = default)
		{
			if (!TryGetTarget(request.Target, out var target) && !TryGetTarget(Constants.RelayTargetCatchAllId, out target))
			{
				return default;
			}

			if (request.BodySize > 0 && request.BodyContent == null)
			{
				request.BodyContent = await _httpClient.GetStreamAsync(new Uri(_requestEndpoint, request.RequestId.ToString("N")));
			}

			try
			{
				var response = await target.HandleAsync(request, cancellationToken);

				if (response.BodySize == null || response.BodySize > binarySizeThreshold)
				{
					using var content = new CountingStreamContent(response.BodyContent);
					await _httpClient.PostAsync(new Uri(_responseEndpoint, response.RequestId.ToString("N")), content, cancellationToken);

					// TODO error handling when post fails

					response.BodySize = content.BytesWritten;
					response.BodyContent = Stream.Null; // stream was disposed by stream content already - no need to keep it
				}
				else
				{
					using var _ = response.BodyContent;
					response.BodyContent = await response.BodyContent.CopyToMemoryStreamAsync(cancellationToken);
				}

				return response;
			}
			finally
			{
				(target as IDisposable)?.Dispose();
			}
		}

		private bool TryGetTarget(string id, out IRelayTarget<TRequest, TResponse> target)
		{
			if (_targets.TryGetValue(id, out var registration))
			{
				target = registration.Factory(_serviceProvider);
				return true;
			}

			target = null;
			return false;
		}

		public void Dispose() => _httpClient.Dispose();
	}
}