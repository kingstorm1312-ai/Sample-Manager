using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;

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

        public async Task<SampleRequestCreationResult> CreateAsync(
            SampleRequestRecord request,
            string operationId)
        {
            if (request == null || String.IsNullOrWhiteSpace(request.YeuCauId))
            {
                throw new InvalidOperationException("Yêu cầu không hợp lệ.");
            }

            int sampleCount = ParseSampleCount(request.SoLuongMau);
            try
            {
                GatewayCreateResult result = await repository.CreateRequestAndSamplesAsync(
                    request,
                    sampleCount,
                    operationId);
                if (result == null || result.Request == null || result.Samples == null)
                {
                    throw new InvalidOperationException("Gateway trả về kết quả tạo mẫu không hợp lệ.");
                }

                cache.AddOrReplace(result.Request);
                cache.AddOrReplaceSamples(result.Samples);
                return new SampleRequestCreationResult(result.Request, result.Samples);
            }
            catch (Exception exception)
            {
                throw new SampleRequestCreationException(
                    "Không thể hoàn tất yêu cầu qua write gateway."
                        + Environment.NewLine
                        + BuildDiagnostic(exception, operationId),
                    exception,
                    false,
                    request);
            }
        }

        private static string BuildDiagnostic(Exception exception, string operationId)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("operationId=").Append(operationId ?? String.Empty)
                .Append("; operationType=CREATE_REQUEST");

            int depth = 0;
            for (Exception current = exception; current != null && depth < 8; current = current.InnerException)
            {
                builder.Append(Environment.NewLine)
                    .Append("exception[").Append(depth.ToString(CultureInfo.InvariantCulture)).Append("]=")
                    .Append(current.GetType().FullName)
                    .Append(": ").Append(current.Message);

                SampleGatewayException gatewayException = current as SampleGatewayException;
                if (gatewayException != null)
                {
                    builder.Append("; code=").Append(gatewayException.Code)
                        .Append("; httpStatus=")
                        .Append(gatewayException.HttpStatusCode.HasValue
                            ? gatewayException.HttpStatusCode.Value.ToString(CultureInfo.InvariantCulture)
                            : "N/A")
                        .Append("; endpoint=")
                        .Append(String.IsNullOrWhiteSpace(gatewayException.Endpoint) ? "N/A" : gatewayException.Endpoint)
                        .Append("; responseBody=")
                        .Append(String.IsNullOrWhiteSpace(gatewayException.ResponseBody) ? "N/A" : gatewayException.ResponseBody);
                }

                depth++;
            }

            return builder.ToString();
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

    }
}
