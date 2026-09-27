using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace SampleManager
{
    internal static class GoogleOAuth
    {
        private const string SheetsScope = "https://www.googleapis.com/auth/spreadsheets";
        private const int TimeoutMilliseconds = 20000;

        public static string GetAccessToken(string credentialsPath, string tokenPath)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            IDictionary<string, object> client = ReadObject(credentialsPath);
            IDictionary<string, object> installed = GetObject(client, "installed");
            string clientId = Required(installed, "client_id");
            string clientSecret = Required(installed, "client_secret");
            string tokenUri = Required(installed, "token_uri");
            IDictionary<string, object> token = ReadObject(tokenPath);
            if (!String.Equals(clientId, Optional(token, "client_id"), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("OAuth token does not match the copied desktop client.");
            }
            if (!HasScope(token, SheetsScope))
            {
                throw new InvalidOperationException("Copied OAuth token does not contain Google Sheets scope.");
            }

            string accessToken = Optional(token, "token");
            DateTime expiry;
            bool parsedExpiry = DateTime.TryParse(
                Optional(token, "expiry"),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out expiry);
            if (!String.IsNullOrWhiteSpace(accessToken)
                && (!parsedExpiry || expiry > DateTime.UtcNow.AddMinutes(1)))
            {
                return accessToken;
            }

            string refreshToken = Required(token, "refresh_token");
            string body = "client_id=" + Uri.EscapeDataString(clientId)
                + "&client_secret=" + Uri.EscapeDataString(clientSecret)
                + "&refresh_token=" + Uri.EscapeDataString(refreshToken)
                + "&grant_type=refresh_token";
            byte[] payload = Encoding.UTF8.GetBytes(body);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(tokenUri);
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            request.ContentLength = payload.Length;
            request.Timeout = TimeoutMilliseconds;
            request.ReadWriteTimeout = TimeoutMilliseconds;
            using (Stream stream = request.GetRequestStream())
            {
                stream.Write(payload, 0, payload.Length);
            }
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream()))
            {
                IDictionary<string, object> result = ParseObject(reader.ReadToEnd());
                string refreshed = Optional(result, "access_token");
                if (String.IsNullOrWhiteSpace(refreshed))
                {
                    throw new InvalidOperationException("OAuth refresh returned no access token.");
                }
                return refreshed;
            }
        }

        private static bool HasScope(IDictionary<string, object> token, string requiredScope)
        {
            object raw;
            object[] scopes;
            if (!token.TryGetValue("scopes", out raw) || (scopes = raw as object[]) == null)
            {
                return false;
            }
            for (int index = 0; index < scopes.Length; index++)
            {
                if (String.Equals(Convert.ToString(scopes[index], CultureInfo.InvariantCulture), requiredScope, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static IDictionary<string, object> ReadObject(string path)
        {
            if (!File.Exists(path))
            {
                throw new InvalidOperationException("OAuth file is missing: " + path);
            }
            return ParseObject(File.ReadAllText(path, Encoding.UTF8));
        }

        private static IDictionary<string, object> ParseObject(string json)
        {
            object value = new JavaScriptSerializer().DeserializeObject(json);
            IDictionary<string, object> result = value as IDictionary<string, object>;
            if (result == null)
            {
                throw new InvalidOperationException("OAuth JSON is not an object.");
            }
            return result;
        }

        private static IDictionary<string, object> GetObject(IDictionary<string, object> source, string key)
        {
            object value;
            IDictionary<string, object> result;
            if (!source.TryGetValue(key, out value) || (result = value as IDictionary<string, object>) == null)
            {
                throw new InvalidOperationException("OAuth JSON object is missing: " + key);
            }
            return result;
        }

        private static string Required(IDictionary<string, object> source, string key)
        {
            string value = Optional(source, key);
            if (String.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("OAuth JSON value is missing: " + key);
            }
            return value;
        }

        private static string Optional(IDictionary<string, object> source, string key)
        {
            object value;
            return source.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value, CultureInfo.InvariantCulture).Trim()
                : String.Empty;
        }
    }
}
