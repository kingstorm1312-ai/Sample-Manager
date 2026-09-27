using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;

namespace SampleManagerUpdater
{
    internal static class Program
    {
        private const string MainExecutableName = "Sample Manager.exe";
        private const string ErrorLogEnvironmentVariable = "SAMPLE_MANAGER_UPDATER_ERROR_LOG";
        private const string HeadlessEnvironmentVariable = "SAMPLE_MANAGER_UPDATER_HEADLESS";
        private static readonly string[] ProtectedRootNames =
        {
            ".secrets",
            "Cache",
            "Logs",
            "Backups"
        };
        private static readonly TimeSpan ProcessWaitTimeout = TimeSpan.FromMinutes(5);

        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            catch (Exception exception)
            {
                return Fail(exception.GetBaseException().Message);
            }
        }

        private static int Run(string[] args)
        {
            Options options = ParseArguments(args);
            string targetExecutable = ResolveTargetExecutable(options.Target);
            EnsureNoProtectedPathSegment(Path.GetDirectoryName(targetExecutable), "target");

            using (PackageSource package = OpenAndValidatePackage(options.Package, options.ExpectedSha256))
            {
                WaitForMainProcessExit(options.ProcessId, targetExecutable);

                string targetDirectory = Path.GetDirectoryName(targetExecutable);
                string stagingDirectory = null;
                string replacementPath = null;
                Exception cleanupFailure = null;

                try
                {
                    stagingDirectory = CreateStagingDirectory(targetDirectory);
                    string stagedExecutable = ExtractExecutable(package.ExecutableEntry, stagingDirectory);

                    replacementPath = Path.Combine(
                        targetDirectory,
                        ".sample-manager-updater-replacement-" + Guid.NewGuid().ToString("N") + ".tmp");
                    File.Copy(stagedExecutable, replacementPath, false);

                    File.Replace(replacementPath, targetExecutable, null);
                    replacementPath = null;

                    DeleteGeneratedStagingDirectory(stagingDirectory, targetDirectory);
                    stagingDirectory = null;

                    RelaunchMainApplication(targetExecutable, targetDirectory);
                }
                finally
                {
                    try
                    {
                        if (!String.IsNullOrEmpty(replacementPath))
                        {
                            DeleteGeneratedReplacementFile(replacementPath, targetDirectory);
                        }
                    }
                    catch (Exception exception)
                    {
                        cleanupFailure = exception;
                    }

                    try
                    {
                        if (!String.IsNullOrEmpty(stagingDirectory))
                        {
                            DeleteGeneratedStagingDirectory(stagingDirectory, targetDirectory);
                        }
                    }
                    catch (Exception exception)
                    {
                        cleanupFailure = exception;
                    }
                }

                if (cleanupFailure != null)
                {
                    throw new InvalidOperationException(
                        "Updater cleanup failed after the update operation: " + cleanupFailure.Message,
                        cleanupFailure);
                }
            }

            return 0;
        }

        private static Options ParseArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                throw new ArgumentException(
                    "Required arguments: --pid <pid> --package <zip> --target <Sample Manager.exe> --sha256 <hash> --restart");
            }

            Options options = new Options();
            HashSet<string> seenOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < args.Length; index++)
            {
                string token = args[index] ?? String.Empty;
                string optionName = token;
                string inlineValue = null;
                int equalsIndex = token.IndexOf('=');
                if (equalsIndex > 0)
                {
                    optionName = token.Substring(0, equalsIndex);
                    inlineValue = token.Substring(equalsIndex + 1);
                }

                if (String.Equals(optionName, "--restart", StringComparison.OrdinalIgnoreCase))
                {
                    if (inlineValue != null || !seenOptions.Add(optionName))
                    {
                        throw new ArgumentException("--restart must be supplied once as a flag.");
                    }

                    options.Restart = true;
                    continue;
                }

                if (!String.Equals(optionName, "--pid", StringComparison.OrdinalIgnoreCase)
                    && !String.Equals(optionName, "--package", StringComparison.OrdinalIgnoreCase)
                    && !String.Equals(optionName, "--target", StringComparison.OrdinalIgnoreCase)
                    && !String.Equals(optionName, "--sha256", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("Unknown updater argument: " + optionName);
                }

                if (!seenOptions.Add(optionName))
                {
                    throw new ArgumentException("Duplicate updater argument: " + optionName);
                }

                string value = inlineValue;
                if (value == null)
                {
                    if (index + 1 >= args.Length)
                    {
                        throw new ArgumentException("Missing value for " + optionName + ".");
                    }

                    value = args[++index];
                }

                value = Unquote(value);
                if (String.IsNullOrWhiteSpace(value))
                {
                    throw new ArgumentException("Empty value for " + optionName + ".");
                }

                if (String.Equals(optionName, "--pid", StringComparison.OrdinalIgnoreCase))
                {
                    int processId;
                    if (!Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out processId)
                        || processId <= 0)
                    {
                        throw new ArgumentException("--pid must be a positive process ID.");
                    }

                    options.ProcessId = processId;
                }
                else if (String.Equals(optionName, "--package", StringComparison.OrdinalIgnoreCase))
                {
                    options.Package = value;
                }
                else if (String.Equals(optionName, "--target", StringComparison.OrdinalIgnoreCase))
                {
                    options.Target = value;
                }
                else
                {
                    options.ExpectedSha256 = value;
                }
            }

            if (options.ProcessId <= 0
                || String.IsNullOrEmpty(options.Package)
                || String.IsNullOrEmpty(options.Target)
                || String.IsNullOrEmpty(options.ExpectedSha256)
                || !options.Restart)
            {
                throw new ArgumentException(
                    "Required arguments: --pid <pid> --package <zip> --target <Sample Manager.exe> --sha256 <hash> --restart");
            }

            ValidateExpectedSha256(options.ExpectedSha256);
            return options;
        }

        private static string Unquote(string value)
        {
            string cleaned = (value ?? String.Empty).Trim();
            if (cleaned.Length >= 2 && cleaned[0] == '"' && cleaned[cleaned.Length - 1] == '"')
            {
                return cleaned.Substring(1, cleaned.Length - 2);
            }

            return cleaned;
        }

        private static string ResolveTargetExecutable(string targetArgument)
        {
            string targetPath = Path.GetFullPath(Unquote(targetArgument));
            if (Directory.Exists(targetPath))
            {
                targetPath = Path.Combine(targetPath, MainExecutableName);
            }

            if (!String.Equals(Path.GetFileName(targetPath), MainExecutableName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "--target must identify Sample Manager.exe or its containing directory.");
            }

            if (!File.Exists(targetPath))
            {
                throw new FileNotFoundException("Target executable was not found.", targetPath);
            }

            return targetPath;
        }

        private static PackageSource OpenAndValidatePackage(string packageArgument, string expectedSha256)
        {
            string packagePath = Path.GetFullPath(Unquote(packageArgument));
            if (!File.Exists(packagePath))
            {
                throw new FileNotFoundException("Update package was not found.", packagePath);
            }

            if (!String.Equals(Path.GetExtension(packagePath), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("--package must point to a .zip package.");
            }

            FileStream packageStream = null;
            ZipArchive archive = null;
            try
            {
                packageStream = new FileStream(
                    packagePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    64 * 1024,
                    FileOptions.SequentialScan);

                string actualSha256 = ComputeSha256(packageStream);
                if (!String.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Package SHA-256 mismatch. Expected " + expectedSha256 + ", actual " + actualSha256 + ".");
                }

                packageStream.Position = 0;
                archive = new ZipArchive(packageStream, ZipArchiveMode.Read, true);
                ZipArchiveEntry executableEntry = ValidatePackageEntries(archive);
                return new PackageSource(packageStream, archive, executableEntry);
            }
            catch
            {
                if (archive != null)
                {
                    archive.Dispose();
                }

                if (packageStream != null)
                {
                    packageStream.Dispose();
                }

                throw;
            }
        }

        private static string ComputeSha256(Stream stream)
        {
            stream.Position = 0;
            using (SHA256 sha256 = SHA256.Create())
            {
                return ToLowerHex(sha256.ComputeHash(stream));
            }
        }

        private static string ToLowerHex(byte[] bytes)
        {
            char[] chars = new char[bytes.Length * 2];
            const string alphabet = "0123456789abcdef";
            for (int index = 0; index < bytes.Length; index++)
            {
                chars[index * 2] = alphabet[bytes[index] >> 4];
                chars[index * 2 + 1] = alphabet[bytes[index] & 0x0f];
            }

            return new String(chars);
        }

        private static void ValidateExpectedSha256(string value)
        {
            if (value == null || value.Length != 64)
            {
                throw new ArgumentException("--sha256 must be exactly 64 hexadecimal characters.");
            }

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool isDigit = character >= '0' && character <= '9';
                bool isLower = character >= 'a' && character <= 'f';
                bool isUpper = character >= 'A' && character <= 'F';
                if (!isDigit && !isLower && !isUpper)
                {
                    throw new ArgumentException("--sha256 must contain only hexadecimal characters.");
                }
            }
        }

        private static ZipArchiveEntry ValidatePackageEntries(ZipArchive archive)
        {
            ZipArchiveEntry executableEntry = null;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string entryName = entry.FullName ?? String.Empty;
                ValidateArchiveEntryPath(entryName);

                if (entryName.EndsWith("/", StringComparison.Ordinal)
                    || entryName.EndsWith("\\", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Package directories are not allowed: " + entryName);
                }

                if (!String.Equals(entryName, MainExecutableName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Package may contain only the root Sample Manager.exe entry.");
                }

                if (executableEntry != null)
                {
                    throw new InvalidOperationException("Package contains duplicate target executable entries.");
                }

                executableEntry = entry;
            }

            if (executableEntry == null)
            {
                throw new InvalidOperationException("Package does not contain Sample Manager.exe.");
            }

            return executableEntry;
        }

        private static void ValidateArchiveEntryPath(string entryName)
        {
            if (String.IsNullOrEmpty(entryName) || entryName.IndexOf('\0') >= 0)
            {
                throw new InvalidOperationException("Package contains an empty or invalid archive path.");
            }

            string normalized = entryName.Replace('/', '\\');
            if (normalized.StartsWith("\\", StringComparison.Ordinal)
                || Path.IsPathRooted(normalized)
                || (normalized.Length >= 2 && Char.IsLetter(normalized[0]) && normalized[1] == ':'))
            {
                throw new InvalidOperationException("Package contains an absolute archive path: " + entryName);
            }

            string[] segments = normalized.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                throw new InvalidOperationException("Package contains an invalid archive path: " + entryName);
            }

            foreach (string segment in segments)
            {
                if (segment == "." || segment == ".." || segment.IndexOf(':') >= 0)
                {
                    throw new InvalidOperationException("Package contains a path-traversal archive path: " + entryName);
                }

                if (IsProtectedRootName(segment))
                {
                    throw new InvalidOperationException(
                        "Package entry targets a protected root: " + segment + ".");
                }
            }
        }

        private static bool IsProtectedRootName(string segment)
        {
            foreach (string protectedRootName in ProtectedRootNames)
            {
                if (String.Equals(segment, protectedRootName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnsureNoProtectedPathSegment(string path, string label)
        {
            string fullPath = Path.GetFullPath(path);
            string root = Path.GetPathRoot(fullPath) ?? String.Empty;
            string relative = fullPath.Substring(root.Length);
            string[] segments = relative.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string segment in segments)
            {
                if (IsProtectedRootName(segment))
                {
                    throw new InvalidOperationException(
                        "The " + label + " path is inside a protected root: " + segment + ".");
                }
            }
        }

        private static void WaitForMainProcessExit(int processId, string targetExecutable)
        {
            Process process;
            try
            {
                process = Process.GetProcessById(processId);
            }
            catch (ArgumentException)
            {
                return;
            }

            using (process)
            {
                if (process.HasExited)
                {
                    return;
                }

                string processPath;
                try
                {
                    processPath = Path.GetFullPath(process.MainModule.FileName);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        "Cannot verify the executable belonging to --pid before mutation.", exception);
                }

                if (!String.Equals(processPath, targetExecutable, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "--pid does not belong to the target Sample Manager.exe process.");
                }

                DateTime deadline = DateTime.UtcNow.Add(ProcessWaitTimeout);
                while (!process.HasExited)
                {
                    if (DateTime.UtcNow >= deadline)
                    {
                        throw new TimeoutException("Timed out waiting for Sample Manager.exe to exit.");
                    }

                    Thread.Sleep(250);
                }
            }
        }

        private static string CreateStagingDirectory(string targetDirectory)
        {
            string stagingDirectory = Path.Combine(
                targetDirectory,
                ".sample-manager-updater-staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingDirectory);
            return stagingDirectory;
        }

        private static string ExtractExecutable(ZipArchiveEntry entry, string stagingDirectory)
        {
            string stagedExecutable = Path.Combine(stagingDirectory, MainExecutableName);
            using (Stream source = entry.Open())
            using (FileStream destination = new FileStream(
                stagedExecutable,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.SequentialScan))
            {
                source.CopyTo(destination);
            }

            FileInfo stagedInfo = new FileInfo(stagedExecutable);
            if (stagedInfo.Length != entry.Length)
            {
                throw new InvalidOperationException("Staged executable length does not match the package entry.");
            }

            return stagedExecutable;
        }

        private static void DeleteGeneratedStagingDirectory(string stagingDirectory, string targetDirectory)
        {
            string fullStagingDirectory = Path.GetFullPath(stagingDirectory);
            string fullTargetDirectory = EnsureTrailingSeparator(Path.GetFullPath(targetDirectory));
            string name = Path.GetFileName(fullStagingDirectory);
            if (!fullStagingDirectory.StartsWith(fullTargetDirectory, StringComparison.OrdinalIgnoreCase)
                || String.IsNullOrEmpty(name)
                || !name.StartsWith(".sample-manager-updater-staging-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Refusing to clean an unexpected staging path.");
            }

            if (Directory.Exists(fullStagingDirectory))
            {
                Directory.Delete(fullStagingDirectory, true);
            }
        }

        private static void DeleteGeneratedReplacementFile(string replacementPath, string targetDirectory)
        {
            string fullReplacementPath = Path.GetFullPath(replacementPath);
            string fullTargetDirectory = EnsureTrailingSeparator(Path.GetFullPath(targetDirectory));
            string name = Path.GetFileName(fullReplacementPath);
            if (!fullReplacementPath.StartsWith(fullTargetDirectory, StringComparison.OrdinalIgnoreCase)
                || String.IsNullOrEmpty(name)
                || !name.StartsWith(".sample-manager-updater-replacement-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Refusing to clean an unexpected replacement path.");
            }

            if (File.Exists(fullReplacementPath))
            {
                File.Delete(fullReplacementPath);
            }
        }

        private static string EnsureTrailingSeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }

        private static void RelaunchMainApplication(string targetExecutable, string targetDirectory)
        {
            if (!File.Exists(targetExecutable))
            {
                throw new FileNotFoundException("Updated target executable is missing before restart.", targetExecutable);
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = targetExecutable,
                WorkingDirectory = targetDirectory,
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }

        private static int Fail(string message)
        {
            string details = "Sample Manager Updater failed: " + message;
            string errorLogPath = Environment.GetEnvironmentVariable(ErrorLogEnvironmentVariable);
            if (!String.IsNullOrEmpty(errorLogPath))
            {
                try
                {
                    File.WriteAllText(errorLogPath, details + Environment.NewLine);
                }
                catch
                {
                }
            }

            if (!String.Equals(
                Environment.GetEnvironmentVariable(HeadlessEnvironmentVariable),
                "1",
                StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    MessageBox.Show(details, "Sample Manager Updater", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch
                {
                }
            }

            return 1;
        }

        private sealed class Options
        {
            public int ProcessId { get; set; }
            public string Package { get; set; }
            public string Target { get; set; }
            public string ExpectedSha256 { get; set; }
            public bool Restart { get; set; }
        }

        private sealed class PackageSource : IDisposable
        {
            private readonly FileStream packageStream;
            private readonly ZipArchive archive;

            public PackageSource(FileStream packageStream, ZipArchive archive, ZipArchiveEntry executableEntry)
            {
                this.packageStream = packageStream;
                this.archive = archive;
                ExecutableEntry = executableEntry;
            }

            public ZipArchiveEntry ExecutableEntry { get; private set; }

            public void Dispose()
            {
                archive.Dispose();
                packageStream.Dispose();
            }
        }
    }
}
