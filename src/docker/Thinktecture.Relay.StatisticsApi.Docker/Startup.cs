using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Thinktecture.Relay.Server.Persistence.EntityFrameworkCore.PostgreSql;

namespace Thinktecture.Relay.StatisticsApi.Docker;

internal class Startup
{
	public IConfiguration Configuration { get; }

	public Startup(IConfiguration configuration)
		=> Configuration = configuration;

	// This method gets called by the runtime. Use this method to add services to the container.
	public void ConfigureServices(IServiceCollection services)
	{
		services.AddControllers();

		services.AddRelayServerDbContext(Configuration.GetConnectionString("PostgreSql"));
	}

	// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
	public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
	{
		if (env.IsDevelopment())
		{
			app.UseDeveloperExceptionPage();
		}
		else
		{
			app.UseHttpsRedirection();
		}

		app.UseRouting();

		app.UseAuthorization();
		app.UseAuthentication();

		app.UseEndpoints(endpoints => endpoints.MapControllers());
	}
}
