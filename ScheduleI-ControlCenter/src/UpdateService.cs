using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ScheduleIControlCenter
{
    internal sealed class UpdateRelease
    {
        public Version Version { get; set; }
        public string VersionText { get; set; }
        public string TagName { get; set; }
        public string AssetName { get; set; }
        public Uri DownloadUri { get; set; }
        public Uri ReleasePageUri { get; set; }
        public string Sha256 { get; set; }
        public long Size { get; set; }
        public string ReleaseNotes { get; set; }
    }

    internal sealed class UpdateCheckResult
    {
        public bool UpdateAvailable { get; set; }
        public UpdateRelease Release { get; set; }
        public string Message { get; set; }
        public DateTime CheckedUtc { get; set; }
        public bool FromCache { get; set; }
    }

    internal sealed class UpdateInstallResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string BackupDirectory { get; set; }
    }

    internal sealed class UpdateService
    {
        internal const string LatestReleaseApi = "https://api.github.com/repos/Enterlessguy/Schedule-I-game-modifier-mod/releases/latest";
        internal const long MaximumAssetBytes = 512L * 1024L * 1024L;
        private const int MaximumMetadataBytes = 2 * 1024 * 1024;

        public Task<UpdateCheckResult> CheckForUpdatesAsync()
        {
            return Task.Run(delegate
            {
                string json = DownloadText(new Uri(LatestReleaseApi), MaximumMetadataBytes);
                UpdateCheckResult result = ParseLatestRelease(json, ReleaseInfo.SemanticVersion);
                SaveCachedResult(result);
                return result;
            });
        }

        public UpdateCheckResult LoadCachedResult()
        {
            try
            {
                string path = CachedMetadataPath();
                if (!File.Exists(path)) return null;
                Dictionary<string, object> data = JsonUtil.ReadObject(path);
                Version version;
                Uri download;
                Uri page;
                if (!TryParseVersion(JsonUtil.GetString(data, "version", string.Empty), out version)
                    || !Uri.TryCreate(JsonUtil.GetString(data, "downloadUrl", string.Empty), UriKind.Absolute, out download)
                    || !IsTrustedReleaseUri(download)
                    || !Uri.TryCreate(JsonUtil.GetString(data, "releasePageUrl", string.Empty), UriKind.Absolute, out page)
                    || !IsTrustedRepositoryPage(page))
                    return null;
                string digest = JsonUtil.GetString(data, "sha256", string.Empty);
                long size = JsonUtil.GetLong(data, "size", -1);
                if (!Regex.IsMatch(digest, "\\A[0-9a-f]{64}\\z", RegexOptions.CultureInvariant)
                    || size <= 0 || size > MaximumAssetBytes)
                    return null;
                DateTime checkedUtc;
                DateTime.TryParse(JsonUtil.GetString(data, "checkedUtc", string.Empty), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out checkedUtc);
                UpdateRelease release = new UpdateRelease
                {
                    Version = version,
                    VersionText = FormatVersion(version),
                    TagName = JsonUtil.GetString(data, "tagName", string.Empty),
                    AssetName = JsonUtil.GetString(data, "assetName", string.Empty),
                    DownloadUri = download,
                    ReleasePageUri = page,
                    Sha256 = digest,
                    Size = size,
                    ReleaseNotes = LimitText(JsonUtil.GetString(data, "releaseNotes", string.Empty), 12000)
                };
                Version installed;
                TryParseVersion(ReleaseInfo.SemanticVersion, out installed);
                bool newer = installed != null && release.Version.CompareTo(installed) > 0;
                return new UpdateCheckResult
                {
                    UpdateAvailable = newer,
                    Release = release,
                    CheckedUtc = checkedUtc,
                    FromCache = true,
                    Message = newer ? "Control Center " + release.VersionText + " was last seen on GitHub."
                        : "The cached release information shows this version is current."
                };
            }
            catch { return null; }
        }

        public Task<string> DownloadAsync(UpdateRelease release, Action<int> reportProgress)
        {
            if (release == null)
                throw new ArgumentNullException("release");
            return Task.Run(() => DownloadRelease(release, reportProgress));
        }

        public static UpdateCheckResult ParseLatestRelease(string json, string currentVersion)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidDataException("GitHub returned an empty release response.");
            if (Encoding.UTF8.GetByteCount(json) > MaximumMetadataBytes)
                throw new InvalidDataException("GitHub release metadata exceeded the 2 MiB safety limit.");

            Version installed;
            if (!TryParseVersion(currentVersion, out installed))
                throw new InvalidDataException("The installed Control Center version is invalid: " + currentVersion);

            Dictionary<string, object> root = JsonUtil.AsObject(JsonUtil.CreateSerializer().DeserializeObject(json));
            if (root == null)
                throw new InvalidDataException("GitHub returned invalid release metadata.");
            if (JsonUtil.GetBool(root, "draft", true) || JsonUtil.GetBool(root, "prerelease", true))
                throw new InvalidDataException("GitHub's latest release is not a stable published release.");

            string tag = JsonUtil.GetString(root, "tag_name", string.Empty).Trim();
            Version available;
            if (!TryParseVersion(tag, out available))
                throw new InvalidDataException("The latest GitHub release has an unsupported version tag: " + tag);

            Dictionary<string, object> chosen = null;
            object assetsValue;
            if (root.TryGetValue("assets", out assetsValue))
            {
                foreach (object item in JsonUtil.AsItems(assetsValue))
                {
                    Dictionary<string, object> asset = JsonUtil.AsObject(item);
                    string name = JsonUtil.GetString(asset, "name", string.Empty);
                    if (IsAcceptedAssetName(name))
                    {
                        chosen = asset;
                        break;
                    }
                }
            }
            if (chosen == null)
                throw new InvalidDataException("The latest release does not contain the supported Control Center ZIP asset.");

            string download = JsonUtil.GetString(chosen, "browser_download_url", string.Empty);
            Uri downloadUri;
            if (!Uri.TryCreate(download, UriKind.Absolute, out downloadUri) || !IsTrustedReleaseUri(downloadUri))
                throw new InvalidDataException("The release asset URL is outside the trusted GitHub repository.");

            string digest = JsonUtil.GetString(chosen, "digest", string.Empty).Trim();
            if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The release asset does not provide a SHA-256 digest.");
            string sha256 = digest.Substring(7).ToLowerInvariant();
            if (!Regex.IsMatch(sha256, "\\A[0-9a-f]{64}\\z", RegexOptions.CultureInvariant))
                throw new InvalidDataException("The release asset SHA-256 digest is invalid.");

            long size = JsonUtil.GetLong(chosen, "size", -1);
            if (size <= 0 || size > MaximumAssetBytes)
                throw new InvalidDataException("The release asset size is outside the supported safety limit.");

            string page = JsonUtil.GetString(root, "html_url", string.Empty);
            Uri pageUri;
            if (!Uri.TryCreate(page, UriKind.Absolute, out pageUri) || !IsTrustedRepositoryPage(pageUri))
                pageUri = new Uri("https://github.com/Enterlessguy/Schedule-I-game-modifier-mod/releases");

            UpdateRelease release = new UpdateRelease
            {
                Version = available,
                VersionText = FormatVersion(available),
                TagName = tag,
                AssetName = JsonUtil.GetString(chosen, "name", string.Empty),
                DownloadUri = downloadUri,
                ReleasePageUri = pageUri,
                Sha256 = sha256,
                Size = size,
                ReleaseNotes = LimitText(JsonUtil.GetString(root, "body", string.Empty), 12000)
            };
            bool newer = available.CompareTo(installed) > 0;
            return new UpdateCheckResult
            {
                UpdateAvailable = newer,
                Release = release,
                CheckedUtc = DateTime.UtcNow,
                Message = newer
                    ? "Control Center " + release.VersionText + " is available."
                    : "Control Center " + FormatVersion(installed) + " is current."
            };
        }

        public static bool TryParseVersion(string value, out Version version)
        {
            version = null;
            string text = (value ?? string.Empty).Trim();
            if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(1);
            if (!Regex.IsMatch(text, "\\A[0-9]+(?:\\.[0-9]+){1,3}\\z", RegexOptions.CultureInvariant))
                return false;
            string[] parts = text.Split('.');
            int[] numbers = new int[4];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]) || numbers[i] < 0)
                    return false;
            }
            version = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
            return true;
        }

        public static string ComputeSha256(string path)
        {
            using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", string.Empty).ToLowerInvariant();
        }

        public static bool StartInstaller(UpdateRelease release, string archivePath, string gameRoot, out string error)
        {
            error = null;
            try
            {
                if (release == null || !File.Exists(archivePath))
                    throw new InvalidOperationException("The verified update package is unavailable.");
                string updaterDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ScheduleI-ControlCenter", "Updates", "updater-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(updaterDirectory);
                string updaterPath = Path.Combine(updaterDirectory, "ScheduleIControlCenter.Updater.exe");
                string metadataPath = Path.Combine(updaterDirectory, "release.json");
                string currentExecutable = Process.GetCurrentProcess().MainModule.FileName;
                File.Copy(currentExecutable, updaterPath, false);
                JsonUtil.WriteObjectAtomic(metadataPath, new Dictionary<string, object>
                {
                    { "version", release.VersionText },
                    { "tagName", release.TagName ?? string.Empty },
                    { "releaseNotes", release.ReleaseNotes ?? string.Empty },
                    { "releasePageUrl", release.ReleasePageUri == null ? string.Empty : release.ReleasePageUri.AbsoluteUri }
                });
                ProcessStartInfo start = new ProcessStartInfo
                {
                    FileName = updaterPath,
                    Arguments = string.Join(" ", new[]
                    {
                        "--apply-update",
                        Quote(archivePath),
                        "--target", Quote(gameRoot),
                        "--sha256", Quote(release.Sha256),
                        "--version", Quote(release.VersionText),
                        "--metadata", Quote(metadataPath),
                        "--wait-pid", Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture)
                    }),
                    WorkingDirectory = updaterDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process process = Process.Start(start);
                if (process == null)
                    throw new InvalidOperationException("Windows did not start the update installer.");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static string DownloadRelease(UpdateRelease release, Action<int> reportProgress)
        {
            string updateRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ScheduleI-ControlCenter", "Updates");
            Directory.CreateDirectory(updateRoot);
            string safeTag = Regex.Replace(release.TagName ?? release.VersionText, "[^A-Za-z0-9._-]", "-");
            string destination = Path.Combine(updateRoot, "ScheduleI-Control-Center-" + safeTag + ".zip");
            if (File.Exists(destination) && string.Equals(ComputeSha256(destination), release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                if (reportProgress != null) reportProgress(100);
                return destination;
            }

            string partial = destination + ".partial-" + Guid.NewGuid().ToString("N");
            try
            {
                HttpWebRequest request = CreateRequest(release.DownloadUri, "application/octet-stream");
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode != HttpStatusCode.OK)
                        throw new WebException("GitHub returned HTTP " + (int)response.StatusCode + ".");
                    long declared = response.ContentLength;
                    if (declared > MaximumAssetBytes || (declared > 0 && declared != release.Size))
                        throw new InvalidDataException("The downloaded asset size does not match GitHub's release metadata.");
                    long total = 0;
                    byte[] buffer = new byte[128 * 1024];
                    using (Stream input = response.GetResponseStream())
                    using (FileStream output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            total += read;
                            if (total > MaximumAssetBytes || total > release.Size)
                                throw new InvalidDataException("The downloaded update exceeded its declared size.");
                            output.Write(buffer, 0, read);
                            if (reportProgress != null)
                                reportProgress((int)Math.Min(99L, total * 100L / release.Size));
                        }
                        output.Flush(true);
                    }
                    if (total != release.Size)
                        throw new InvalidDataException("The downloaded update is incomplete.");
                }
                string actual = ComputeSha256(partial);
                if (!string.Equals(actual, release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The downloaded update failed SHA-256 verification.");
                if (File.Exists(destination))
                    File.Delete(destination);
                File.Move(partial, destination);
                if (reportProgress != null) reportProgress(100);
                return destination;
            }
            finally
            {
                if (File.Exists(partial))
                    File.Delete(partial);
            }
        }

        private static void SaveCachedResult(UpdateCheckResult result)
        {
            if (result == null || result.Release == null) return;
            string path = CachedMetadataPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            UpdateRelease release = result.Release;
            JsonUtil.WriteObjectAtomic(path, new Dictionary<string, object>
            {
                { "checkedUtc", result.CheckedUtc.ToString("o", CultureInfo.InvariantCulture) },
                { "version", release.VersionText },
                { "tagName", release.TagName ?? string.Empty },
                { "assetName", release.AssetName ?? string.Empty },
                { "downloadUrl", release.DownloadUri == null ? string.Empty : release.DownloadUri.AbsoluteUri },
                { "releasePageUrl", release.ReleasePageUri == null ? string.Empty : release.ReleasePageUri.AbsoluteUri },
                { "sha256", release.Sha256 ?? string.Empty },
                { "size", release.Size },
                { "releaseNotes", release.ReleaseNotes ?? string.Empty }
            });
        }

        private static string CachedMetadataPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ScheduleI-ControlCenter", "Updates", "latest-release.json");
        }

        private static string DownloadText(Uri uri, int maximumBytes)
        {
            HttpWebRequest request = CreateRequest(uri, "application/vnd.github+json");
            request.Headers["X-GitHub-Api-Version"] = "2022-11-28";
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new WebException("GitHub returned HTTP " + (int)response.StatusCode + ".");
                using (Stream input = response.GetResponseStream())
                using (MemoryStream output = new MemoryStream())
                {
                    byte[] buffer = new byte[16 * 1024];
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (output.Length + read > maximumBytes)
                            throw new InvalidDataException("GitHub release metadata exceeded the safety limit.");
                        output.Write(buffer, 0, read);
                    }
                    return new UTF8Encoding(false, true).GetString(output.ToArray());
                }
            }
        }

        private static HttpWebRequest CreateRequest(Uri uri, string accept)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = "GET";
            request.UserAgent = "ScheduleI-ControlCenter/" + ReleaseInfo.SemanticVersion;
            request.Accept = accept;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.AllowAutoRedirect = true;
            request.Timeout = 20000;
            request.ReadWriteTimeout = 30000;
            return request;
        }

        private static bool IsTrustedReleaseUri(Uri uri)
        {
            return uri != null
                && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                && uri.AbsolutePath.StartsWith("/Enterlessguy/Schedule-I-game-modifier-mod/releases/download/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAcceptedAssetName(string name)
        {
            return Regex.IsMatch(name ?? string.Empty,
                "\\AScheduleI-Control-Center(?:-V[0-9]+(?:\\.[0-9]+)*)?\\.zip\\z",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static bool IsTrustedRepositoryPage(Uri uri)
        {
            return uri != null
                && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                && uri.AbsolutePath.StartsWith("/Enterlessguy/Schedule-I-game-modifier-mod/", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatVersion(Version version)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}", version.Major, version.Minor, version.Build);
        }

        private static string LimitText(string value, int limit)
        {
            string text = value ?? string.Empty;
            return text.Length <= limit ? text : text.Substring(0, limit) + Environment.NewLine + "[release notes truncated]";
        }

        internal static string LimitForRecord(string value, int limit)
        {
            return LimitText(value, limit);
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }
    }

    internal static class UpdateInstaller
    {
        private const int MaximumEntries = 4096;
        private const long MaximumUncompressedBytes = 1024L * 1024L * 1024L;
        private static readonly HashSet<string> RequiredFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ScheduleIControlCenter.exe",
            "ScheduleI-ControlCenter/dist/ScheduleIControlCenter.exe",
            "ScheduleI-ControlCenter/dist/ScheduleIControlCenter.Cli.exe",
            "Mods/ScheduleIControlBridge.dll",
            "version.dll",
            "UserData/Loader.cfg"
        };

        public static bool IsApplyMode(string[] args)
        {
            return args != null && args.Any(arg => string.Equals(arg, "--apply-update", StringComparison.OrdinalIgnoreCase));
        }

        public static int Run(string[] args)
        {
            string archive = ReadArgument(args, "--apply-update");
            string target = ReadArgument(args, "--target");
            string sha256 = ReadArgument(args, "--sha256");
            string version = ReadArgument(args, "--version");
            string metadata = ReadArgument(args, "--metadata");
            int waitPid;
            int.TryParse(ReadArgument(args, "--wait-pid"), NumberStyles.None, CultureInfo.InvariantCulture, out waitPid);
            UpdateInstallResult result = null;
            try
            {
                if (waitPid > 0)
                    WaitForExit(waitPid, TimeSpan.FromMinutes(2));
                if (Process.GetProcessesByName("Schedule I").Length > 0)
                    throw new InvalidOperationException("Schedule I is still running. The update was not applied.");
                result = ApplyArchive(archive, target, sha256, version);
            }
            catch (Exception ex)
            {
                result = new UpdateInstallResult { Success = false, Message = ex.GetType().Name + ": " + ex.Message };
            }

            WriteResult(target, result, metadata, version);
            TryStartControlCenter(target);
            return result != null && result.Success ? 0 : 1;
        }

        internal static UpdateInstallResult ApplyArchive(string archivePath, string gameRoot, string expectedSha256, string expectedVersion)
        {
            if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
                throw new FileNotFoundException("The downloaded update package was not found.", archivePath);
            string root = Path.GetFullPath(gameRoot ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (root.Length < 4 || !File.Exists(Path.Combine(root, "Schedule I.exe")))
                throw new InvalidDataException("The update target is not a Schedule I installation.");
            if (!Regex.IsMatch(expectedSha256 ?? string.Empty, "\\A[0-9a-fA-F]{64}\\z", RegexOptions.CultureInvariant))
                throw new InvalidDataException("The expected update SHA-256 is invalid.");
            if (!string.Equals(UpdateService.ComputeSha256(archivePath), expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update package changed after download verification.");
            Version parsedVersion;
            if (!UpdateService.TryParseVersion(expectedVersion, out parsedVersion))
                throw new InvalidDataException("The expected update version is invalid.");

            string updatesRoot = Path.Combine(root, "ScheduleI-ControlCenter", "Updates");
            Directory.CreateDirectory(updatesRoot);
            string transactionId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string stage = Path.Combine(Path.GetTempPath(), "SICC-U", "s-" + transactionId.Substring(transactionId.Length - 8));
            string backup = Path.Combine(updatesRoot, "backup-" + transactionId);
            Directory.CreateDirectory(stage);
            Directory.CreateDirectory(backup);
            List<AppliedFile> applied = new List<AppliedFile>();
            try
            {
                ExtractValidated(archivePath, stage);
                ValidateRequiredFiles(stage, expectedVersion);
                string[] stagedSources = Directory.GetFiles(stage, "*", SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
                List<string> managedFiles = new List<string>();
                foreach (string source in stagedSources)
                {
                    string relative = source.Substring(stage.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    managedFiles.Add(relative.Replace(Path.DirectorySeparatorChar, '/'));
                    string destination = SafeCombine(root, relative);
                    string backupPath = BackupPath(backup, relative);
                    bool existed = File.Exists(destination);
                    if (existed)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(backupPath));
                        File.Copy(destination, backupPath, false);
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    string pending = Path.Combine(Path.GetDirectoryName(destination), ".sicc-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
                    File.Copy(source, pending, false);
                    if (existed)
                        File.Replace(pending, destination, null, true);
                    else
                        File.Move(pending, destination);
                    applied.Add(new AppliedFile { Destination = destination, Backup = backupPath, Existed = existed });
                }

                HashSet<string> incoming = new HashSet<string>(managedFiles, StringComparer.OrdinalIgnoreCase);
                foreach (string previous in ReadManagedFiles(updatesRoot))
                {
                    string normalized = (previous ?? string.Empty).Replace('\\', '/').TrimStart('/');
                    if (normalized.Length == 0 || incoming.Contains(normalized) || !IsAllowedPackagePath(normalized))
                        continue;
                    string destination = SafeCombine(root, normalized.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(destination)) continue;
                    string backupPath = BackupPath(backup, normalized);
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath));
                    File.Copy(destination, backupPath, false);
                    File.Delete(destination);
                    applied.Add(new AppliedFile { Destination = destination, Backup = backupPath, Existed = true });
                }
                WriteBackupManifest(backup, root, applied, expectedVersion);
                WriteManagedFiles(updatesRoot, managedFiles, expectedVersion);
                return new UpdateInstallResult
                {
                    Success = true,
                    Message = "Control Center " + expectedVersion + " was installed successfully.",
                    BackupDirectory = backup
                };
            }
            catch
            {
                RollBack(applied);
                throw;
            }
            finally
            {
                TryDeleteDirectory(stage);
            }
        }

        private static void ExtractValidated(string archivePath, string stage)
        {
            long declaredTotal = 0;
            long extractedTotal = 0;
            int count = 0;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (ZipArchive archive = ZipFile.OpenRead(archivePath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string normalized = (entry.FullName ?? string.Empty).Replace('\\', '/').TrimStart('/');
                    if (normalized.Length == 0 || normalized.EndsWith("/", StringComparison.Ordinal))
                        continue;
                    count++;
                    if (count > MaximumEntries)
                        throw new InvalidDataException("The update package contains too many files.");
                    if (!IsAllowedPackagePath(normalized))
                        throw new InvalidDataException("The update package contains a disallowed path: " + normalized);
                    int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
                    if (unixType == 0xA000)
                        throw new InvalidDataException("Symbolic links are not allowed in update packages.");
                    declaredTotal += entry.Length;
                    if (entry.Length < 0 || declaredTotal > MaximumUncompressedBytes)
                        throw new InvalidDataException("The expanded update exceeds the 1 GiB safety limit.");
                    string destination = SafeCombine(stage, normalized.Replace('/', Path.DirectorySeparatorChar));
                    if (!seen.Add(destination))
                        throw new InvalidDataException("The update package contains a duplicate path: " + normalized);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    using (Stream input = entry.Open())
                    using (FileStream output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        long entryBytes = 0;
                        byte[] buffer = new byte[128 * 1024];
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            entryBytes += read;
                            extractedTotal += read;
                            if (entryBytes > entry.Length || extractedTotal > MaximumUncompressedBytes)
                                throw new InvalidDataException("The expanded update exceeded its declared size.");
                            output.Write(buffer, 0, read);
                        }
                        if (entryBytes != entry.Length)
                            throw new InvalidDataException("An update entry did not match its declared size: " + normalized);
                        output.Flush(true);
                    }
                }
            }
        }

        private static bool IsAllowedPackagePath(string path)
        {
            string[] segments = path.Split('/');
            if (segments.Any(segment => segment.Length == 0 || segment == "." || segment == ".." || segment.IndexOf(':') >= 0))
                return false;
            string first = segments[0];
            if (segments.Length == 1)
            {
                if (Regex.IsMatch(first, "\\ARELEASE_NOTES_V[0-9]+(?:[._][0-9]+)*\\.md\\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    return true;
                return new[]
                {
                    "ScheduleIControlCenter.exe", "version.dll", "README.md", "RELEASE_NOTES.md", "CHECKSUMS-SHA256.txt",
                    "LICENSE", "PRIVACY.md", "SECURITY.md", "SIGNING_POLICY.md", "THIRD_PARTY_NOTICES.md"
                }.Any(name => string.Equals(name, first, StringComparison.OrdinalIgnoreCase));
            }
            if (string.Equals(first, "MelonLoader", StringComparison.OrdinalIgnoreCase)
                || string.Equals(first, "Mods", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(first, "UserData", StringComparison.OrdinalIgnoreCase))
                return segments.Length == 2 && string.Equals(segments[1], "Loader.cfg", StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(first, "ScheduleI-ControlCenter", StringComparison.OrdinalIgnoreCase))
                return false;
            string second = segments.Length > 1 ? segments[1] : string.Empty;
            return !new[] { "Backups", "InstallRecords", "Updates", ".tmp-inspect", "bin", "obj" }
                .Any(name => string.Equals(name, second, StringComparison.OrdinalIgnoreCase));
        }

        private static void ValidateRequiredFiles(string stage, string expectedVersion)
        {
            foreach (string relative in RequiredFiles)
                if (!File.Exists(SafeCombine(stage, relative.Replace('/', Path.DirectorySeparatorChar))))
                    throw new InvalidDataException("The update package is missing: " + relative);
            foreach (string relative in new[]
            {
                "ScheduleIControlCenter.exe",
                "ScheduleI-ControlCenter/dist/ScheduleIControlCenter.exe",
                "ScheduleI-ControlCenter/dist/ScheduleIControlCenter.Cli.exe"
            })
            {
                string path = SafeCombine(stage, relative.Replace('/', Path.DirectorySeparatorChar));
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                if (!string.Equals(info.CompanyName, "Intelligence Database", StringComparison.Ordinal)
                    || !FileVersionMatches(info.FileVersion, expectedVersion))
                    throw new InvalidDataException("The update package executable metadata does not match " + expectedVersion + ": " + relative);
            }
        }

        private static IEnumerable<string> ReadManagedFiles(string updatesRoot)
        {
            string path = Path.Combine(updatesRoot, "managed-files.json");
            if (!File.Exists(path)) yield break;
            Dictionary<string, object> data;
            try { data = JsonUtil.ReadObject(path); }
            catch { yield break; }
            object files;
            if (!data.TryGetValue("files", out files)) yield break;
            foreach (object value in JsonUtil.AsItems(files))
            {
                string item = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(item)) yield return item;
            }
        }

        private static void WriteManagedFiles(string updatesRoot, IList<string> files, string version)
        {
            JsonUtil.WriteObjectAtomic(Path.Combine(updatesRoot, "managed-files.json"), new Dictionary<string, object>
            {
                { "version", version ?? string.Empty },
                { "writtenUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) },
                { "files", files.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray() }
            });
        }

        private static string BackupPath(string backupRoot, string relative)
        {
            byte[] bytes = Encoding.UTF8.GetBytes((relative ?? string.Empty).Replace('\\', '/').ToLowerInvariant());
            string name;
            using (SHA256 hash = SHA256.Create())
                name = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant() + ".bak";
            return Path.Combine(backupRoot, name);
        }

        private static void WriteBackupManifest(string backupRoot, string gameRoot, IList<AppliedFile> files, string version)
        {
            List<object> entries = new List<object>();
            string rootPrefix = Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            foreach (AppliedFile file in files)
            {
                string relative = file.Destination.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
                    ? file.Destination.Substring(rootPrefix.Length).Replace(Path.DirectorySeparatorChar, '/')
                    : string.Empty;
                entries.Add(new Dictionary<string, object>
                {
                    { "path", relative },
                    { "backupFile", file.Existed ? Path.GetFileName(file.Backup) : string.Empty },
                    { "previouslyExisted", file.Existed }
                });
            }
            JsonUtil.WriteObjectAtomic(Path.Combine(backupRoot, "backup-manifest.json"), new Dictionary<string, object>
            {
                { "version", version ?? string.Empty },
                { "createdUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) },
                { "files", entries.ToArray() }
            });
        }

        private static bool FileVersionMatches(string fileVersion, string expectedVersion)
        {
            Version file;
            Version expected;
            return UpdateService.TryParseVersion(fileVersion, out file)
                && UpdateService.TryParseVersion(expectedVersion, out expected)
                && file.Equals(expected);
        }

        private static string SafeCombine(string root, string relative)
        {
            string canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string combined = Path.GetFullPath(Path.Combine(canonicalRoot, relative ?? string.Empty));
            string prefix = canonicalRoot + Path.DirectorySeparatorChar;
            if (!combined.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("An update path escaped its target directory.");
            return combined;
        }

        private static void RollBack(List<AppliedFile> applied)
        {
            for (int i = applied.Count - 1; i >= 0; i--)
            {
                AppliedFile file = applied[i];
                try
                {
                    if (file.Existed && File.Exists(file.Backup))
                    {
                        string pending = Path.Combine(Path.GetDirectoryName(file.Destination),
                            ".sicc-rollback-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
                        File.Copy(file.Backup, pending, false);
                        if (File.Exists(file.Destination))
                            File.Replace(pending, file.Destination, null, true);
                        else
                            File.Move(pending, file.Destination);
                    }
                    else if (!file.Existed && File.Exists(file.Destination))
                        File.Delete(file.Destination);
                }
                catch { }
            }
        }

        private static string ReadArgument(string[] args, string name)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        private static void WaitForExit(int processId, TimeSpan timeout)
        {
            try
            {
                using (Process process = Process.GetProcessById(processId))
                    if (!process.WaitForExit((int)timeout.TotalMilliseconds))
                        throw new TimeoutException("The running Control Center did not close in time.");
            }
            catch (ArgumentException) { }
        }

        private static void WriteResult(string gameRoot, UpdateInstallResult result, string metadataPath, string version)
        {
            try
            {
                string root = Path.GetFullPath(gameRoot ?? string.Empty);
                string path = Path.Combine(root, "ScheduleI-ControlCenter", "Updates", "last-update.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string notes = string.Empty;
                string releasePage = string.Empty;
                if (!string.IsNullOrEmpty(metadataPath) && File.Exists(metadataPath))
                {
                    Dictionary<string, object> metadata = JsonUtil.ReadObject(metadataPath);
                    notes = JsonUtil.GetString(metadata, "releaseNotes", string.Empty);
                    releasePage = JsonUtil.GetString(metadata, "releasePageUrl", string.Empty);
                }
                JsonUtil.WriteObjectAtomic(path, new Dictionary<string, object>
                {
                    { "timestampUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) },
                    { "success", result != null && result.Success },
                    { "message", result == null ? "The update installer did not return a result." : result.Message },
                    { "backupDirectory", result == null ? string.Empty : DiagnosticsService.SafePath(result.BackupDirectory) },
                    { "version", version ?? string.Empty },
                    { "releaseNotes", UpdateService.LimitForRecord(notes, 12000) },
                    { "releasePageUrl", releasePage }
                });
            }
            catch { }
        }

        private static void TryStartControlCenter(string gameRoot)
        {
            try
            {
                string executable = Path.Combine(Path.GetFullPath(gameRoot), "ScheduleI-ControlCenter", "dist", "ScheduleIControlCenter.exe");
                if (File.Exists(executable))
                    Process.Start(new ProcessStartInfo { FileName = executable, WorkingDirectory = Path.GetDirectoryName(executable), UseShellExecute = true });
            }
            catch { }
        }

        private static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch { }
        }

        private sealed class AppliedFile
        {
            public string Destination { get; set; }
            public string Backup { get; set; }
            public bool Existed { get; set; }
        }
    }
}
