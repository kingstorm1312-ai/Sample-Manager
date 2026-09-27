using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace SampleManager
{
    internal sealed class GatewaySettings
    {
        private GatewaySettings(string endpoint, string secret)
        {
            Endpoint = endpoint;
            Secret = secret;
        }

        public string Endpoint { get; private set; }
        public string Secret { get; private set; }

        public static GatewaySettings Load()
        {
            string endpoint = Environment.GetEnvironmentVariable("SAMPLE_MANAGER_GATEWAY_URL");
            string secret = Environment.GetEnvironmentVariable("SAMPLE_MANAGER_GATEWAY_SECRET");
            if (String.IsNullOrWhiteSpace(endpoint) || String.IsNullOrWhiteSpace(secret))
            {
                IDictionary<string, object> values = ReadLocalConfig();
                if (String.IsNullOrWhiteSpace(endpoint)) endpoint = Optional(values, "gatewayUrl");
                if (String.IsNullOrWhiteSpace(secret)) secret = Optional(values, "gatewaySecret");
            }

            Uri parsed;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out parsed)
                || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(
                    "Chưa cấu hình write gateway. Tạo gateway-config.json trong %LOCALAPPDATA%\\CPC\\Sample Manager.");
            }
            if (String.IsNullOrWhiteSpace(secret))
            {
                throw new InvalidOperationException(
                    "Write gateway thiếu gateway secret trong cấu hình máy này.");
            }

            return new GatewaySettings(endpoint.Trim(), secret.Trim());
        }

        public static string LocalConfigPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CPC",
                    "Sample Manager",
                    "gateway-config.json");
            }
        }

        private static IDictionary<string, object> ReadLocalConfig()
        {
            if (!File.Exists(LocalConfigPath))
            {
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                object parsed = new JavaScriptSerializer().DeserializeObject(
                    File.ReadAllText(LocalConfigPath, Encoding.UTF8));
                IDictionary<string, object> values = parsed as IDictionary<string, object>;
                return values ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Không đọc được cấu hình write gateway.", exception);
            }
        }

        private static string Optional(IDictionary<string, object> values, string key)
        {
            object value;
            return values != null && values.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value).Trim()
                : String.Empty;
        }
    }
}
