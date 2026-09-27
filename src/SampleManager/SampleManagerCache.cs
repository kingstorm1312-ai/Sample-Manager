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
            Replace(nextQaOptions, nextRequests, nextSamples);
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
                        nextRequests[index] = CloneRequest(record);
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
                            nextSamples[index] = CloneSample(addedSamples[addedIndex]);
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
                GhiChu = source.GhiChu
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
                TrangThaiMay = source.TrangThaiMay
            };
        }
    }
}
