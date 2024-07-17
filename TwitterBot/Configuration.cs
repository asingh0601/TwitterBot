namespace TwitterBot
{
	internal class Configuration
	{
		public static string Server => Environment.GetEnvironmentVariable("TWITTERBOT_SERVER") ?? string.Empty;
		public static string Port = Environment.GetEnvironmentVariable("TWITTERBOT_PORT") ?? string.Empty;
		public static string DbName = Environment.GetEnvironmentVariable("TWITTERBOT_DBNAME") ?? string.Empty;
		public static string DbUserId = Environment.GetEnvironmentVariable("TWITTERBOT_DBUSERID") ?? string.Empty;
		public static string DbUserPassword = Environment.GetEnvironmentVariable("TWITTERBOT_DBPASSWORD") ?? string.Empty;
		public static string ProxyIp = Environment.GetEnvironmentVariable("TWITTERBOT_PROXYIP") ?? string.Empty;
		public static int ProxyPort = int.Parse(Environment.GetEnvironmentVariable("TWITTERBOT_PROXYPORT") ?? string.Empty);
		public static string ProxyUserName = Environment.GetEnvironmentVariable("TWITTERBOT_PROXYUSERNAME") ?? string.Empty;
		public static string ProxyPassword = Environment.GetEnvironmentVariable("TWITTERBOT_PROXYPASSWORD") ?? string.Empty;
	}
}
