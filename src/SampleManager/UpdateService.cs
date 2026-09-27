using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SampleManager
{
    internal static class UpdateConfiguration
    {
        internal const string ProductionManifestUrl =
            "https://github.com/kingstorm1312-ai/Sample-Manager/releases/latest/download/update.json";
        internal const string ManifestOverrideEnvironmentVariable =
            "SAMPLE_MANAGER_UPDATE_MANIFEST_URL";
    }

    internal sealed class UpdateManifest
    {
        internal string VersionText { get; private set; }
        internal Version Version { get; private set; }
        internal string PackageUrl { get; private set; }
        internal string Sha256 { get; private set; }
        internal bool Mandatory { get; private set; }
        internal string Notes { get; private set; }

        internal UpdateManifest(
            string versionText,
            Version version,
            string packageUrl,
            string sha256,
            bool mandatory,
            string notes)
        {
            VersionText = versionText;
            Version = version;
            PackageUrl = packageUrl;
            Sha256 = sha256;
            Mandatory = mandatory;
            Notes = notes;
        }
    }

    internal sealed class UpdateCheckResult
    {
        internal string CurrentProductVersion { get; private set; }
        internal UpdateManifest Manifest { get; private set; }
        internal bool IsUpdateAvailable { get; private set; }

        internal UpdateCheckResult(
            string currentProductVersion,
            UpdateManifest manifest,
            bool isUpdateAvailable)
        {
            CurrentProductVersion = currentProductVersion;
            Manifest = manifest;
            IsUpdateAvailable = isUpdateAvailable;
        }
    }

    internal sealed class DownloadedUpdatePackage
    {
        internal string PackagePath { get; private set; }
        internal string Sha256 { get; private set; }

        internal DownloadedUpdatePackage(string packagePath, string sha256)
        {
            PackagePath = packagePath;
            Sha256 = sha256;
        }
    }

    internal sealed class UpdateService
    {
        private const int ManifestTimeoutMilliseconds = 5000;
        private const int PackageTimeoutMilliseconds = 30000;
        private const string MainExecutableName = "Sample Manager.exe";
        private const string UpdaterExecutableName = "Sample Manager Updater.exe";

        internal UpdateCheckResult CheckForUpdate()
        {
            string executablePath = Application.ExecutablePath;
            string currentProductVersion = FileVersionInfo.GetVersionInfo(executablePath).ProductVersion;
            Version currentVersion = ParseVersion(currentProductVersion, "current ProductVersion");
            string manifestJson = DownloadText(GetManifestUrl(), ManifestTimeoutMilliseconds);
            UpdateManifest manifest = ParseManifest(manifestJson);

            return new UpdateCheckResult(
                currentProductVersion,
                manifest,
                manifest.Version > currentVersion);
        }

        internal DownloadedUpdatePackage DownloadAndVerify(UpdateManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException("manifest");
            }

            string updateDirectory = Path.Combine(Path.GetTempPath(), "SampleManager-Updates");
            Directory.CreateDirectory(updateDirectory);
            string packagePath = Path.Combine(
                updateDirectory,
                "Sample Manager-" + Guid.NewGuid().ToString("N") + ".zip");

            try
            {
                DownloadFile(manifest.PackageUrl, packagePath, PackageTimeoutMilliseconds);
                string actualSha256 = ComputeSha256(packagePath);
                if (!String.Equals(actualSha256, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "SHA-256 package mismatch. Expected " + manifest.Sha256
                        + ", actual " + actualSha256 + ".");
                }

                return new DownloadedUpdatePackage(packagePath, actualSha256);
            }
            catch
            {
                TryDeleteGeneratedPackage(packagePath, updateDirectory);
                throw;
            }
        }

        internal void LaunchUpdater(DownloadedUpdatePackage package)
        {
            if (package == null || String.IsNullOrEmpty(package.PackagePath))
            {
                throw new ArgumentException("Downloaded update package is missing.");
            }

            string applicationDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string updaterPath = Path.Combine(applicationDirectory, UpdaterExecutableName);
            if (!File.Exists(updaterPath))
            {
                throw new FileNotFoundException("Sample Manager Updater.exe was not found.", updaterPath);
            }

            string targetPath = Path.GetFullPath(Application.ExecutablePath);
            if (!String.Equals(
                Path.GetFileName(targetPath),
                MainExecutableName,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The current process is not Sample Manager.exe.");
            }

            int processId = Process.GetCurrentProcess().Id;
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = updaterPath,
                WorkingDirectory = applicationDirectory,
                UseShellExecute = true,
                Arguments = "--pid " + processId.ToString()
                    + " --package " + QuoteArgument(package.PackagePath)
                    + " --target " + QuoteArgument(targetPath)
                    + " --sha256 " + package.Sha256
                    + " --restart"
            };
            Process.Start(startInfo);
        }

        internal static string GetManifestUrl()
        {
            string overrideUrl = Environment.GetEnvironmentVariable(
                UpdateConfiguration.ManifestOverrideEnvironmentVariable);
            if (!String.IsNullOrWhiteSpace(overrideUrl))
            {
                return overrideUrl.Trim();
            }

            return UpdateConfiguration.ProductionManifestUrl;
        }

        private static UpdateManifest ParseManifest(string json)
        {
            if (String.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException("Update manifest is empty.");
            }

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            object parsed = serializer.DeserializeObject(json);
            Dictionary<string, object> values = parsed as Dictionary<string, object>;
            if (values == null)
            {
                throw new InvalidOperationException("Update manifest must be a JSON object.");
            }

            string versionText = RequiredString(values, "version");
            Version version = ParseVersion(versionText, "manifest version");
            string packageUrl = OptionalString(values, "downloadUrl");
            if (String.IsNullOrWhiteSpace(packageUrl))
            {
                packageUrl = OptionalString(values, "url");
            }
            if (String.IsNullOrWhiteSpace(packageUrl))
            {
                throw new InvalidOperationException("Update manifest field is missing: downloadUrl.");
            }
            ValidateHttpUrl(packageUrl, "manifest url");
            string sha256 = RequiredString(values, "sha256");
            ValidateSha256(sha256);

            return new UpdateManifest(
                versionText,
                version,
                packageUrl,
                sha256.ToLowerInvariant(),
                OptionalBoolean(values, "mandatory"),
                OptionalString(values, "notes"));
        }

        private static string RequiredString(Dictionary<string, object> values, string key)
        {
            object value;
            if (!values.TryGetValue(key, out value) || value == null)
            {
                throw new InvalidOperationException("Update manifest field is missing: " + key + ".");
            }

            string text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            if (String.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("Update manifest field is empty: " + key + ".");
            }

            return text.Trim();
        }

        private static bool OptionalBoolean(Dictionary<string, object> values, string key)
        {
            object value;
            if (!values.TryGetValue(key, out value) || value == null)
            {
                return false;
            }

            if (value is Boolean)
            {
                return (Boolean)value;
            }

            bool parsed;
            if (Boolean.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), out parsed))
            {
                return parsed;
            }

            throw new InvalidOperationException("Update manifest field is not boolean: " + key + ".");
        }

        private static string OptionalString(Dictionary<string, object> values, string key)
        {
            object value;
            if (!values.TryGetValue(key, out value) || value == null)
            {
                return String.Empty;
            }

            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture).Trim();
        }

        private static Version ParseVersion(string value, string label)
        {
            Version parsed;
            if (!Version.TryParse(value, out parsed) || parsed.Build < 0)
            {
                throw new InvalidOperationException(label + " is not a numeric x.y.z version.");
            }

            return parsed;
        }

        private static void ValidateHttpUrl(string value, string label)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(label + " must be an absolute HTTP(S) URL.");
            }
        }

        private static void ValidateSha256(string value)
        {
            if (value.Length != 64)
            {
                throw new InvalidOperationException("Manifest sha256 must contain 64 hexadecimal characters.");
            }

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f')
                    || (character >= 'A' && character <= 'F')))
                {
                    throw new InvalidOperationException("Manifest sha256 contains a non-hexadecimal character.");
                }
            }
        }

        private static string DownloadText(string url, int timeoutMilliseconds)
        {
            ConfigureTls();
            HttpWebRequest request = CreateRequest(url, timeoutMilliseconds);
            using (WebResponse response = request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private static void DownloadFile(string url, string destinationPath, int timeoutMilliseconds)
        {
            ConfigureTls();
            HttpWebRequest request = CreateRequest(url, timeoutMilliseconds);
            using (WebResponse response = request.GetResponse())
            using (Stream source = response.GetResponseStream())
            using (FileStream destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.SequentialScan))
            {
                source.CopyTo(destination);
            }
        }

        private static HttpWebRequest CreateRequest(string url, int timeoutMilliseconds)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Timeout = timeoutMilliseconds;
            request.ReadWriteTimeout = timeoutMilliseconds;
            request.UserAgent = "Sample Manager/0.1.0 updater-check";
            return request;
        }

        private static void ConfigureTls()
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
        }

        private static string ComputeSha256(string path)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.SequentialScan))
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] digest = sha256.ComputeHash(stream);
                StringBuilder builder = new StringBuilder(digest.Length * 2);
                foreach (byte value in digest)
                {
                    builder.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static void TryDeleteGeneratedPackage(string packagePath, string updateDirectory)
        {
            if (String.IsNullOrEmpty(packagePath) || String.IsNullOrEmpty(updateDirectory))
            {
                return;
            }

            string fullPackagePath = Path.GetFullPath(packagePath);
            string fullDirectory = Path.GetFullPath(updateDirectory);
            string prefix = fullDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string fileName = Path.GetFileName(fullPackagePath);
            if (!fullPackagePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || String.IsNullOrEmpty(fileName)
                || !fileName.StartsWith("Sample Manager-", StringComparison.Ordinal)
                || !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (File.Exists(fullPackagePath))
            {
                File.Delete(fullPackagePath);
            }
        }
    }

    internal static class UpdateCoordinator
    {
        private static int checkInProgress;

        internal static void Start(Form owner)
        {
            RunCheck(owner, null, null);
        }

        internal static void CheckNow(
            Form owner,
            Action<string> reportStatus,
            Action completed)
        {
            if (owner == null || owner.IsDisposed)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref checkInProgress, 1, 0) != 0)
            {
                ReportStatus(owner, reportStatus, "Đang kiểm tra cập nhật");
                ReportCompleted(owner, completed);
                return;
            }

            RunCheck(owner, reportStatus, completed, true);
        }

        private static void RunCheck(
            Form owner,
            Action<string> reportStatus,
            Action onCompleted,
            bool alreadyStarted = false)
        {
            if (owner == null || owner.IsDisposed)
            {
                return;
            }

            if (!alreadyStarted && Interlocked.CompareExchange(ref checkInProgress, 1, 0) != 0)
            {
                return;
            }

            UpdateService service = new UpdateService();
            Task.Factory.StartNew<UpdateCheckResult>(
                delegate { return service.CheckForUpdate(); })
                .ContinueWith(
                    delegate(Task<UpdateCheckResult> task)
                    {
                        bool updateStarted = false;
                        try
                        {
                            if (task.IsCanceled || task.IsFaulted || task.Result == null)
                            {
                                ReportStatus(owner, reportStatus, "Không thể kiểm tra cập nhật");
                                return;
                            }

                            UpdateCheckResult result = task.Result;
                            if (!result.IsUpdateAvailable)
                            {
                                ReportStatus(owner, reportStatus, "Đã là bản mới nhất");
                                return;
                            }

                            ReportStatus(owner, reportStatus, "Đang tải cập nhật");
                            DownloadedUpdatePackage package = service.DownloadAndVerify(result.Manifest);
                            service.LaunchUpdater(package);
                            updateStarted = true;
                            PostToUi(owner, delegate
                            {
                                ReportCompleted(owner, onCompleted);
                                Application.Exit();
                            });
                        }
                        catch
                        {
                            ReportStatus(owner, reportStatus, "Cập nhật thất bại");
                        }
                        finally
                        {
                            if (!updateStarted)
                            {
                                ReportCompleted(owner, onCompleted);
                            }
                            Interlocked.Exchange(ref checkInProgress, 0);
                        }
                    },
                    TaskScheduler.Default);
        }

        private static void ReportStatus(Form owner, Action<string> reportStatus, string status)
        {
            if (reportStatus == null)
            {
                return;
            }

            PostToUi(owner, delegate { reportStatus(status); });
        }

        private static void ReportCompleted(Form owner, Action completed)
        {
            if (completed == null)
            {
                return;
            }

            PostToUi(owner, delegate { completed(); });
        }

        private static void PostToUi(Form owner, MethodInvoker action)
        {
            if (owner == null || owner.IsDisposed || owner.Disposing || !owner.IsHandleCreated)
            {
                return;
            }

            try
            {
                owner.BeginInvoke(action);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
