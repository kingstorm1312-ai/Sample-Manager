using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
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
    }

    internal sealed class GoogleSheetsSampleRepository
    {
        private const string SpreadsheetId = "1mliaTlEftFXI3mqDYUny134MutVqjFPZBqrNKL424aQ";
        private const string RequestSheet = "YEU_CAU_MAU";
        private const string QaSheet = "DM_QA";
        private const string SampleSheet = "QUAN_LY_MAU";
        private const int RequestSheetId = 0;
        private const long SampleSheetId = 2040218823L;
        private const int TimeoutMilliseconds = 20000;
        private readonly string credentialsPath;
        private readonly string tokenPath;

        public GoogleSheetsSampleRepository()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            credentialsPath = Path.Combine(baseDirectory, ".secrets", "google_oauth_desktop.json");
            tokenPath = Path.Combine(baseDirectory, ".secrets", "google_token.json");
        }

        public IList<QaOption> ReadActiveQaOptions()
        {
            IDictionary<string, object> result = GetJson(BuildValuesUrl(QaSheet, "A:D"));
            IList<IList<string>> rows = ParseRows(result);
            IList<QaOption> options = new List<QaOption>();
            for (int index = 1; index < rows.Count; index++)
            {
                string id = Cell(rows[index], 0);
                string displayName = Cell(rows[index], 1);
                string active = Cell(rows[index], 2);
                if (!String.IsNullOrWhiteSpace(id)
                    && String.Equals(active, "TRUE", StringComparison.OrdinalIgnoreCase))
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
            IDictionary<string, object> result = GetJson(BuildValuesUrl(RequestSheet, "A:L"));
            IList<IList<string>> rows = ParseRows(result);
            IList<SampleRequestRecord> records = new List<SampleRequestRecord>();
            if (rows.Count == 0)
            {
                return records;
            }

            IList<string> headers = rows[0];
            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                SampleRequestRecord record = ToRecord(headers, rows[rowIndex]);
                records.Add(record);
            }
            return records;
        }

        public IList<SampleManagementRecord> ReadAllSamples()
        {
            IDictionary<string, object> result = GetJson(BuildValuesUrl(SampleSheet, "A:L"));
            IList<IList<string>> rows = ParseRows(result);
            IList<SampleManagementRecord> records = new List<SampleManagementRecord>();
            if (rows.Count == 0)
            {
                return records;
            }

            IList<string> headers = rows[0];
            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                records.Add(ToSampleRecord(headers, rows[rowIndex]));
            }
            return records;
        }

        public SampleRequestRecord AppendAndReadBack(SampleRequestRecord record)
        {
            IList<string> headers = ReadHeaders();
            IList<object> values = new List<object>();
            for (int index = 0; index < headers.Count; index++)
            {
                values.Add(ValueForHeader(record, headers[index]));
            }
            IDictionary<string, object> body = new Dictionary<string, object>();
            body["majorDimension"] = "ROWS";
            body["values"] = new object[] { values };
            PostJson(BuildAppendUrl(), body);

            SampleRequestRecord readBack = ReadById(record.YeuCauId);
            if (readBack == null)
            {
                throw new InvalidOperationException("Đã ghi nhưng không đọc lại được bản ghi theo YeuCau_ID.");
            }
            return readBack;
        }

        public SampleRequestRecord UpdateRequestAndReadBack(SampleRequestRecord record)
        {
            if (record == null || String.IsNullOrWhiteSpace(record.YeuCauId))
            {
                throw new InvalidOperationException("Yêu cầu không hợp lệ.");
            }

            IList<string> headers = ReadHeaders();
            IDictionary<string, object> result = GetJson(BuildValuesUrl(RequestSheet, "A:L"));
            IList<IList<string>> rows = ParseRows(result);
            int matchedPhysicalRow = -1;
            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                if (String.Equals(Cell(rows[rowIndex], HeaderIndex(headers, "YeuCau_ID")), record.YeuCauId, StringComparison.Ordinal))
                {
                    matchedPhysicalRow = rowIndex + 1;
                    break;
                }
            }
            if (matchedPhysicalRow < 0)
            {
                throw new InvalidOperationException("Không tìm thấy yêu cầu để cập nhật.");
            }

            IList<object> values = new List<object>();
            for (int index = 0; index < headers.Count; index++)
            {
                values.Add(ValueForHeader(record, headers[index]));
            }
            IDictionary<string, object> body = new Dictionary<string, object>();
            body["majorDimension"] = "ROWS";
            body["values"] = new object[] { values };
            PutJson(BuildRequestUpdateUrl(matchedPhysicalRow), body);

            SampleRequestRecord updated = ReadById(record.YeuCauId);
            if (updated == null)
            {
                throw new InvalidOperationException("Đã cập nhật nhưng không đọc lại được yêu cầu.");
            }
            return updated;
        }

        public void EnsureSampleStatusColumn()
        {
            IList<string> headers = ReadSampleHeaders();
            if (HeaderIndex(headers, "TrangThaiMay") >= 0)
            {
                return;
            }

            IDictionary<string, object> body = new Dictionary<string, object>();
            body["majorDimension"] = "ROWS";
            body["values"] = new object[] { new object[] { "TrangThaiMay" } };
            PutJson(BuildSampleHeaderUpdateUrl(), body);
        }

        public IList<SampleManagementRecord> AppendSamplesAndReadBack(IList<SampleManagementRecord> records)
        {
            return EnsureSamplesAndReadBack(records);
        }

        public IList<SampleManagementRecord> EnsureSamplesAndReadBack(IList<SampleManagementRecord> records)
        {
            if (records == null || records.Count == 0)
            {
                throw new InvalidOperationException("Không có mẫu để tạo.");
            }

            IList<SampleManagementRecord> existing = ReadAllSamples();
            int existingForRequest = 0;
            for (int existingIndex = 0; existingIndex < existing.Count; existingIndex++)
            {
                if (String.Equals(existing[existingIndex].YeuCauId, records[0].YeuCauId, StringComparison.Ordinal))
                {
                    existingForRequest++;
                }
            }
            if (existingForRequest > records.Count)
            {
                throw new InvalidOperationException("Yêu cầu đã có nhiều mẫu hơn số lượng cần tạo.");
            }

            IList<SampleManagementRecord> missing = new List<SampleManagementRecord>();
            for (int recordIndex = 0; recordIndex < records.Count; recordIndex++)
            {
                SampleManagementRecord found = FindSampleBySampleId(existing, records[recordIndex].SampleId);
                if (found != null)
                {
                    if (!String.Equals(found.YeuCauId, records[recordIndex].YeuCauId, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("Sample_ID đã được liên kết với yêu cầu khác.");
                    }
                    continue;
                }

                SampleManagementRecord sameSequence = FindSampleByRequestAndStt(
                    existing,
                    records[recordIndex].YeuCauId,
                    records[recordIndex].SttMau);
                if (sameSequence != null)
                {
                    throw new InvalidOperationException("Yêu cầu đã có mẫu trùng số thứ tự.");
                }
                missing.Add(records[recordIndex]);
            }

            if (missing.Count > 0)
            {
                AppendSampleRows(missing);
                existing = ReadAllSamples();
            }

            IList<SampleManagementRecord> matched = new List<SampleManagementRecord>();
            for (int recordIndex = 0; recordIndex < records.Count; recordIndex++)
            {
                SampleManagementRecord found = FindSampleBySampleId(existing, records[recordIndex].SampleId);
                if (found == null)
                {
                    throw new InvalidOperationException("Đã ghi nhưng không đọc lại được mẫu.");
                }
                if (!String.Equals(found.YeuCauId, records[recordIndex].YeuCauId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Mẫu đọc lại không thuộc đúng yêu cầu.");
                }
                matched.Add(found);
            }
            return matched;
        }

        private void AppendSampleRows(IList<SampleManagementRecord> records)
        {
            IList<string> headers = ReadSampleHeaders();
            IDictionary<string, object> existingResult = GetJson(BuildValuesUrl(SampleSheet, "A:V"));
            IList<IList<string>> existingRows = ParseRows(existingResult);
            int firstPhysicalRow = existingRows.Count + 1;
            IList<object> values = new List<object>();
            for (int recordIndex = 0; recordIndex < records.Count; recordIndex++)
            {
                IList<object> row = new List<object>();
                for (int headerIndex = 0; headerIndex < headers.Count; headerIndex++)
                {
                    row.Add(SampleValueForHeader(records[recordIndex], headers[headerIndex]));
                }
                values.Add(row);
            }

            IDictionary<string, object> body = new Dictionary<string, object>();
            body["majorDimension"] = "ROWS";
            body["values"] = values;
            PutJson(BuildSampleWriteUrl(firstPhysicalRow, records.Count), body);
        }

        public SampleManagementRecord UpdateSampleAndReadBack(SampleManagementRecord record)
        {
            return UpdateSampleAndReadBack(record, record == null ? String.Empty : record.PhienBan, String.Empty);
        }

        public SampleManagementRecord UpdateSampleAndReadBack(
            SampleManagementRecord record,
            string requestVersion,
            string contract)
        {
            if (record == null || String.IsNullOrWhiteSpace(record.MauId))
            {
                throw new InvalidOperationException("Mẫu không hợp lệ.");
            }

            IList<string> headers = ReadSampleHeaders();
            IDictionary<string, object> result = GetJson(BuildValuesUrl(SampleSheet, "A:L"));
            IList<IList<string>> rows = ParseRows(result);
            int matchedPhysicalRow = -1;
            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                if (String.Equals(Cell(rows[rowIndex], HeaderIndex(headers, "Mau_ID")), record.MauId, StringComparison.Ordinal))
                {
                    matchedPhysicalRow = rowIndex + 1;
                    break;
                }
            }
            if (matchedPhysicalRow < 0)
            {
                throw new InvalidOperationException("Không tìm thấy mẫu để cập nhật.");
            }

            IList<SampleManagementRecord> currentRecords = ReadAllSamples();
            SampleManagementRecord current = FindSampleByMauId(currentRecords, record.MauId);
            if (current == null)
            {
                throw new InvalidOperationException("Không đọc được mẫu hiện tại.");
            }
            if (!String.Equals(current.YeuCauId, record.YeuCauId, StringComparison.Ordinal)
                || !String.Equals(current.SttMau, record.SttMau, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Dữ liệu liên kết của mẫu không khớp.");
            }

            string currentVersion = SampleNormalization.NormalizePhienBan(current.PhienBan);
            string nextVersion = SampleNormalization.NormalizePhienBan(record.PhienBan);
            bool versionChanged = !String.Equals(currentVersion, nextVersion, StringComparison.OrdinalIgnoreCase);
            if (versionChanged)
            {
                string initialVersion = SampleNormalization.NormalizePhienBan(requestVersion);
                if (!String.Equals(currentVersion, initialVersion, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Phiên bản đã được xác nhận; không thể đổi lại.");
                }

                string expectedSampleId = SampleNormalization.BuildSampleId(contract, nextVersion, record.SttMau);
                if (!String.Equals(record.SampleId, expectedSampleId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Sample_ID không khớp phiên bản mới.");
                }

                SampleManagementRecord duplicate = FindSampleBySampleId(currentRecords, expectedSampleId);
                if (duplicate != null && !String.Equals(duplicate.MauId, record.MauId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Sample_ID mới đã tồn tại.");
                }
            }

            for (int index = 0; index < headers.Count; index++)
            {
                bool isVersionHeader = headers[index] == "Sample_ID" || headers[index] == "PhienBan";
                if (isVersionHeader && !versionChanged)
                {
                    continue;
                }
                if (!isVersionHeader && !IsEditableSampleHeader(headers[index]))
                {
                    continue;
                }

                IDictionary<string, object> body = new Dictionary<string, object>();
                body["majorDimension"] = "ROWS";
                body["values"] = new object[]
                {
                    new object[] { SampleValueForHeader(record, headers[index]) }
                };
                PutJson(BuildSampleUpdateUrl(matchedPhysicalRow, index), body);
            }

            IList<SampleManagementRecord> readBack = ReadAllSamples();
            SampleManagementRecord updated = FindSampleByMauId(readBack, record.MauId);
            if (updated == null)
            {
                throw new InvalidOperationException("Đã cập nhật nhưng không đọc lại được mẫu.");
            }
            return updated;
        }

        public void DeleteSampleById(string mauId)
        {
            IDictionary<string, object> result = GetJson(BuildValuesUrl(SampleSheet, "A:L"));
            IList<IList<string>> rows = ParseRows(result);
            IList<string> headers = rows.Count == 0 ? null : rows[0];
            if (headers == null)
            {
                throw new InvalidOperationException("Không có header QUAN_LY_MAU để xóa.");
            }

            int matchedPhysicalRow = -1;
            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                if (String.Equals(Cell(rows[rowIndex], HeaderIndex(headers, "Mau_ID")), mauId, StringComparison.Ordinal))
                {
                    matchedPhysicalRow = rowIndex + 1;
                    break;
                }
            }
            if (matchedPhysicalRow < 0)
            {
                throw new InvalidOperationException("Không tìm thấy mẫu test để xóa: " + mauId);
            }

            IDictionary<string, object> rowRange = new Dictionary<string, object>();
            rowRange["sheetId"] = SampleSheetId;
            rowRange["dimension"] = "ROWS";
            rowRange["startIndex"] = matchedPhysicalRow - 1;
            rowRange["endIndex"] = matchedPhysicalRow;
            IDictionary<string, object> deleteDimension = new Dictionary<string, object>();
            deleteDimension["range"] = rowRange;
            IDictionary<string, object> request = new Dictionary<string, object>();
            request["deleteDimension"] = deleteDimension;
            IDictionary<string, object> body = new Dictionary<string, object>();
            body["requests"] = new object[] { request };
            PostJson(BuildBatchUpdateUrl(), body);
        }

        public SampleRequestRecord ReadById(string requestId)
        {
            IDictionary<string, object> result = GetJson(BuildValuesUrl(RequestSheet, "A:L"));
            IList<IList<string>> rows = ParseRows(result);
            if (rows.Count == 0)
            {
                return null;
            }
            IList<string> headers = rows[0];
            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                if (String.Equals(Cell(rows[rowIndex], HeaderIndex(headers, "YeuCau_ID")), requestId, StringComparison.Ordinal))
                {
                    return ToRecord(headers, rows[rowIndex]);
                }
            }
            return null;
        }

        public void DeleteById(string requestId)
        {
            IDictionary<string, object> result = GetJson(BuildValuesUrl(RequestSheet, "A:L"));
            IList<IList<string>> rows = ParseRows(result);
            IList<string> headers = rows.Count == 0 ? null : rows[0];
            if (headers == null)
            {
                throw new InvalidOperationException("Không có header YEU_CAU_MAU để xóa.");
            }
            int matchedPhysicalRow = -1;
            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                if (String.Equals(Cell(rows[rowIndex], HeaderIndex(headers, "YeuCau_ID")), requestId, StringComparison.Ordinal))
                {
                    matchedPhysicalRow = rowIndex + 1;
                    break;
                }
            }
            if (matchedPhysicalRow < 0)
            {
                throw new InvalidOperationException("Không tìm thấy record test để xóa: " + requestId);
            }

            IDictionary<string, object> rowRange = new Dictionary<string, object>();
            rowRange["sheetId"] = RequestSheetId;
            rowRange["dimension"] = "ROWS";
            rowRange["startIndex"] = matchedPhysicalRow - 1;
            rowRange["endIndex"] = matchedPhysicalRow;
            IDictionary<string, object> deleteDimension = new Dictionary<string, object>();
            deleteDimension["range"] = rowRange;
            IDictionary<string, object> request = new Dictionary<string, object>();
            request["deleteDimension"] = deleteDimension;
            IDictionary<string, object> body = new Dictionary<string, object>();
            body["requests"] = new object[] { request };
            PostJson(BuildBatchUpdateUrl(), body);
            if (ReadById(requestId) != null)
            {
                throw new InvalidOperationException("Đã xóa nhưng readback vẫn còn record test: " + requestId);
            }
        }

        private IList<string> ReadHeaders()
        {
            IDictionary<string, object> result = GetJson(BuildValuesUrl(RequestSheet, "A:L"));
            IList<IList<string>> rows = ParseRows(result);
            if (rows.Count == 0)
            {
                throw new InvalidOperationException("YEU_CAU_MAU chưa có header.");
            }
            string[] required = { "YeuCau_ID", "NgayTaoYeuCau", "SoHopDong", "MaVatTu", "TenTui", "QA_ID", "NoiYeuCau", "SoLuongMau", "PhienBan", "Deadline", "TrangThai", "GhiChu" };
            for (int index = 0; index < required.Length; index++)
            {
                if (HeaderIndex(rows[0], required[index]) < 0)
                {
                    throw new InvalidOperationException("YEU_CAU_MAU thiếu cột: " + required[index]);
                }
            }
            return rows[0];
        }

        private static object ValueForHeader(SampleRequestRecord record, string header)
        {
            if (header == "YeuCau_ID") return record.YeuCauId;
            if (header == "NgayTaoYeuCau") return record.NgayTaoYeuCau;
            if (header == "SoHopDong") return record.SoHopDong;
            if (header == "MaVatTu") return record.MaVatTu;
            if (header == "TenTui") return record.TenTui;
            if (header == "QA_ID") return record.QaId;
            if (header == "NoiYeuCau") return record.NoiYeuCau;
            if (header == "SoLuongMau") return record.SoLuongMau;
            if (header == "PhienBan") return SampleNormalization.NormalizePhienBan(record.PhienBan);
            if (header == "Deadline") return record.Deadline;
            if (header == "TrangThai") return record.TrangThai;
            if (header == "GhiChu") return record.GhiChu;
            return String.Empty;
        }

        private IList<string> ReadSampleHeaders()
        {
            IDictionary<string, object> result = GetJson(BuildValuesUrl(SampleSheet, "A:L"));
            IList<IList<string>> rows = ParseRows(result);
            if (rows.Count == 0)
            {
                throw new InvalidOperationException("QUAN_LY_MAU chưa có header.");
            }
            string[] required = { "Mau_ID", "YeuCau_ID", "Sample_ID", "PhienBan", "STTMau", "NgayDuyet", "NgayHetHan", "NguoiDuyet", "NoiLuu", "NgayGiaoMau", "GhiChu", "TrangThaiMay" };
            for (int index = 0; index < required.Length; index++)
            {
                if (HeaderIndex(rows[0], required[index]) < 0)
                {
                    throw new InvalidOperationException("QUAN_LY_MAU thiếu cột: " + required[index]);
                }
            }
            return rows[0];
        }

        private static object SampleValueForHeader(SampleManagementRecord record, string header)
        {
            if (header == "Mau_ID") return record.MauId;
            if (header == "YeuCau_ID") return record.YeuCauId;
            if (header == "Sample_ID") return record.SampleId;
            if (header == "PhienBan") return record.PhienBan;
            if (header == "STTMau") return record.SttMau;
            if (header == "NgayDuyet") return record.NgayDuyet;
            if (header == "NgayHetHan") return record.NgayHetHan;
            if (header == "NguoiDuyet") return record.NguoiDuyet;
            if (header == "NoiLuu") return record.NoiLuu;
            if (header == "NgayGiaoMau") return record.NgayGiaoMau;
            if (header == "GhiChu") return record.GhiChu;
            if (header == "TrangThaiMay") return record.TrangThaiMay;
            return String.Empty;
        }

        private static bool IsEditableSampleHeader(string header)
        {
            return header == "NgayDuyet"
                || header == "NgayHetHan"
                || header == "NguoiDuyet"
                || header == "NoiLuu"
                || header == "NgayGiaoMau"
                || header == "GhiChu"
                || header == "TrangThaiMay";
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
                GhiChu = Value(row, headers, "GhiChu")
            };
        }

        private static SampleManagementRecord ToSampleRecord(IList<string> headers, IList<string> row)
        {
            return new SampleManagementRecord
            {
                MauId = Value(row, headers, "Mau_ID"),
                YeuCauId = Value(row, headers, "YeuCau_ID"),
                SampleId = Value(row, headers, "Sample_ID"),
                PhienBan = Value(row, headers, "PhienBan"),
                SttMau = Value(row, headers, "STTMau"),
                NgayDuyet = Value(row, headers, "NgayDuyet"),
                NgayHetHan = Value(row, headers, "NgayHetHan"),
                NguoiDuyet = Value(row, headers, "NguoiDuyet"),
                NoiLuu = Value(row, headers, "NoiLuu"),
                NgayGiaoMau = Value(row, headers, "NgayGiaoMau"),
                GhiChu = Value(row, headers, "GhiChu"),
                TrangThaiMay = Value(row, headers, "TrangThaiMay")
            };
        }

        private static SampleManagementRecord FindSampleByMauId(
            IList<SampleManagementRecord> records,
            string mauId)
        {
            for (int index = 0; index < records.Count; index++)
            {
                if (String.Equals(records[index].MauId, mauId, StringComparison.Ordinal))
                {
                    return records[index];
                }
            }
            return null;
        }

        private static SampleManagementRecord FindSampleBySampleId(
            IList<SampleManagementRecord> records,
            string sampleId)
        {
            for (int index = 0; index < records.Count; index++)
            {
                if (String.Equals(records[index].SampleId, sampleId, StringComparison.Ordinal))
                {
                    return records[index];
                }
            }
            return null;
        }

        private static SampleManagementRecord FindSampleByRequestAndStt(
            IList<SampleManagementRecord> records,
            string requestId,
            string sttMau)
        {
            for (int index = 0; index < records.Count; index++)
            {
                if (String.Equals(records[index].YeuCauId, requestId, StringComparison.Ordinal)
                    && String.Equals(records[index].SttMau, sttMau, StringComparison.Ordinal))
                {
                    return records[index];
                }
            }
            return null;
        }

        private IDictionary<string, object> GetJson(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Timeout = TimeoutMilliseconds;
            request.ReadWriteTimeout = TimeoutMilliseconds;
            request.Headers[HttpRequestHeader.Authorization] = "Bearer " + Authenticate();
            request.Accept = "application/json";
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream()))
            {
                return DeserializeObject(reader.ReadToEnd());
            }
        }

        private void PostJson(string url, IDictionary<string, object> body)
        {
            byte[] payload = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(body));
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.Timeout = TimeoutMilliseconds;
            request.ReadWriteTimeout = TimeoutMilliseconds;
            request.Headers[HttpRequestHeader.Authorization] = "Bearer " + Authenticate();
            request.ContentType = "application/json; charset=utf-8";
            request.ContentLength = payload.Length;
            using (Stream stream = request.GetRequestStream())
            {
                stream.Write(payload, 0, payload.Length);
            }
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                using (Stream stream = response.GetResponseStream())
                {
                    if (stream != null) stream.CopyTo(Stream.Null);
                }
            }
        }

        private void PutJson(string url, IDictionary<string, object> body)
        {
            byte[] payload = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(body));
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "PUT";
            request.Timeout = TimeoutMilliseconds;
            request.ReadWriteTimeout = TimeoutMilliseconds;
            request.Headers[HttpRequestHeader.Authorization] = "Bearer " + Authenticate();
            request.ContentType = "application/json; charset=utf-8";
            request.ContentLength = payload.Length;
            using (Stream stream = request.GetRequestStream())
            {
                stream.Write(payload, 0, payload.Length);
            }
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            {
                if (stream != null) stream.CopyTo(Stream.Null);
            }
        }

        private string Authenticate()
        {
            return GoogleOAuth.GetAccessToken(credentialsPath, tokenPath);
        }

        private static string BuildValuesUrl(string sheetName, string range)
        {
            string quotedRange = "'" + sheetName.Replace("'", "''") + "'!" + range;
            return "https://sheets.googleapis.com/v4/spreadsheets/"
                + Uri.EscapeDataString(SpreadsheetId)
                + "/values/" + Uri.EscapeDataString(quotedRange)
                + "?majorDimension=ROWS";
        }

        private static string BuildAppendUrl()
        {
            string quotedRange = "'" + RequestSheet + "'!A1:L";
            return "https://sheets.googleapis.com/v4/spreadsheets/"
                + Uri.EscapeDataString(SpreadsheetId)
                + "/values/" + Uri.EscapeDataString(quotedRange)
                + ":append?valueInputOption=RAW&insertDataOption=INSERT_ROWS";
        }

        private static string BuildSampleWriteUrl(int firstPhysicalRow, int rowCount)
        {
            int lastPhysicalRow = firstPhysicalRow + rowCount - 1;
            string quotedRange = "'" + SampleSheet + "'!A" + firstPhysicalRow + ":L" + lastPhysicalRow;
            return "https://sheets.googleapis.com/v4/spreadsheets/"
                + Uri.EscapeDataString(SpreadsheetId)
                + "/values/" + Uri.EscapeDataString(quotedRange)
                + "?valueInputOption=RAW";
        }

        private static string BuildSampleUpdateUrl(int physicalRow, int columnIndex)
        {
            string column = ToColumnName(columnIndex);
            string range = "'" + SampleSheet + "'!" + column + physicalRow;
            return "https://sheets.googleapis.com/v4/spreadsheets/"
                + Uri.EscapeDataString(SpreadsheetId)
                + "/values/" + Uri.EscapeDataString(range)
                + "?valueInputOption=RAW";
        }

        private static string ToColumnName(int zeroBasedIndex)
        {
            int value = zeroBasedIndex + 1;
            String result = String.Empty;
            while (value > 0)
            {
                int remainder = (value - 1) % 26;
                result = (char)('A' + remainder) + result;
                value = (value - 1) / 26;
            }
            return result;
        }

        private static string BuildRequestUpdateUrl(int physicalRow)
        {
            string range = "'" + RequestSheet + "'!A" + physicalRow + ":L" + physicalRow;
            return "https://sheets.googleapis.com/v4/spreadsheets/"
                + Uri.EscapeDataString(SpreadsheetId)
                + "/values/" + Uri.EscapeDataString(range)
                + "?valueInputOption=RAW";
        }

        private static string BuildSampleHeaderUpdateUrl()
        {
            string range = "'" + SampleSheet + "'!L1";
            return "https://sheets.googleapis.com/v4/spreadsheets/"
                + Uri.EscapeDataString(SpreadsheetId)
                + "/values/" + Uri.EscapeDataString(range)
                + "?valueInputOption=RAW";
        }

        private static string BuildBatchUpdateUrl()
        {
            return "https://sheets.googleapis.com/v4/spreadsheets/"
                + Uri.EscapeDataString(SpreadsheetId)
                + ":batchUpdate";
        }

        private static IList<IList<string>> ParseRows(IDictionary<string, object> result)
        {
            IList<IList<string>> rows = new List<IList<string>>();
            object raw;
            object[] values;
            if (!result.TryGetValue("values", out raw) || (values = raw as object[]) == null)
            {
                return rows;
            }
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
