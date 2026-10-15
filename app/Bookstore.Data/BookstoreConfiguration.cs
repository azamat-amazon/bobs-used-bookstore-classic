using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace BobsBookstoreClassic.Data
{
    public sealed class BookstoreConfiguration
    {
        private static readonly Lazy<BookstoreConfiguration> Lazy = new Lazy<BookstoreConfiguration>(() => new BookstoreConfiguration());
        private static BookstoreConfiguration Instance => Lazy.Value;

        private readonly Dictionary<string, string> _appSettings = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _connectionStrings = new Dictionary<string, string>();
        private BookstoreConfiguration()
        {
        }

        /// <summary>
        /// Initializes the singleton from an <see cref="IConfiguration"/> source
        /// (typically called once at application startup).
        /// </summary>
        // PORT-TODO
        // {"note": "Call BookstoreConfiguration.Initialize(configuration) in Program.cs at startup so the singleton is populated from IConfiguration (appsettings.json). Without this call, GetSetting/GetConnectionString will throw KeyNotFoundException at runtime.", "files": ["Program.cs"]}
        public static void Initialize(IConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            // Load AppSettings from the "AppSettings" section (mirrors the legacy AppSettings XML section)
            var appSettingsSection = configuration.GetSection("AppSettings");
            foreach (var child in appSettingsSection.GetChildren())
            {
                Instance._appSettings[child.Key] = child.Value;
                // Environment variable overrides, preserving original behavior
                if (Environment.GetEnvironmentVariable(child.Key) != null)
                {
                    Instance._appSettings[child.Key] = Environment.GetEnvironmentVariable(child.Key);
                }
            }

            // Load ConnectionStrings
            var connectionStringsSection = configuration.GetSection("ConnectionStrings");
            foreach (var child in connectionStringsSection.GetChildren())
            {
                Instance._connectionStrings[child.Key] = child.Value;
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