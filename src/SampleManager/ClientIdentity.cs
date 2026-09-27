using System;
using System.IO;
using System.Text;

namespace SampleManager
{
    internal static class ClientIdentity
    {
        private static readonly object SyncRoot = new object();
        private static string cachedId;

        public static string GetOrCreate()
        {
            lock (SyncRoot)
            {
                if (!String.IsNullOrWhiteSpace(cachedId))
                {
                    return cachedId;
                }

                string path = FilePath;
                string existing = String.Empty;
                try
                {
                    if (File.Exists(path)) existing = File.ReadAllText(path, Encoding.UTF8).Trim();
                }
                catch (IOException)
                {
                    existing = String.Empty;
                }

                Guid parsed;
                if (Guid.TryParse(existing, out parsed))
                {
                    cachedId = parsed.ToString("D");
                    return cachedId;
                }

                string created = Guid.NewGuid().ToString("D");
                string directory = Path.GetDirectoryName(path);
                Directory.CreateDirectory(directory);
                File.WriteAllText(path, created, new UTF8Encoding(false));
                cachedId = created;
                return cachedId;
            }
        }

        public static string FilePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CPC",
                    "Sample Manager",
                    "client-id.dat");
            }
        }
    }
}
