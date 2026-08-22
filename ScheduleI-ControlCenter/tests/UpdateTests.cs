using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace ScheduleIControlCenter
{
    internal static class UpdateTests
    {
        public static void RunAll()
        {
            ReleaseMetadataValidation();
            TransactionalPackageSync();
            UnsafeArchiveRejection();
            Console.WriteLine("PASS: update metadata, SHA-256, complete package sync, managed-file removal, and unsafe archive rejection.");
        }

        public static void VerifyReleasePackage(string archivePath)
        {
            Require(File.Exists(archivePath), "The release ZIP does not exist: " + archivePath);
            string root = NewRoot("release-package");
            try
            {
                UpdateInstallResult result = UpdateInstaller.ApplyArchive(archivePath, root,
                    UpdateService.ComputeSha256(archivePath), ReleaseInfo.SemanticVersion);
                Require(result.Success, "The complete release ZIP did not install successfully.");
                Require(File.Exists(Path.Combine(root, "ScheduleI-ControlCenter", "dist", "ScheduleIControlCenter.exe")),
                    "The installed release did not contain the graphical application.");
            }
            finally { TryDelete(root); }
        }

        private static void ReleaseMetadataValidation()
        {
            string digest = new string('a', 64);
            string json = "{\"draft\":false,\"prerelease\":false,\"tag_name\":\"v2.0.1\","
                + "\"html_url\":\"https://github.com/Enterlessguy/Schedule-I-game-modifier-mod/releases/tag/v2.0.1\","
                + "\"body\":\"Update notes\",\"assets\":[{\"name\":\"ScheduleI-Control-Center-V2.zip\","
                + "\"browser_download_url\":\"https://github.com/Enterlessguy/Schedule-I-game-modifier-mod/releases/download/v2.0.1/ScheduleI-Control-Center-V2.zip\","
                + "\"digest\":\"sha256:" + digest + "\",\"size\":1234}]}";
            UpdateCheckResult newer = UpdateService.ParseLatestRelease(json, "2.0.0");
            Require(newer.UpdateAvailable && newer.Release.VersionText == "2.0.1", "A newer stable release was not detected.");
            Require(!UpdateService.ParseLatestRelease(json, "2.0.1").UpdateAvailable, "The installed release was incorrectly treated as outdated.");

            string unsafeJson = json.Replace("https://github.com/Enterlessguy/Schedule-I-game-modifier-mod/releases/download/", "https://example.invalid/");
            RequireThrows<InvalidDataException>(() => UpdateService.ParseLatestRelease(unsafeJson, "2.0.0"), "An untrusted asset URL was accepted.");
        }

        private static void TransactionalPackageSync()
        {
            string root = NewRoot("sync");
            try
            {
                string first = Path.Combine(root, "first.zip");
                BuildPackage(first, true);
                UpdateInstallResult installed = UpdateInstaller.ApplyArchive(first, root, UpdateService.ComputeSha256(first), ReleaseInfo.SemanticVersion);
                Require(installed.Success, "The valid update package was rejected.");
                string obsolete = Path.Combine(root, "ScheduleI-ControlCenter", "obsolete.txt");
                Require(File.Exists(obsolete), "The first package did not install its managed file.");

                string second = Path.Combine(root, "second.zip");
                BuildPackage(second, false);
                UpdateInstallResult updated = UpdateInstaller.ApplyArchive(second, root, UpdateService.ComputeSha256(second), ReleaseInfo.SemanticVersion);
                Require(updated.Success && !File.Exists(obsolete), "A file removed from the next complete package was not removed from the installation.");
                Require(File.Exists(Path.Combine(root, "ScheduleI-ControlCenter", "Updates", "managed-files.json")), "The managed-file manifest was not written.");
            }
            finally { TryDelete(root); }
        }

        private static void UnsafeArchiveRejection()
        {
            string root = NewRoot("unsafe");
            try
            {
                string archivePath = Path.Combine(root, "unsafe.zip");
                using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
                {
                    ZipArchiveEntry entry = archive.CreateEntry("../escape.txt");
                    using (StreamWriter writer = new StreamWriter(entry.Open(), new UTF8Encoding(false))) writer.Write("blocked");
                }
                RequireThrows<InvalidDataException>(() => UpdateInstaller.ApplyArchive(archivePath, root, UpdateService.ComputeSha256(archivePath), ReleaseInfo.SemanticVersion), "A traversal archive was accepted.");
                Require(!File.Exists(Path.Combine(Directory.GetParent(root).FullName, "escape.txt")), "A traversal entry escaped the staging directory.");
            }
            finally { TryDelete(root); }
        }

        private static string NewRoot(string name)
        {
            string root = Path.Combine(Path.GetTempPath(), "ScheduleI-Updater-" + name + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "Schedule I.exe"), "test");
            return root;
        }

        private static void BuildPackage(string path, bool includeObsolete)
        {
            string dist = Path.Combine(Environment.CurrentDirectory, "dist");
            string gui = Path.Combine(dist, "ScheduleIControlCenter.exe");
            string cli = Path.Combine(dist, "ScheduleIControlCenter.Cli.exe");
            Require(File.Exists(gui) && File.Exists(cli), "Build the GUI and CLI before running updater tests.");
            using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                AddFile(archive, gui, "ScheduleIControlCenter.exe");
                AddFile(archive, gui, "ScheduleI-ControlCenter/dist/ScheduleIControlCenter.exe");
                AddFile(archive, cli, "ScheduleI-ControlCenter/dist/ScheduleIControlCenter.Cli.exe");
                AddText(archive, "Mods/ScheduleIControlBridge.dll", "bridge");
                AddText(archive, "version.dll", "loader");
                AddText(archive, "UserData/Loader.cfg", "cfg");
                AddText(archive, "ScheduleI-ControlCenter/current.txt", "current");
                if (includeObsolete) AddText(archive, "ScheduleI-ControlCenter/obsolete.txt", "obsolete");
            }
        }

        private static void AddFile(ZipArchive archive, string source, string name)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Fastest);
            using (Stream input = File.OpenRead(source)) using (Stream output = entry.Open()) input.CopyTo(output);
        }

        private static void AddText(ZipArchive archive, string name, string value)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Fastest);
            using (StreamWriter writer = new StreamWriter(entry.Open(), new UTF8Encoding(false))) writer.Write(value);
        }

        private static void RequireThrows<T>(Action action, string message) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException(message);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void TryDelete(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch { }
        }
    }
}
