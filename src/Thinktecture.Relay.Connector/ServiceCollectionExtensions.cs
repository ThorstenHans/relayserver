using System;
using System.Net.Http;
using System.Threading;
using IdentityModel.AspNetCore.AccessTokenManagement;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Thinktecture.Relay.Acknowledgement;
using Thinktecture.Relay.Connector;
using Thinktecture.Relay.Connector.Authentication;
using Thinktecture.Relay.Connector.DependencyInjection;
using Thinktecture.Relay.Connector.Options;
using Thinktecture.Relay.Connector.Targets;
using Thinktecture.Relay.Transport;

// ReSharper disable once CheckNamespace; (extension methods on IServiceCollection namespace)
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for the <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
	/// <summary>
	/// Adds the connector to the <see cref="IServiceCollection"/>.
	/// </summary>
	/// <param name="services">The <see cref="IServiceCollection"/>.</param>
	/// <param name="configure">A configure callback for setting the <see cref="RelayConnectorOptions"/>.</param>
	/// <returns>The <see cref="IRelayConnectorBuilder{TRequest,TResponse,TAcknowledge}"/>.</returns>
	public static IRelayConnectorBuilder<ClientRequest, TargetResponse, AcknowledgeRequest> AddRelayConnector(
		this IServiceCollection services,
		Action<RelayConnectorOptions> configure)
		=> services.AddRelayConnector<ClientRequest, TargetResponse, AcknowledgeRequest>(configure);

	/// <summary>
	/// Adds the connector to the <see cref="IServiceCollection"/>.
	/// </summary>
	/// <param name="services">The <see cref="IServiceCollection"/>.</param>
	/// <param name="configure">A configure callback for setting the <see cref="RelayConnectorOptions"/>.</param>
	/// <typeparam name="TRequest">The type of request.</typeparam>
	/// <typeparam name="TResponse">The type of response.</typeparam>
	/// <typeparam name="TAcknowledge">The type of acknowledge.</typeparam>
	/// <returns>The <see cref="IRelayConnectorBuilder{TRequest,TResponse,TAcknowledge}"/>.</returns>
	public static IRelayConnectorBuilder<TRequest, TResponse, TAcknowledge> AddRelayConnector<TRequest, TResponse,
		TAcknowledge>(
		this IServiceCollection services,
		Action<RelayConnectorOptions> configure)
		where TRequest : IClientRequest, new()
		where TResponse : ITargetResponse, new()
		where TAcknowledge : IAcknowledgeRequest, new()
	{
		var builder = new RelayConnectorBuilder<TRequest, TResponse, TAcknowledge>(services);

		builder.Services.Configure(configure);

		builder.Services
			.AddTransient<IConfigureOptions<RelayConnectorOptions>, RelayConnectorConfigureOptions>()
			.AddTransient<IPostConfigureOptions<RelayConnectorOptions>,
				RelayConnectorPostConfigureOptions<TRequest, TResponse>>()
			.AddTransient<IValidateOptions<RelayConnectorOptions>, RelayConnectorValidateOptions>()
			.AddTransient<IConfigureOptions<AccessTokenManagementOptions>, AccessTokenManagementConfigureOptions>()
			.TryAddTransient<IAccessTokenProvider, AccessTokenProvider>();

		builder.Services.AddClientAccessTokenManagement((provider, options) =>
			{
				var clientOptions = provider.GetRequiredService<IOptions<AccessTokenManagementOptions>>().Value.Client;
				options.Clients = clientOptions.Clients;
				options.DefaultClient = clientOptions.DefaultClient;
				options.CacheKeyPrefix = clientOptions.CacheKeyPrefix;
				options.CacheLifetimeBuffer = clientOptions.CacheLifetimeBuffer;
			});

		builder.Services
			.AddHttpClient(Constants.HttpClientNames.RelayServer, (provider, client) =>
			{
				var options = provider.GetRequiredService<IOptions<RelayConnectorOptions>>();
				client.BaseAddress = options.Value.RelayServerBaseUri;
				client.Timeout = options.Value.DiscoveryDocument.EndpointTimeout;
			})
			.AddClientAccessTokenHandler();

		builder.Services
			.AddHttpClient(Constants.HttpClientNames.RelayWebTargetDefault,
				client => client.Timeout = Timeout.InfiniteTimeSpan)
			.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
				{ UseCookies = false, AllowAutoRedirect = false });
		builder.Services
			.AddHttpClient(Constants.HttpClientNames.RelayWebTargetFollowRedirect,
				client => client.Timeout = Timeout.InfiniteTimeSpan)
			.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler() { UseCookies = false });

		builder.Services
			.TryAddSingleton<IClientRequestHandler<TRequest>, ClientRequestHandler<TRequest, TResponse, TAcknowledge>>();
		builder.Services
			.TryAddTransient<IClientRequestWorker<TRequest, TResponse>, ClientRequestWorker<TRequest, TResponse>>();

		builder.Services.AddSingleton<RelayTargetRegistry<TRequest, TResponse>>();
		builder.Services.AddSingleton<RelayConnector>();

		return builder;
	}
}
