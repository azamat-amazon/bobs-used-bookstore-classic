using Microsoft.Extensions.Configuration;

namespace BobsBookstoreClassic.Data
{
    public sealed class BookstoreConfiguration
    {
        private static readonly BookstoreConfiguration Instance = new BookstoreConfiguration();

        private readonly Dictionary<string, string> _appSettings = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _connectionStrings = new Dictionary<string, string>();

        private BookstoreConfiguration() { }

        /// <summary>
        /// Call once at startup (e.g. in Program.cs) with the host IConfiguration.
        /// </summary>
        public static void Initialize(IConfiguration configuration)
        {
            foreach (var kvp in configuration.AsEnumerable())
            {
                if (kvp.Value != null)
                {
                    Instance._appSettings[kvp.Key] = kvp.Value;
                }
            }

            var connStrings = configuration.GetSection("ConnectionStrings");
            foreach (var child in connStrings.GetChildren())
            {
                if (child.Value != null)
                {
                    Instance._connectionStrings[child.Key] = child.Value;
                }
            }

            // Environment variable overrides
            foreach (var key in Instance._appSettings.Keys.ToList())
            {
                var envValue = Environment.GetEnvironmentVariable(key);
                if (envValue != null)
                {
                    Instance._appSettings[key] = envValue;
                }
            }
        }

        public static void AddSetting(string key, string value)
        {
            Instance._appSettings[key] = value;
        }

        public static string GetSetting(string key)
        {
            return Instance._appSettings[key];
        }

        public static T GetSetting<T>(string key)
        {
            var value = Instance._appSettings[key];
            return (T)Convert.ChangeType(value, typeof(T));
        }

        public static void AddConnectionString(string key, string value)
        {
            Instance._connectionStrings[key] = value;
        }

        public static string GetConnectionString(string key)
        {
            return Instance._connectionStrings[key];
        }
    }
}