using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SampleManager
{
    internal sealed class SampleGatewayResult
    {
        public string OperationId { get; set; }
        public string OperationType { get; set; }
        public IDictionary<string, object> Result { get; set; }
    }

    internal sealed class SampleGatewayException : InvalidOperationException
    {
        public SampleGatewayException(string code, string message)
            : base(message)
        {
            Code = code ?? String.Empty;
        }

        public string Code { get; private set; }
        public bool IsConflict { get { return String.Equals(Code, "CONFLICT", StringComparison.Ordinal); } }
        public bool IsClaimed { get { return String.Equals(Code, "CLAIMED", StringComparison.Ordinal); } }
    }

    internal sealed class SampleWriteGatewayClient
    {
        private const int TimeoutMilliseconds = 30000;
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        private GatewaySettings settings;

        public async Task<SampleGatewayResult> ExecuteAsync(
            string operationId,
            string operationType,
            IDictionary<string, object> payload)
        {
            if (String.IsNullOrWhiteSpace(operationId)) throw new ArgumentException("operationId");
            if (String.IsNullOrWhiteSpace(operationType)) throw new ArgumentException("operationType");
            if (payload == null) throw new ArgumentNullException("payload");
            settings = GatewaySettings.Load();

            IDictionary<string, object> body = new Dictionary<string, object>();
            body["operationId"] = operationId;
            body["operationType"] = operationType;
            body["payloadHash"] = ComputeSha256(CanonicalJson(payload));
            body["gatewaySecret"] = settings.Secret;
            body["payload"] = payload;

            string responseJson = await PostAsync(serializer.Serialize(body));
            IDictionary<string, object> response = DeserializeObject(responseJson);
            object okValue;
            bool ok = response.TryGetValue("ok", out okValue) && Convert.ToBoolean(okValue);
            if (!ok)
            {
                string code = GetString(response, "code");
                string message = GetString(response, "message");
                throw new SampleGatewayException(
                    code,
                    String.IsNullOrWhiteSpace(message) ? "Write gateway từ chối mutation." : message);
            }

            return new SampleGatewayResult
            {
                OperationId = GetString(response, "operationId"),
                OperationType = GetString(response, "operationType"),
                Result = GetObject(response, "result")
            };
        }

        private async Task<string> PostAsync(string json)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            byte[] payload = Encoding.UTF8.GetBytes(json);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(settings.Endpoint);
            request.Method = "POST";
            request.Timeout = TimeoutMilliseconds;
            request.ReadWriteTimeout = TimeoutMilliseconds;
            request.ContentType = "application/json; charset=utf-8";
            request.Accept = "application/json";
            request.ContentLength = payload.Length;

            using (Stream stream = await request.GetRequestStreamAsync())
            {
                await stream.WriteAsync(payload, 0, payload.Length);
            }

            HttpWebResponse response = null;
            try
            {
                response = (HttpWebResponse)await request.GetResponseAsync();
            }
            catch (WebException exception)
            {
                response = exception.Response as HttpWebResponse;
                if (response == null) throw;
            }

            using (response)
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
            {
                string responseJson = await Task.Run(() => reader.ReadToEnd());
                if (String.IsNullOrWhiteSpace(responseJson))
                {
                    throw new InvalidOperationException("Write gateway trả về response rỗng.");
                }
                return responseJson;
            }
        }

        private IDictionary<string, object> DeserializeObject(string json)
        {
            object value = serializer.DeserializeObject(json);
            IDictionary<string, object> result = value as IDictionary<string, object>;
            if (result == null) throw new InvalidOperationException("Write gateway trả về JSON không hợp lệ.");
            return result;
        }

        private static IDictionary<string, object> GetObject(IDictionary<string, object> source, string key)
        {
            object value;
            IDictionary<string, object> result;
            if (!source.TryGetValue(key, out value)
                || (result = value as IDictionary<string, object>) == null)
            {
                return new Dictionary<string, object>();
            }
            return result;
        }

        private static string GetString(IDictionary<string, object> source, string key)
        {
            object value;
            return source.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value).Trim()
                : String.Empty;
        }

        private static string CanonicalJson(object value)
        {
            IDictionary<string, object> dictionary = value as IDictionary<string, object>;
            if (dictionary != null)
            {
                List<string> keys = new List<string>(dictionary.Keys);
                keys.Sort(StringComparer.Ordinal);
                StringBuilder builder = new StringBuilder();
                builder.Append('{');
                for (int index = 0; index < keys.Count; index++)
                {
                    if (index > 0) builder.Append(',');
                    builder.Append(new JavaScriptSerializer().Serialize(keys[index]));
                    builder.Append(':');
                    builder.Append(CanonicalJson(dictionary[keys[index]]));
                }
                builder.Append('}');
                return builder.ToString();
            }

            IList list = value as IList;
            if (list != null)
            {
                StringBuilder builder = new StringBuilder();
                builder.Append('[');
                for (int index = 0; index < list.Count; index++)
                {
                    if (index > 0) builder.Append(',');
                    builder.Append(CanonicalJson(list[index]));
                }
                builder.Append(']');
                return builder.ToString();
            }

            return new JavaScriptSerializer().Serialize(value);
        }

        private static string ComputeSha256(string value)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
                StringBuilder builder = new StringBuilder(bytes.Length * 2);
                for (int index = 0; index < bytes.Length; index++)
                {
                    builder.Append(bytes[index].ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}
