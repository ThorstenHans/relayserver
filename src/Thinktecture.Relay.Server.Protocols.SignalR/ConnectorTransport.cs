using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Thinktecture.Relay.Acknowledgement;
using Thinktecture.Relay.Server.Transport;
using Thinktecture.Relay.Transport;

namespace Thinktecture.Relay.Server.Protocols.SignalR;

/// <inheritdoc/>
// ReSharper disable once ClassNeverInstantiated.Global
public class ConnectorTransport<TRequest, TResponse, TAcknowledge> : IConnectorTransport<TRequest>
	where TRequest : IClientRequest
	where TResponse : ITargetResponse
	where TAcknowledge : IAcknowledgeRequest
{
	private readonly string _connectionId;
	private readonly IHubContext<ConnectorHub<TRequest, TResponse, TAcknowledge>, IConnector<TRequest>> _hubContext;
	private readonly ILogger<ConnectorTransport<TRequest, TResponse, TAcknowledge>> _logger;

	private readonly Action<ILogger, IClientRequest, Guid, string, Exception?> _logTransportingRequest =
		LoggerMessage.Define<IClientRequest, Guid, string>(LogLevel.Trace, 26200,
			"Transporting request {@Request} for request {RequestId} on connection {ConnectionId}");

	/// <summary>
	/// Initializes a new instance of the <see cref="ConnectorTransport{TRequest,TResponse,TAcknowledge}"/> class.
	/// </summary>
	/// <param name="logger">An <see cref="ILogger{TCategoryName}"/>.</param>
	/// <param name="connectionId">The unique id of the connection.</param>
	/// <param name="hubContext">An <see cref="IHubContext{THub}"/>.</param>
	public ConnectorTransport(ILogger<ConnectorTransport<TRequest, TResponse, TAcknowledge>> logger, string connectionId,
		IHubContext<ConnectorHub<TRequest, TResponse, TAcknowledge>, IConnector<TRequest>> hubContext)
	{
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_connectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
		_hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
	}

	/// <inheritdoc/>
	public async Task TransportAsync(TRequest request, CancellationToken cancellationToken = default)
	{
		if (_logger.IsEnabled(LogLevel.Trace))
			_logTransportingRequest(_logger, request, request.RequestId, _connectionId, null);

		try
		{
			await _hubContext.Clients.Client(_connectionId).RequestTarget(request);
		}
		catch (Exception ex)
		{
			_logger.LogError(26201, ex,
				"An error occured while transporting request {RequestId} on connection {ConnectionId}",
				request.RequestId,
				_connectionId);
		}
	}
}
