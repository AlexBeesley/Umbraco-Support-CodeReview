using Azure.Monitor.OpenTelemetry.AspNetCore;
using CorporateWebsite.Core.Models;
using CorporateWebsite.Umbraco.Web.Middleware.Security;
using CorporateWebsite.Umbraco.Web.Middleware.StaticFiles;
using CorporateWebsite.Umbraco.Web.Observability;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;
using ThePensionsRegulator.Frontend.Umbraco;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

namespace CorporateWebsite.Umbraco.Web
{
	public class Startup
	{
		private readonly IWebHostEnvironment _env;
		private readonly IConfiguration _config;

		public Startup(IWebHostEnvironment webHostEnvironment, IConfiguration config)
		{
			_env = webHostEnvironment ?? throw new ArgumentNullException(nameof(webHostEnvironment));
			_config = config ?? throw new ArgumentNullException(nameof(config));
		}

		public void ConfigureServices(IServiceCollection services)
		{
			var useBlobStorageForMedia = !string.IsNullOrEmpty(_config["Umbraco:Storage:AzureBlob:Media:ConnectionString"]);
			services.AddTprFrontendUmbraco(opt => opt.RenderWidthContainerForBlocks = true, opt => opt.UpdateDestinationHostnames = []);

#if DEBUG
			services.AddCors(options =>
			{
				options.AddPolicy("LocalDevelopment", policy =>
				{
					policy.WithOrigins("https://localhost:44350")
						.AllowAnyHeader();
				});
			});
#endif
			var builder = services
				.AddUmbraco(_env, _config)
				.AddBackOffice()
				.AddWebsite()
				.AddComposers();

			if (useBlobStorageForMedia)
			{
				builder
				.AddAzureBlobMediaFileSystem()
				.AddAzureBlobImageSharpCache();
			}

			var openTelemetry = services.AddOpenTelemetry();
			builder.Services.ConfigureOpenTelemetryTracerProvider(tracer => tracer.AddProcessor<FilterRequestTelemetryProcessor>());
			if (_env.IsProduction())
			{
				openTelemetry.UseAzureMonitor();
			}


			builder.Build();
		}

		public void Configure(IApplicationBuilder app, IOptions<AppConfig> appConfig, IOptions<MvcOptions> mvcOptions, IUmbracoContextAccessor umbracoContextAccessor, IPublishedValueFallback publishedValueFallback)
		{
			if (appConfig.Value.EnableMediaRedirects)
			{
				var mediaRedirectsPath = Path.Combine(_env.ContentRootPath, "media-redirects.xml");
				using (var mediaRedirectsStreamReader = File.OpenText(mediaRedirectsPath))
				{
					var options = new RewriteOptions()
						.AddIISUrlRewrite(mediaRedirectsStreamReader);

					app.UseRewriter(options);
				}
			}
			if (_config.GetValue<bool>("Umbraco:CMS:Hosting:Debug"))
			{
				app.UseDeveloperExceptionPage();
			}
			else
			{
				app.UseStatusCodePagesWithReExecute("/error/{0}");
				app.UseExceptionHandler("/error/500");
			}

			app.UseExtendedStaticFiles(appConfig);
			app.UseHttpsRedirection();
			app.UseSecurityHeaders();
			app.UseWhitelistedReferrerSecurityHeaders();
			app.UseSession();



			app.UseUmbraco()
				.WithMiddleware(u =>
				{
					if (appConfig.Value.EnableBackoffice) { u.UseBackOffice(); }

#if DEBUG
					app.UseCors("LocalDevelopment");
#endif

					u.UseWebsite();
				})
				.WithEndpoints(u =>
				{
					u.UseInstallerEndpoints();
					if (appConfig.Value.EnableBackoffice) { u.UseBackOfficeEndpoints(); }
					u.UseWebsiteEndpoints();
				});

			app.UseTprFrontendUmbraco(mvcOptions, umbracoContextAccessor, publishedValueFallback);


		}
	}
}
