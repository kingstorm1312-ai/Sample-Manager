using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SampleManager
{
    internal sealed class QaOption
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }

        public override string ToString()
        {
            return String.IsNullOrWhiteSpace(DisplayName) ? "Chưa có tên QA" : DisplayName;
        }
    }

    internal sealed class SampleRequestRecord
    {
        public string YeuCauId { get; set; }
        public string NgayTaoYeuCau { get; set; }
        public string SoHopDong { get; set; }
        public string MaVatTu { get; set; }
        public string TenTui { get; set; }
        public string QaId { get; set; }
        public string NoiYeuCau { get; set; }
        public string SoLuongMau { get; set; }
        public string PhienBan { get; set; }
        public string Deadline { get; set; }
        public string TrangThai { get; set; }
        public string GhiChu { get; set; }
        public string RowVersion { get; set; }
    }

    internal sealed class SampleManagementRecord
    {
        public string MauId { get; set; }
        public string YeuCauId { get; set; }
        public string SampleId { get; set; }
        public string PhienBan { get; set; }
        public string SttMau { get; set; }
        public string NgayDuyet { get; set; }
        public string NgayHetHan { get; set; }
        public string NguoiDuyet { get; set; }
        public string NoiLuu { get; set; }
        public string NgayGiaoMau { get; set; }
        public string GhiChu { get; set; }
        public string TrangThaiMay { get; set; }
        public string RowVersion { get; set; }
        public string ClaimOwner { get; set; }
        public string ClaimedAt { get; set; }
    }

    internal sealed class GatewayCreateResult
    {
        public SampleRequestRecord Request { get; set; }
        public IList<SampleManagementRecord> Samples { get; set; }
    }

    internal sealed class GoogleSheetsSampleRepository
    {
        private const string SpreadsheetId = "1mliaTlEftFXI3mqDYUny134MutVqjFPZBqrNKL424aQ";
        private const string RequestSheet = "YEU_CAU_MAU";
        private const string QaSheet = "DM_QA";
        private const string SampleSheet = "QUAN_LY_MAU";
        private const int TimeoutMilliseconds = 20000;
        private readonly string credentialsPath;
        private readonly string tokenPath;
        private readonly SampleWriteGatewayClient writeGateway;

        public GoogleSheetsSampleRepository()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            credentialsPath = Path.Combine(baseDirectory, ".secrets", "google_oauth_desktop.json");
            tokenPath = Path.Combine(baseDirectory, ".secrets", "google_token.json");
            writeGateway = new SampleWriteGatewayClient();
        }

        public IList<QaOption> ReadActiveQaOptions()
        {
            IDictionary<string, object> result = GetJson(BuildValuesUrl(QaSheet, "A:D"));
            IList<IList<string>> rows = ParseRows(result);
            IList<QaOption> options = new List<QaOption>();
            HashSet<string> seenIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 1; index < rows.Count; index++)
            {
                string id = Cell(rows[index], 0);
                string displayName = Cell(rows[index], 1);
                if (!String.IsNullOrWhiteSpace(id) && seenIds.Add(id))
                {
                    options.Add(new QaOption { Id = id, DisplayName = displayName });
                }
            }
            return options;
        }

        public IList<SampleRequestRecord> ReadRequestsForQa(string qaId)
        {
            IList<SampleRequestRecord> allRecords = ReadAllRequests();
            IList<SampleRequestRecord> records = new List<SampleRequestRecord>();
            for (int index = 0; index < allRecords.Count; index++)
            {
                if (String.Equals(allRecords[index].QaId, qaId, StringComparison.Ordinal))
                {
                    records.Add(allRecords[index]);
                }
            }
            return records;
        }

        public IList<SampleRequestRecord> ReadAllRequests()
        {
            IDictionary<string, object> result = GetJson(BuildValuesUrl(RequestSheet, "A:M"));
            IList<IList<string>> rows = ParseRows(result);
            IList<SampleRequestRecord> records = new List<SampleRequestRecord>();
            if (rows.Count == 0) return records;
            IList<string> headers = rows[0];
            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                SampleRequestRecord record = ToRecord(headers, rows[rowIndex]);
                if (!String.IsNullOrWhiteSpace(record.YeuCauId)) records.Add(record);
            }
            return records;
        }

        public IList<SampleManagementRecord> ReadAllSamples()
        {
            IDictionary<string, object> result = GetJson(BuildValuesUrl(SampleSheet, "A:O"));
            IList<IList<string>> rows = ParseRows(result);
            IList<SampleManagementRecord> records = new List<SampleManagementRecord>();
            if (rows.Count == 0) return records;
            IList<string> headers = rows[0];
            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                SampleManagementRecord record = ToSampleRecord(headers, rows[rowIndex]);
                if (!String.IsNullOrWhiteSpace(record.MauId)) records.Add(record);
            }
            return records;
        }

        public SampleRequestRecord ReadById(string requestId)
        {
            IList<SampleRequestRecord> records = ReadAllRequests();
            for (int index = 0; index < records.Count; index++)
            {
                if (String.Equals(records[index].YeuCauId, requestId, StringComparison.Ordinal)) return records[index];
            }
            return null;
        }

        public async Task<GatewayCreateResult> CreateRequestAndSamplesAsync(
            SampleRequestRecord record,
            int sampleCount,
            string operationId)
        {
            IDictionary<string, object> payload = new Dictionary<string, object>();
            payload["request"] = RequestPayload(record);
            payload["sampleCount"] = sampleCount;
            SampleGatewayResult response = await writeGateway.ExecuteAsync(operationId, "CREATE_REQUEST", payload);
            IDictionary<string, object> result = response.Result;
            return new GatewayCreateResult
            {
                Request = ToRequestRecord(GetObject(result, "request")),
                Samples = ToSampleRecords(GetObjectArray(result, "samples"))
            };
        }

        public async Task<SampleRequestRecord> UpdateRequestAndReadBackAsync(
            SampleRequestRecord record,
            IDictionary<string, string> changedFields,
            string operationId)
        {
            IDictionary<string, object> payload = new Dictionary<string, object>();
            payload["recordId"] = record.YeuCauId;
            payload["expectedVersion"] = ParseVersion(record.RowVersion);
            payload["changedFields"] = StringFields(changedFields);
            SampleGatewayResult response = await writeGateway.ExecuteAsync(operationId, "UPDATE_REQUEST", payload);
            return ToRequestRecord(GetObject(response.Result, "request"));
        }

        public async Task<SampleManagementRecord> UpdateSampleAndReadBackAsync(
            SampleManagementRecord record,
            IDictionary<string, string> changedFields,
            string clientId,
            string operationId)
        {
            IDictionary<string, object> payload = new Dictionary<string, object>();
            payload["recordId"] = record.MauId;
            payload["expectedVersion"] = ParseVersion(record.RowVersion);
            payload["clientId"] = clientId;
            payload["changedFields"] = StringFields(changedFields);
            SampleGatewayResult response = await writeGateway.ExecuteAsync(operationId, "UPDATE_SAMPLE", payload);
            return ToSampleRecord(GetObject(response.Result, "sample"));
        }

        public async Task<SampleManagementRecord> ClaimSampleAsync(
            SampleManagementRecord record,
            string clientId,
            string operationId)
        {
            IDictionary<string, object> payload = new Dictionary<string, object>();
            payload["recordId"] = record.MauId;
            payload["expectedVersion"] = ParseVersion(record.RowVersion);
            payload["clientId"] = clientId;
            SampleGatewayResult response = await writeGateway.ExecuteAsync(operationId, "CLAIM_SAMPLE", payload);
            return ToSampleRecord(GetObject(response.Result, "sample"));
        }

        public async Task<SampleManagementRecord> ReleaseClaimAsync(
            SampleManagementRecord record,
            string clientId,
            string operationId)
        {
            IDictionary<string, object> payload = new Dictionary<string, object>();
            payload["recordId"] = record.MauId;
            payload["expectedVersion"] = ParseVersion(record.RowVersion);
            payload["clientId"] = clientId;
            SampleGatewayResult response = await writeGateway.ExecuteAsync(operationId, "RELEASE_CLAIM", payload);
            return ToSampleRecord(GetObject(response.Result, "sample"));
        }

        public async Task DeleteByIdAsync(string requestId, string operationId)
        {
            IDictionary<string, object> payload = new Dictionary<string, object>();
            payload["recordId"] = requestId;
            await writeGateway.ExecuteAsync(operationId, "DELETE_REQUEST", payload);
        }

        public async Task DeleteSampleByIdAsync(string mauId, string operationId)
        {
            IDictionary<string, object> payload = new Dictionary<string, object>();
            payload["recordId"] = mauId;
            await writeGateway.ExecuteAsync(operationId, "DELETE_SAMPLE", payload);
        }

        private IDictionary<string, object> GetJson(string url)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Timeout = TimeoutMilliseconds;
            request.ReadWriteTimeout = TimeoutMilliseconds;
            request.Headers[HttpRequestHeader.Authorization] = "Bearer " + Authenticate();
            request.Accept = "application/json";
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                return DeserializeObject(reader.ReadToEnd());
            }
        }

        private string Authenticate()
        {
            return GoogleOAuth.GetAccessToken(credentialsPath, tokenPath);
        }

        private static IDictionary<string, object> RequestPayload(SampleRequestRecord record)
        {
            IDictionary<string, object> payload = new Dictionary<string, object>();
            payload["YeuCau_ID"] = record.YeuCauId;
            payload["NgayTaoYeuCau"] = record.NgayTaoYeuCau;
            payload["SoHopDong"] = record.SoHopDong;
            payload["MaVatTu"] = record.MaVatTu;
            payload["TenTui"] = record.TenTui;
            payload["QA_ID"] = record.QaId;
            payload["NoiYeuCau"] = record.NoiYeuCau;
            payload["SoLuongMau"] = record.SoLuongMau;
            payload["PhienBan"] = SampleNormalization.NormalizePhienBan(record.PhienBan);
            payload["Deadline"] = record.Deadline;
            payload["TrangThai"] = record.TrangThai;
            payload["GhiChu"] = record.GhiChu;
            return payload;
        }

        private static IDictionary<string, object> StringFields(IDictionary<string, string> source)
        {
            IDictionary<string, object> fields = new Dictionary<string, object>();
            if (source == null) return fields;
            foreach (KeyValuePair<string, string> pair in source)
            {
                fields[pair.Key] = pair.Value ?? String.Empty;
            }
            return fields;
        }

        private static SampleRequestRecord ToRecord(IList<string> headers, IList<string> row)
        {
            return new SampleRequestRecord
            {
                YeuCauId = Value(row, headers, "YeuCau_ID"),
                NgayTaoYeuCau = Value(row, headers, "NgayTaoYeuCau"),
                SoHopDong = Value(row, headers, "SoHopDong"),
                MaVatTu = Value(row, headers, "MaVatTu"),
                TenTui = Value(row, headers, "TenTui"),
                QaId = Value(row, headers, "QA_ID"),
                NoiYeuCau = Value(row, headers, "NoiYeuCau"),
                SoLuongMau = Value(row, headers, "SoLuongMau"),
                PhienBan = SampleNormalization.NormalizePhienBan(Value(row, headers, "PhienBan")),
                Deadline = Value(row, headers, "Deadline"),
                TrangThai = Value(row, headers, "TrangThai"),
                GhiChu = Value(row, headers, "GhiChu"),
                RowVersion = Value(row, headers, "RowVersion")
            };
        }

        private static SampleManagementRecord ToSampleRecord(IList<string> headers, IList<string> row)
        {
            return new SampleManagementRecord
            {
                MauId = Value(row, headers, "Mau_ID"),
                YeuCauId = Value(row, headers, "YeuCau_ID"),
                SampleId = Value(row, headers, "Sample_ID"),
                PhienBan = SampleNormalization.NormalizePhienBan(Value(row, headers, "PhienBan")),
                SttMau = Value(row, headers, "STTMau"),
                NgayDuyet = Value(row, headers, "NgayDuyet"),
                NgayHetHan = Value(row, headers, "NgayHetHan"),
                NguoiDuyet = Value(row, headers, "NguoiDuyet"),
                NoiLuu = Value(row, headers, "NoiLuu"),
                NgayGiaoMau = Value(row, headers, "NgayGiaoMau"),
                GhiChu = Value(row, headers, "GhiChu"),
                TrangThaiMay = Value(row, headers, "TrangThaiMay"),
                RowVersion = Value(row, headers, "RowVersion"),
                ClaimOwner = Value(row, headers, "ClaimOwner"),
                ClaimedAt = Value(row, headers, "ClaimedAt")
            };
        }

        private static SampleRequestRecord ToRequestRecord(IDictionary<string, object> source)
        {
            return new SampleRequestRecord
            {
                YeuCauId = ObjectValue(source, "YeuCau_ID"),
                NgayTaoYeuCau = ObjectValue(source, "NgayTaoYeuCau"),
                SoHopDong = ObjectValue(source, "SoHopDong"),
                MaVatTu = ObjectValue(source, "MaVatTu"),
                TenTui = ObjectValue(source, "TenTui"),
                QaId = ObjectValue(source, "QA_ID"),
                NoiYeuCau = ObjectValue(source, "NoiYeuCau"),
                SoLuongMau = ObjectValue(source, "SoLuongMau"),
                PhienBan = SampleNormalization.NormalizePhienBan(ObjectValue(source, "PhienBan")),
                Deadline = ObjectValue(source, "Deadline"),
                TrangThai = ObjectValue(source, "TrangThai"),
                GhiChu = ObjectValue(source, "GhiChu"),
                RowVersion = ObjectValue(source, "RowVersion")
            };
        }

        private static SampleManagementRecord ToSampleRecord(IDictionary<string, object> source)
        {
            return new SampleManagementRecord
            {
                MauId = ObjectValue(source, "Mau_ID"),
                YeuCauId = ObjectValue(source, "YeuCau_ID"),
                SampleId = ObjectValue(source, "Sample_ID"),
                PhienBan = SampleNormalization.NormalizePhienBan(ObjectValue(source, "PhienBan")),
                SttMau = ObjectValue(source, "STTMau"),
                NgayDuyet = ObjectValue(source, "NgayDuyet"),
                NgayHetHan = ObjectValue(source, "NgayHetHan"),
                NguoiDuyet = ObjectValue(source, "NguoiDuyet"),
                NoiLuu = ObjectValue(source, "NoiLuu"),
                NgayGiaoMau = ObjectValue(source, "NgayGiaoMau"),
                GhiChu = ObjectValue(source, "GhiChu"),
                TrangThaiMay = ObjectValue(source, "TrangThaiMay"),
                RowVersion = ObjectValue(source, "RowVersion"),
                ClaimOwner = ObjectValue(source, "ClaimOwner"),
                ClaimedAt = ObjectValue(source, "ClaimedAt")
            };
        }

        private static IList<SampleManagementRecord> ToSampleRecords(object[] values)
        {
            IList<SampleManagementRecord> records = new List<SampleManagementRecord>();
            if (values == null) return records;
            for (int index = 0; index < values.Length; index++)
            {
                IDictionary<string, object> item = values[index] as IDictionary<string, object>;
                if (item != null) records.Add(ToSampleRecord(item));
            }
            return records;
        }

        private static IDictionary<string, object> GetObject(IDictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value)
                ? value as IDictionary<string, object> ?? new Dictionary<string, object>()
                : new Dictionary<string, object>();
        }

        private static object[] GetObjectArray(IDictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) ? value as object[] : null;
        }

        private static string ObjectValue(IDictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value, CultureInfo.InvariantCulture).Trim()
                : String.Empty;
        }

        private static int ParseVersion(string value)
        {
            int version;
            return Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out version) && version >= 0
                ? version
                : 0;
        }

        private static string BuildValuesUrl(string sheetName, string range)
        {
            string quotedRange = "'" + sheetName.Replace("'", "''") + "'!" + range;
            return "https://sheets.googleapis.com/v4/spreadsheets/"
                + Uri.EscapeDataString(SpreadsheetId)
                + "/values/" + Uri.EscapeDataString(quotedRange)
                + "?majorDimension=ROWS";
        }

        private static IList<IList<string>> ParseRows(IDictionary<string, object> result)
        {
            IList<IList<string>> rows = new List<IList<string>>();
            object raw;
            object[] values;
            if (!result.TryGetValue("values", out raw) || (values = raw as object[]) == null) return rows;
            for (int rowIndex = 0; rowIndex < values.Length; rowIndex++)
            {
                object[] cells = values[rowIndex] as object[];
                IList<string> row = new List<string>();
                if (cells != null)
                {
                    for (int cellIndex = 0; cellIndex < cells.Length; cellIndex++)
                    {
                        row.Add(Convert.ToString(cells[cellIndex], CultureInfo.InvariantCulture));
                    }
                }
                rows.Add(row);
            }
            return rows;
        }

        private static int HeaderIndex(IList<string> headers, string header)
        {
            for (int index = 0; index < headers.Count; index++)
            {
                if (String.Equals(headers[index], header, StringComparison.Ordinal)) return index;
            }
            return -1;
        }

        private static string Cell(IList<string> row, int index)
        {
            return index >= 0 && index < row.Count && row[index] != null ? row[index].Trim() : String.Empty;
        }

        private static string Value(IList<string> row, IList<string> headers, string header)
        {
            return Cell(row, HeaderIndex(headers, header));
        }

        private static IDictionary<string, object> DeserializeObject(string json)
        {
            IDictionary<string, object> result = new JavaScriptSerializer().DeserializeObject(json) as IDictionary<string, object>;
            if (result == null) throw new InvalidOperationException("Google Sheets returned invalid JSON.");
            return result;
        }
    }
}
