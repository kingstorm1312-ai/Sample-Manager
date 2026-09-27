using System;
using System.Collections.Generic;

namespace SampleManager
{
    internal sealed class SampleManagerCacheSnapshot
    {
        public SampleManagerCacheSnapshot(
            IList<QaOption> qaOptions,
            IList<SampleRequestRecord> requests,
            IList<SampleManagementRecord> samples,
            DateTime updatedAtLocal,
            bool isLoaded)
        {
            QaOptions = qaOptions;
            Requests = requests;
            Samples = samples;
            UpdatedAtLocal = updatedAtLocal;
            IsLoaded = isLoaded;
        }

        public IList<QaOption> QaOptions { get; private set; }
        public IList<SampleRequestRecord> Requests { get; private set; }
        public IList<SampleManagementRecord> Samples { get; private set; }
        public DateTime UpdatedAtLocal { get; private set; }
        public bool IsLoaded { get; private set; }
    }

    internal sealed class SampleManagerCache
    {
        private readonly object syncRoot = new object();
        private IList<QaOption> qaOptions = new List<QaOption>();
        private IList<SampleRequestRecord> requests = new List<SampleRequestRecord>();
        private IList<SampleManagementRecord> samples = new List<SampleManagementRecord>();
        private DateTime updatedAtLocal;
        private bool isLoaded;

        public event EventHandler Changed;

        public SampleManagerCacheSnapshot Snapshot()
        {
            lock (syncRoot)
            {
                return new SampleManagerCacheSnapshot(
                    CloneQaOptions(qaOptions),
                    CloneRequests(requests),
                    CloneSamples(samples),
                    updatedAtLocal,
                    isLoaded);
            }
        }

        public void LoadLive(GoogleSheetsSampleRepository repository)
        {
            IList<QaOption> liveQaOptions = repository.ReadActiveQaOptions();
            IList<SampleRequestRecord> liveRequests = repository.ReadAllRequests();
            IList<SampleManagementRecord> liveSamples = repository.ReadAllSamples();
            ReplaceLive(liveQaOptions, liveRequests, liveSamples);
        }

        public void ReplaceLive(
            IList<QaOption> nextQaOptions,
            IList<SampleRequestRecord> nextRequests,
            IList<SampleManagementRecord> nextSamples)
        {
            lock (syncRoot)
            {
                if (!isLoaded)
                {
                    qaOptions = CloneQaOptions(nextQaOptions);
                    requests = CloneRequests(nextRequests);
                    samples = CloneSamples(nextSamples);
                }
                else
                {
                    qaOptions = CloneQaOptions(nextQaOptions);
                    requests = MergeRequests(requests, nextRequests);
                    samples = MergeSamples(samples, nextSamples);
                }
                updatedAtLocal = DateTime.Now;
                isLoaded = true;
            }
            NotifyChanged();
        }

        public void AddOrReplace(SampleRequestRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException("record");
            }

            lock (syncRoot)
            {
                IList<SampleRequestRecord> nextRequests = CloneRequests(requests);
                bool replaced = false;
                for (int index = 0; index < nextRequests.Count; index++)
                {
                    if (String.Equals(nextRequests[index].YeuCauId, record.YeuCauId, StringComparison.Ordinal))
                    {
                        if (CompareVersion(record.RowVersion, nextRequests[index].RowVersion) >= 0)
                        {
                            nextRequests[index] = CloneRequest(record);
                        }
                        replaced = true;
                        break;
                    }
                }
                if (!replaced)
                {
                    nextRequests.Add(CloneRequest(record));
                }
                requests = nextRequests;
                updatedAtLocal = DateTime.Now;
                isLoaded = true;
            }
            NotifyChanged();
        }

        public void AddOrReplaceSamples(IList<SampleManagementRecord> addedSamples)
        {
            if (addedSamples == null)
            {
                throw new ArgumentNullException("addedSamples");
            }

            lock (syncRoot)
            {
                IList<SampleManagementRecord> nextSamples = CloneSamples(samples);
                for (int addedIndex = 0; addedIndex < addedSamples.Count; addedIndex++)
                {
                    bool replaced = false;
                    for (int index = 0; index < nextSamples.Count; index++)
                    {
                        if (String.Equals(nextSamples[index].MauId, addedSamples[addedIndex].MauId, StringComparison.Ordinal))
                        {
                            if (CompareVersion(addedSamples[addedIndex].RowVersion, nextSamples[index].RowVersion) >= 0)
                            {
                                nextSamples[index] = CloneSample(addedSamples[addedIndex]);
                            }
                            replaced = true;
                            break;
                        }
                    }
                    if (!replaced)
                    {
                        nextSamples.Add(CloneSample(addedSamples[addedIndex]));
                    }
                }
                samples = nextSamples;
                updatedAtLocal = DateTime.Now;
                isLoaded = true;
            }
            NotifyChanged();
        }

        public void AddOrReplaceSample(SampleManagementRecord sample)
        {
            if (sample == null)
            {
                throw new ArgumentNullException("sample");
            }
            AddOrReplaceSamples(new[] { sample });
        }

        private void Replace(
            IList<QaOption> nextQaOptions,
            IList<SampleRequestRecord> nextRequests,
            IList<SampleManagementRecord> nextSamples)
        {
            if (nextQaOptions == null || nextRequests == null || nextSamples == null)
            {
                throw new ArgumentNullException("nextRequests");
            }

            lock (syncRoot)
            {
                qaOptions = CloneQaOptions(nextQaOptions);
                requests = CloneRequests(nextRequests);
                samples = CloneSamples(nextSamples);
                updatedAtLocal = DateTime.Now;
                isLoaded = true;
            }
            NotifyChanged();
        }

        private void NotifyChanged()
        {
            EventHandler handler = Changed;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private static IList<QaOption> CloneQaOptions(IList<QaOption> source)
        {
            IList<QaOption> result = new List<QaOption>();
            for (int index = 0; index < source.Count; index++)
            {
                result.Add(new QaOption
                {
                    Id = source[index].Id,
                    DisplayName = source[index].DisplayName
                });
            }
            return result;
        }

        private static IList<SampleRequestRecord> CloneRequests(IList<SampleRequestRecord> source)
        {
            IList<SampleRequestRecord> result = new List<SampleRequestRecord>();
            for (int index = 0; index < source.Count; index++)
            {
                result.Add(CloneRequest(source[index]));
            }
            return result;
        }

        private static IList<SampleRequestRecord> MergeRequests(
            IList<SampleRequestRecord> current,
            IList<SampleRequestRecord> live)
        {
            IList<SampleRequestRecord> result = CloneRequests(live);
            for (int currentIndex = 0; currentIndex < current.Count; currentIndex++)
            {
                bool found = false;
                for (int liveIndex = 0; liveIndex < result.Count; liveIndex++)
                {
                    if (!String.Equals(result[liveIndex].YeuCauId, current[currentIndex].YeuCauId, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    found = true;
                    if (CompareVersion(current[currentIndex].RowVersion, result[liveIndex].RowVersion) > 0)
                    {
                        result[liveIndex] = CloneRequest(current[currentIndex]);
                    }
                    break;
                }
                if (!found) result.Add(CloneRequest(current[currentIndex]));
            }
            return result;
        }

        private static SampleRequestRecord CloneRequest(SampleRequestRecord source)
        {
            return new SampleRequestRecord
            {
                YeuCauId = source.YeuCauId,
                NgayTaoYeuCau = source.NgayTaoYeuCau,
                SoHopDong = source.SoHopDong,
                MaVatTu = source.MaVatTu,
                TenTui = source.TenTui,
                QaId = source.QaId,
                NoiYeuCau = source.NoiYeuCau,
                SoLuongMau = source.SoLuongMau,
                PhienBan = source.PhienBan,
                Deadline = source.Deadline,
                TrangThai = source.TrangThai,
                GhiChu = source.GhiChu,
                RowVersion = source.RowVersion
            };
        }

        private static IList<SampleManagementRecord> CloneSamples(IList<SampleManagementRecord> source)
        {
            IList<SampleManagementRecord> result = new List<SampleManagementRecord>();
            for (int index = 0; index < source.Count; index++)
            {
                result.Add(CloneSample(source[index]));
            }
            return result;
        }

        private static IList<SampleManagementRecord> MergeSamples(
            IList<SampleManagementRecord> current,
            IList<SampleManagementRecord> live)
        {
            IList<SampleManagementRecord> result = CloneSamples(live);
            for (int currentIndex = 0; currentIndex < current.Count; currentIndex++)
            {
                bool found = false;
                for (int liveIndex = 0; liveIndex < result.Count; liveIndex++)
                {
                    if (!String.Equals(result[liveIndex].MauId, current[currentIndex].MauId, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    found = true;
                    if (CompareVersion(current[currentIndex].RowVersion, result[liveIndex].RowVersion) > 0)
                    {
                        result[liveIndex] = CloneSample(current[currentIndex]);
                    }
                    break;
                }
                if (!found) result.Add(CloneSample(current[currentIndex]));
            }
            return result;
        }

        private static SampleManagementRecord CloneSample(SampleManagementRecord source)
        {
            return new SampleManagementRecord
            {
                MauId = source.MauId,
                YeuCauId = source.YeuCauId,
                SampleId = source.SampleId,
                PhienBan = source.PhienBan,
                SttMau = source.SttMau,
                NgayDuyet = source.NgayDuyet,
                NgayHetHan = source.NgayHetHan,
                NguoiDuyet = source.NguoiDuyet,
                NoiLuu = source.NoiLuu,
                NgayGiaoMau = source.NgayGiaoMau,
                GhiChu = source.GhiChu,
                TrangThaiMay = source.TrangThaiMay,
                RowVersion = source.RowVersion,
                ClaimOwner = source.ClaimOwner,
                ClaimedAt = source.ClaimedAt
            };
        }

        private static int CompareVersion(string left, string right)
        {
            int leftVersion = ParseVersion(left);
            int rightVersion = ParseVersion(right);
            return leftVersion.CompareTo(rightVersion);
        }

        private static int ParseVersion(string value)
        {
            int version;
            return Int32.TryParse(value, out version) && version >= 0 ? version : 0;
        }
    }
}
