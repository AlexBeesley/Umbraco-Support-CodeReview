using CorporateWebsite.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Sync;

namespace CorporateWebsite.Umbraco.Web.ServerRoles
{
	public sealed class ConfigurableServerRoleAccessor : IServerRoleAccessor
	{
		public ServerRole CurrentServerRole { get; }

		public ConfigurableServerRoleAccessor(
			IConfiguration configuration,
			IOptions<AppConfig> appConfig,
			ILogger<ConfigurableServerRoleAccessor> logger)
		{
			var configuredRole = configuration.GetValue<string>("Umbraco:CMS:Hosting:ServerRole");

			if (!string.IsNullOrWhiteSpace(configuredRole) &&
				Enum.TryParse<ServerRole>(configuredRole, ignoreCase: true, out var role) &&
				role != ServerRole.Unknown)
			{
				CurrentServerRole = role;
				logger.LogInformation("Server role explicitly configured as {ServerRole}.", CurrentServerRole);
			}
			else
			{
				CurrentServerRole = appConfig.Value.EnableBackoffice
					? ServerRole.SchedulingPublisher
					: ServerRole.Subscriber;

				logger.LogInformation(
					"No explicit server role configured. Determined role as {ServerRole} based on EnableBackoffice={EnableBackoffice}.",
					CurrentServerRole,
					appConfig.Value.EnableBackoffice);
			}
		}
	}

	public sealed class SchedulingPublisherServerRoleAccessor : IServerRoleAccessor
	{
		public ServerRole CurrentServerRole => ServerRole.SchedulingPublisher;
	}

	public sealed class SubscriberServerRoleAccessor : IServerRoleAccessor
	{
		public ServerRole CurrentServerRole => ServerRole.Subscriber;
	}
}
