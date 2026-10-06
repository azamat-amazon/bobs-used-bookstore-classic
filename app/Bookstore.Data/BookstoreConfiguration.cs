using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace BobsBookstoreClassic.Data
{
    public sealed class BookstoreConfiguration
    {
        private static IConfiguration? _configuration;
        private static BookstoreConfiguration? _instance;
        private static readonly object _lock = new object();

        private static BookstoreConfiguration Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            if (_configuration == null)
                                throw new InvalidOperationException(
                                    "BookstoreConfiguration has not been initialized. Call BookstoreConfiguration.Initialize(IConfiguration) in Program.cs before using settings.");
                            _instance = new BookstoreConfiguration(_configuration);
                        }
                    }
                }
                return _instance;
            }
        }

        private readonly Dictionary<string, string> _appSettings = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _connectionStrings = new Dictionary<string, string>();

        private BookstoreConfiguration(IConfiguration configuration)
        {
            // Load app settings from the configuration pipeline
            var appSettingsSection = configuration.GetSection("AppSettings");
            foreach (var child in appSettingsSection.GetChildren())
            {
                _appSettings[child.Key] = child.Value ?? string.Empty;
                var envValue = Environment.GetEnvironmentVariable(child.Key);
                if (envValue != null)
                {
                    _appSettings[child.Key] = envValue;
                }
            }

            // Load connection strings
            var connectionStringsSection = configuration.GetSection("ConnectionStrings");
            foreach (var child in connectionStringsSection.GetChildren())
            {
                _connectionStrings[child.Key] = child.Value ?? string.Empty;
            }
        }

        /// <summary>
        /// Initialize with an IConfiguration instance. Must be called once at startup (Program.cs)
        /// before any GetSetting / GetConnectionString calls.
        /// </summary>
        public static void Initialize(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            // Reset the instance so it re-reads from the new configuration
            lock (_lock)
            {
                _instance = null;
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