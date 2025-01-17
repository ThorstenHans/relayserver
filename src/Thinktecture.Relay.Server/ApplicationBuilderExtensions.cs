using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Thinktecture.Relay.Acknowledgement;
using Thinktecture.Relay.Server.DependencyInjection;
using Thinktecture.Relay.Server.Middleware;
using Thinktecture.Relay.Transport;

// ReSharper disable once CheckNamespace; (extension methods on IApplicationBuilder namespace)
namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Extension methods for the <see cref="IApplicationBuilder"/>.
/// </summary>
public static class ApplicationBuilderExtensions
{
	/// <summary>
	/// Adds RelayServer to the application's request pipeline.
	/// </summary>
	/// <param name="builder">The <see cref="IApplicationBuilder"/> instance.</param>
	/// <returns>The <see cref="IApplicationBuilder"/> instance.</returns>
	public static IApplicationBuilder UseRelayServer(this IApplicationBuilder builder)
		=> builder.UseRelayServer<ClientRequest, TargetResponse, AcknowledgeRequest>();

	/// <summary>
	/// Adds RelayServer to the application's request pipeline.
	/// </summary>
	/// <param name="builder">The <see cref="IApplicationBuilder"/> instance.</param>
	/// <typeparam name="TRequest">The type of request.</typeparam>
	/// <typeparam name="TResponse">The type of response.</typeparam>
	/// <typeparam name="TAcknowledge">The type of acknowledge.</typeparam>
	/// <returns>The <see cref="IApplicationBuilder"/> instance.</returns>
	public static IApplicationBuilder UseRelayServer<TRequest, TResponse, TAcknowledge>(this IApplicationBuilder builder)
		where TRequest : IClientRequest
		where TResponse : class, ITargetResponse, new()
		where TAcknowledge : IAcknowledgeRequest
	{
		builder.Map("/relay", app => app.UseMiddleware<RelayMiddleware<TRequest, TResponse, TAcknowledge>>());
		builder.Map("/health", app =>
		{
			app.UseHealthChecks("/ready", new HealthCheckOptions() { Predicate = check => check.Tags.Contains("ready") });
			app.UseHealthChecks("/live", new HealthCheckOptions() { Predicate = _ => false });
		});

		foreach (var part in builder.ApplicationServices.GetServices<IApplicationBuilderPart>())
		{
			part.Use(builder);
		}

		return builder;
	}
}
