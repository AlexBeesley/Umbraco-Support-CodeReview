using CorporateWebsite.Umbraco.Web.ServerRoles;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Infrastructure.DependencyInjection;

namespace CorporateWebsite.Umbraco.Web.Composers
{
	public sealed class ServerRoleComposer : IComposer
	{
		public void Compose(IUmbracoBuilder builder)
		{
			builder.SetServerRegistrar<ConfigurableServerRoleAccessor>();
		}
	}
}
