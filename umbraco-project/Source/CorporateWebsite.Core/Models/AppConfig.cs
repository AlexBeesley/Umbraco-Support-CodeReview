namespace CorporateWebsite.Core.Models
{
	public class AppConfig
	{
		public required string GtmContainerId { get; set; }
		public required string CorporateSiteBaseUrl { get; set; }
		public bool EnableBackoffice { get; set; }
		public bool EnableReverseProxy { get; set; }
		public bool EnableMediaRedirects { get; set; }
		public string RedisInstanceName { get; set; } = "";
		public IEnumerable<FileExtension>? AdditionalFileExtensions { get; set; }
		public required string ExternalBlogUrl { get; set; }
	}

	public class FileExtension
	{
		public required string Extension { get; set; }
		public required string MimeType { get; set; }
	}
}
