using System;
using System.Threading;
using System.Threading.Tasks;
using IdentityModel;
using IdentityModel.AspNetCore.AccessTokenManagement;
using IdentityModel.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Thinktecture.Relay.Connector.Options;

internal class AccessTokenManagementConfigureOptions : IConfigureOptions<AccessTokenManagementOptions>
{
	private readonly IHostApplicationLifetime _hostApplicationLifetime;
	private readonly ILogger<AccessTokenManagementConfigureOptions> _logger;
	private readonly RelayConnectorOptions _relayConnectorOptions;

	public AccessTokenManagementConfigureOptions(ILogger<AccessTokenManagementConfigureOptions> logger,
		IHostApplicationLifetime hostApplicationLifetime, IOptions<RelayConnectorOptions> relayConnectorOptions)
	{
		if (relayConnectorOptions == null) throw new ArgumentNullException(nameof(relayConnectorOptions));

		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_hostApplicationLifetime =
			hostApplicationLifetime ?? throw new ArgumentNullException(nameof(hostApplicationLifetime));
		_relayConnectorOptions = relayConnectorOptions.Value;
	}

	public void Configure(AccessTokenManagementOptions options)
	{
		var urlString = _relayConnectorOptions.DiscoveryDocument.AuthorizationServer;
		if (!urlString.EndsWith('/'))
		{
			urlString += '/';
		}

		var uri = new Uri(new Uri(urlString), OidcConstants.Discovery.DiscoveryEndpoint);

		while (!_hostApplicationLifetime.ApplicationStopping.IsCancellationRequested)
		{
			var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(uri.ToString(),
				new OpenIdConnectConfigurationRetriever(),
				new HttpDocumentRetriever() { RequireHttps = uri.Scheme == "https" });

			try
			{
				var configuration = configManager.GetConfigurationAsync(CancellationToken.None).GetAwaiter().GetResult();
				_logger.LogTrace(10200, "Got discovery document from {DiscoveryDocumentUrl} ({@DiscoveryDocument})", uri,
					configuration);

				options.Client.Clients.Add(Constants.HttpClientNames.RelayServer, new ClientCredentialsTokenRequest()
				{
					Address = configuration.TokenEndpoint,
					ClientId = _relayConnectorOptions.TenantName,
					ClientSecret = _relayConnectorOptions.TenantSecret,
					Scope = Constants.RelayServerScopes,
				});
				break;
			}
			catch (Exception ex)
			{
				_logger.LogError(10201, ex,
					"An error occured while retrieving the discovery document from {DiscoveryDocumentUrl}",
					uri);

				try
				{
					Task.Delay(TimeSpan.FromSeconds(10), _hostApplicationLifetime.ApplicationStopping).GetAwaiter()
						.GetResult();
				}
				catch (OperationCanceledException)
				{
					// Ignore this, as this will be thrown when the service shuts down gracefully
				}
			}
		}
	}
}
