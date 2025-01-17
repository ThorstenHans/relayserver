using System;
using System.Collections.Generic;
using System.Linq;
using RabbitMQ.Client;

namespace Thinktecture.Relay.Server.Protocols.RabbitMq;

/// <inheritdoc/>
public class RoundRobinEndpointResolver : IEndpointResolver
{
	private readonly AmqpTcpEndpoint[] _endpoints;

	/// <summary>
	/// Initializes a new instance of the <see cref="RoundRobinEndpointResolver"/> class.
	/// </summary>
	/// <param name="endpoints">The <see cref="AmqpTcpEndpoint"/>s to use.</param>
	public RoundRobinEndpointResolver(IEnumerable<AmqpTcpEndpoint> endpoints)
	{
		if (endpoints == null) throw new ArgumentNullException(nameof(endpoints));

		_endpoints = endpoints.ToArray();
	}

	/// <inheritdoc/>
	public IEnumerable<AmqpTcpEndpoint> All()
		=> _endpoints;
}
