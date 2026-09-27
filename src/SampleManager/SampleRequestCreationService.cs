using System;
using System.Collections.Generic;
using System.Globalization;

namespace SampleManager
{
    internal sealed class SampleRequestCreationResult
    {
        public SampleRequestRecord Request { get; private set; }
        public IList<SampleManagementRecord> Samples { get; private set; }

        public SampleRequestCreationResult(
            SampleRequestRecord request,
            IList<SampleManagementRecord> samples)
        {
            Request = request;
            Samples = samples;
        }
    }

    internal sealed class SampleRequestCreationException : InvalidOperationException
    {
        public bool RequestPersisted { get; private set; }
        public SampleRequestRecord Request { get; private set; }

        public SampleRequestCreationException(
            string message,
            Exception innerException,
            bool requestPersisted,
            SampleRequestRecord request)
            : base(message, innerException)
        {
            RequestPersisted = requestPersisted;
            Request = request;
        }
    }

    internal sealed class SampleRequestCreationService
    {
        private readonly GoogleSheetsSampleRepository repository;
        private readonly SampleManagerCache cache;

        public SampleRequestCreationService(
            GoogleSheetsSampleRepository repository,
            SampleManagerCache cache)
        {
            this.repository = repository;
            this.cache = cache;
        }

        public SampleRequestCreationResult Create(SampleRequestRecord request)
        {
            if (request == null || String.IsNullOrWhiteSpace(request.YeuCauId))
            {
                throw new InvalidOperationException("Yêu cầu không hợp lệ.");
            }

            request.PhienBan = "V1";
            int sampleCount = ParseSampleCount(request.SoLuongMau);
            SampleRequestRecord readBackRequest = EnsureRequest(request);
            IList<SampleManagementRecord> expectedSamples = BuildSamples(readBackRequest, sampleCount);
            IList<SampleManagementRecord> readBackSamples;
            try
            {
                readBackSamples = repository.EnsureSamplesAndReadBack(expectedSamples);
            }
            catch (Exception exception)
            {
                throw new SampleRequestCreationException(
                    "Yêu cầu đã ghi nhưng tạo mẫu chưa hoàn tất.",
                    exception,
                    true,
                    readBackRequest);
            }

            cache.AddOrReplace(readBackRequest);
            cache.AddOrReplaceSamples(readBackSamples);
            return new SampleRequestCreationResult(readBackRequest, readBackSamples);
        }

        private SampleRequestRecord EnsureRequest(SampleRequestRecord request)
        {
            try
            {
                SampleRequestRecord existing = repository.ReadById(request.YeuCauId);
                if (existing != null)
                {
                    return existing;
                }

                return repository.AppendAndReadBack(request);
            }
            catch (Exception firstException)
            {
                try
                {
                    SampleRequestRecord existing = repository.ReadById(request.YeuCauId);
                    if (existing != null)
                    {
                        return existing;
                    }
                }
                catch (Exception readBackException)
                {
                    throw new SampleRequestCreationException(
                        "Không thể xác nhận yêu cầu.",
                        readBackException,
                        false,
                        null);
                }

                throw new SampleRequestCreationException(
                    "Không thể ghi yêu cầu mẫu.",
                    firstException,
                    false,
                    null);
            }
        }

        private static int ParseSampleCount(string value)
        {
            int count;
            if (!Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out count) || count < 1)
            {
                throw new InvalidOperationException("Số lượng mẫu không hợp lệ.");
            }
            return count;
        }

        private static IList<SampleManagementRecord> BuildSamples(
            SampleRequestRecord request,
            int sampleCount)
        {
            string contract = SampleNormalization.NormalizeSoHopDong(request.SoHopDong);
            string version = SampleNormalization.NormalizePhienBan(request.PhienBan);
            IList<SampleManagementRecord> samples = new List<SampleManagementRecord>();
            for (int index = 1; index <= sampleCount; index++)
            {
                samples.Add(new SampleManagementRecord
                {
                    MauId = "MAU-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
                    YeuCauId = request.YeuCauId,
                    SampleId = SampleNormalization.BuildSampleId(
                        contract,
                        version,
                        index.ToString(CultureInfo.InvariantCulture)),
                    PhienBan = version,
                    SttMau = index.ToString(CultureInfo.InvariantCulture),
                    NgayDuyet = String.Empty,
                    NgayHetHan = String.Empty,
                    NguoiDuyet = String.Empty,
                    NoiLuu = String.Empty,
                    NgayGiaoMau = String.Empty,
                    GhiChu = String.Empty,
                    TrangThaiMay = "Chờ may"
                });
            }
            return samples;
        }
    }
}
