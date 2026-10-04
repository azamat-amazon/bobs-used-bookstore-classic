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
        /// Initializes the configuration from an <see cref="IConfiguration"/> source.
        /// Call this once at application startup (e.g. in Program.cs) after building the host.
        /// </summary>
        // PORT-TODO
        // {"note": "Call BookstoreConfiguration.Initialize(configuration) in Program.cs at startup after building IConfiguration. Long-term, callers of GetSetting/GetConnectionString should migrate to inject IConfiguration or IOptions<T> directly.", "files": ["Program.cs"]}
        public static void Initialize(IConfiguration configuration)
        {
            foreach (var kvp in configuration.AsEnumerable())
            {
                if (kvp.Value != null)
                {
                    Instance._appSettings[kvp.Key] = kvp.Value;
                }
                // Environment variables override config file values
                if (Environment.GetEnvironmentVariable(kvp.Key) != null)
                {
                    Instance._appSettings[kvp.Key] = Environment.GetEnvironmentVariable(kvp.Key);
                }
            }

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