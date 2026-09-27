using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SampleManager
{
    internal static class SampleNormalization
    {
        private static readonly Regex ContractPattern = new Regex(
            @"^\s*(\d{1,4})\s*/\s*(\d{4}|\d{2})(.*)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string NormalizeSoHopDong(string value)
        {
            string raw = (value ?? String.Empty).Trim();
            Match match = ContractPattern.Match(raw);
            if (!match.Success)
            {
                return raw.ToUpperInvariant();
            }

            string contractNumber = match.Groups[1].Value.PadLeft(4, '0');
            string year = match.Groups[2].Value;
            if (year.Length == 2)
            {
                year = "20" + year;
            }
            string suffix = match.Groups[3].Value.Trim().ToUpperInvariant();
            return contractNumber + "/" + year + suffix;
        }

        public static string NormalizeMaVatTu(string value)
        {
            return (value ?? String.Empty).Trim().Replace(" ", String.Empty).ToUpperInvariant();
        }

        public static string NormalizePhienBan(string value)
        {
            string raw = (value ?? String.Empty).Trim().ToUpperInvariant();
            if (String.IsNullOrWhiteSpace(raw))
            {
                return "V1";
            }

            if (raw.Length < 2 || raw[0] != 'V')
            {
                throw new InvalidOperationException("Phiên bản không hợp lệ.");
            }

            int version;
            if (!Int32.TryParse(raw.Substring(1), out version) || version < 1)
            {
                throw new InvalidOperationException("Phiên bản không hợp lệ.");
            }
            return "V" + version.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string BuildSampleId(string contract, string version, string sampleNumber)
        {
            string normalizedContract = NormalizeSoHopDong(contract);
            string normalizedVersion = NormalizePhienBan(version);
            int number;
            if (String.IsNullOrWhiteSpace(normalizedContract)
                || !Int32.TryParse(sampleNumber, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out number)
                || number < 1)
            {
                throw new InvalidOperationException("Không thể sinh Sample_ID.");
            }
            return "RS-" + normalizedContract + "-" + normalizedVersion + "-" + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string NormalizeSampleStatus(string value)
        {
            string normalized = (value ?? String.Empty).Trim();
            if (String.Equals(normalized, "Đã may", StringComparison.OrdinalIgnoreCase)) return "Đã may";
            if (String.Equals(normalized, "Đang chờ NPL", StringComparison.OrdinalIgnoreCase)) return "Đang chờ NPL";
            if (String.Equals(normalized, "Đang chờ nhãn xanh", StringComparison.OrdinalIgnoreCase)) return "Đang chờ nhãn xanh";
            return "Chờ may";
        }

        public static bool IsCanceledRequest(SampleRequestRecord request)
        {
            return request != null
                && String.Equals((request.TrangThai ?? String.Empty).Trim(), "Hủy", StringComparison.OrdinalIgnoreCase);
        }

        public static string GetRequestDisplayStatus(
            SampleRequestRecord request,
            IList<SampleManagementRecord> samples)
        {
            if (request == null)
            {
                return String.Empty;
            }
            if (IsCanceledRequest(request))
            {
                return "Hủy";
            }
            if (String.Equals((request.TrangThai ?? String.Empty).Trim(), "Đã phân phối", StringComparison.OrdinalIgnoreCase))
            {
                return "Đã phân phối";
            }

            bool hasSample = false;
            bool hasWaitingMaterial = false;
            bool hasWaitingGreenLabel = false;
            bool hasWaitingToSew = false;
            bool allCompleted = true;
            if (samples != null)
            {
                for (int index = 0; index < samples.Count; index++)
                {
                    if (!String.Equals(samples[index].YeuCauId, request.YeuCauId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    hasSample = true;
                    string status = NormalizeSampleStatus(samples[index].TrangThaiMay);
                    if (status == "Đang chờ NPL") hasWaitingMaterial = true;
                    if (status == "Đang chờ nhãn xanh") hasWaitingGreenLabel = true;
                    if (status == "Chờ may") hasWaitingToSew = true;
                    if (status != "Đã may") allCompleted = false;
                }
            }

            if (!hasSample)
            {
                return String.IsNullOrWhiteSpace(request.TrangThai) ? "Mới" : request.TrangThai.Trim();
            }
            if (hasWaitingMaterial) return "Đang chờ NPL";
            if (hasWaitingGreenLabel) return "Đang chờ nhãn xanh";
            if (hasWaitingToSew) return "Chờ may";
            return allCompleted ? "Đã may" : "Chờ may";
        }
    }
}
