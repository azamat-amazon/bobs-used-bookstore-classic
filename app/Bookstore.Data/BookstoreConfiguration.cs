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

    /// <summary>
    /// Provides application configuration backed by <see cref="IConfiguration"/>.
    /// Register as a singleton in the DI container.
    /// </summary>
    public sealed class BookstoreConfiguration
    {
        private readonly IConfiguration _configuration;
        private readonly Dictionary<string, string> _appSettings = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _connectionStrings = new Dictionary<string, string>();

        public BookstoreConfiguration(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            // Load all key-value pairs from the configuration (equivalent to AppSettings)
            foreach (var kvp in _configuration.AsEnumerable())
            {
                if (kvp.Value != null)
                {
                    _appSettings[kvp.Key] = kvp.Value;
                }
            }

            // Environment variables override configuration values (preserving original behavior)
            foreach (var key in _appSettings.Keys.ToList())
            {
                var envValue = Environment.GetEnvironmentVariable(key);
                if (envValue != null)
                {
                    _appSettings[key] = envValue;
                }
            }

            // Load connection strings
            var connectionStringsSection = _configuration.GetSection("ConnectionStrings");
            foreach (var child in connectionStringsSection.GetChildren())
            {
                _connectionStrings[child.Key] = child.Value;
            }
        }

        public void AddSetting(string key, string value)
        {
            _appSettings[key] = value;
        }

        public string GetSetting(string key)
        {
            return _appSettings[key];
        }

        public T GetSetting<T>(string key)
        {
            var value = _appSettings[key];
            return (T)Convert.ChangeType(value, typeof(T));
        }

        public void AddConnectionString(string key, string value)
        {
            _connectionStrings[key] = value;
        }

        public string GetConnectionString(string key)
        {
            return _connectionStrings[key];
        }
    }
}