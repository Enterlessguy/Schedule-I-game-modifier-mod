// Schedule I Control Center - attach & launch
// Single-file launcher: locates the game (default path first, then a bounded
// system-wide search), optionally installs the runtime package into the game
// folder after user confirmation, then starts the Control Center.
//
// Design constraints for this file:
//  - No elevation, no persistence, no network, no deletion of user files.
//  - Writes only into the selected game folder (plus a small attach record
//    in that folder's ScheduleI-ControlCenter\InstallRecords directory).
//  - No hardcoded user-specific paths or identifiers.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ScheduleIControlCenter.Launcher
{
    internal static class Program
    {
        public const string GameExe = "Schedule I.exe";
        public const string ControlCenterRelativeExe = @"ScheduleI-ControlCenter\dist\ScheduleIControlCenter.exe";

        [STAThread]
        private static int Main(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                return RunCli(args);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }

        private static int RunCli(string[] args)
        {
            string logPath = null;
            var rest = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if ((string.Equals(args[i], "--log", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(args[i], "-log", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
                {
                    logPath = args[++i];
                }
                else
                {
                    rest.Add(args[i]);
                }
            }

            var sb = new StringBuilder();
            int exit = 0;
            try
            {
                string command = rest.Count > 0 ? rest[0] : "--help";
                switch (command)
                {
                    case "--locate":
                        string found = GameLocator.FindGame();
                        sb.AppendLine(found ?? "NOT_FOUND");
                        exit = found != null ? 0 : 1;
                        break;
                    case "--check":
                        exit = Attacher.CheckState(rest.Count > 1 ? rest[1] : null, sb);
                        break;
                    case "--install":
                        exit = Attacher.AttachCli(rest.Count > 1 ? rest[1] : null, sb);
                        break;
                    case "--help":
                    case "-h":
                        sb.AppendLine("Schedule I Control Center Launcher");
                        sb.AppendLine("Usage: ScheduleIControlCenter.exe [command] [--log <file>]");
                        sb.AppendLine("Commands:");
                        sb.AppendLine("  --locate              print the located game folder or NOT_FOUND");
                        sb.AppendLine("  --check <dir>         print attach state for a game folder");
                        sb.AppendLine("  --install <dir>       attach runtime files to a game folder");
                        break;
                    default:
                        sb.AppendLine("Unknown command: " + command);
                        exit = 2;
                        break;
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("ERROR: " + ex.Message);
                exit = 3;
            }

            if (!string.IsNullOrEmpty(logPath))
            {
                try
                {
                    File.WriteAllText(logPath, sb.ToString());
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Cannot write log: " + ex.Message);
                }
            }
            return exit;
        }
    }

    internal static class GameLocator
    {
        private static readonly string[] DefaultCandidates =
        {
            @"C:\Program Files (x86)\Steam\steamapps\common\Schedule I",
            @"C:\Program Files\Steam\steamapps\common\Schedule I"
        };

        private static readonly string[] SkipDirectoryNames =
        {
            "windows", "users", "$recycle.bin", "system volume information", "recovery",
            "perflogs", "programdata", "appdata", "onedrive", "node_modules", ".git",
            "program files", "program files (x86)", "temp", "tmp", "deliveryoptimization",
            "windows.old"
        };

        public static bool IsValidGameDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            try
            {
                return File.Exists(Path.Combine(directory, Program.GameExe));
            }
            catch
            {
                return false;
            }
        }

        public static string FindGame()
        {
            // 1) Fixed default Steam locations.
            foreach (string candidate in DefaultCandidates)
            {
                if (IsValidGameDirectory(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            // 2) Steam install root(s) from the registry.
            var steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddSteamRootsFromRegistry(steamRoots);

            // 3) Steam library folders listed in libraryfolders.vdf.
            var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in steamRoots)
            {
                libraries.Add(root);
                string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    AddLibraryPaths(vdf, libraries);
                }
            }

            foreach (string library in libraries)
            {
                string candidate = Path.Combine(library, "steamapps", "common", "Schedule I");
                if (IsValidGameDirectory(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            // 4) Bounded system-wide search as a fallback.
            return SearchSystemWide();
        }

        private static void AddSteamRootsFromRegistry(HashSet<string> roots)
        {
            string[] keyNames =
            {
                @"SOFTWARE\WOW6432Node\Valve\Steam",
                @"SOFTWARE\Valve\Steam"
            };

            foreach (string keyName in keyNames)
            {
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(keyName))
                    {
                        string value = key != null ? key.GetValue("InstallPath") as string : null;
                        if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
                        {
                            roots.Add(value);
                        }
                    }
                }
                catch
                {
                }
            }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    string value = key != null ? key.GetValue("InstallPath") as string : null;
                    if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
                    {
                        roots.Add(value);
                    }
                }
            }
            catch
            {
            }
        }

        private static void AddLibraryPaths(string vdfPath, HashSet<string> libraries)
        {
            try
            {
                string text = File.ReadAllText(vdfPath);
                foreach (Match match in Regex.Matches(text, "\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
                {
                    // Steam escapes backslashes in .vdf paths.
                    string path = match.Groups[1].Value.Replace("\\\\", "\\");
                    if (Directory.Exists(path))
                    {
                        libraries.Add(path);
                    }
                }
            }
            catch
            {
            }
        }

        private static string SearchSystemWide()
        {
            var stopwatch = Stopwatch.StartNew();
            int visited = 0;
            const int MaxVisited = 30000;
            const long TimeoutMs = 30000;

            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                {
                    continue;
                }

                string root;
                try
                {
                    root = drive.RootDirectory.FullName;
                }
                catch
                {
                    continue;
                }

                string[] knownLayouts =
                {
                    @"Steam\steamapps\common\Schedule I",
                    @"SteamLibrary\steamapps\common\Schedule I",
                    @"steamapps\common\Schedule I"
                };
                foreach (string relative in knownLayouts)
                {
                    string candidate = Path.Combine(root, relative);
                    if (IsValidGameDirectory(candidate))
                    {
                        return candidate;
                    }
                }

                string hit = Walk(root, 0, ref visited, MaxVisited, stopwatch, TimeoutMs);
                if (hit != null)
                {
                    return hit;
                }
            }

            return null;
        }

        private static string Walk(string directory, int depth, ref int visited, int maxVisited, Stopwatch stopwatch, long timeoutMs)
        {
            if (visited >= maxVisited || stopwatch.ElapsedMilliseconds > timeoutMs)
            {
                return null;
            }
            visited++;

            string[] children;
            try
            {
                children = Directory.GetDirectories(directory);
            }
            catch
            {
                return null;
            }

            foreach (string child in children)
            {
                string name;
                FileAttributes attributes;
                try
                {
                    name = Path.GetFileName(child);
                    attributes = File.GetAttributes(child);
                }
                catch
                {
                    continue;
                }

                if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                {
                    continue;
                }
                if (SkipDirectoryNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(name, "Schedule I", StringComparison.OrdinalIgnoreCase) && IsValidGameDirectory(child))
                {
                    return child;
                }

                if (depth < 4)
                {
                    string hit = Walk(child, depth + 1, ref visited, maxVisited, stopwatch, timeoutMs);
                    if (hit != null)
                    {
                        return hit;
                    }
                }
            }

            return null;
        }
    }

    internal sealed class AttachSummary
    {
        public int Copied;
        public int Updated;
        public int Skipped;
        public int MissingSource;
        public int Errors;
        public readonly List<string> ErrorDetails = new List<string>();
    }

    internal static class Attacher
    {
        private static readonly string[] PackageFiles =
        {
            "version.dll",
            @"UserData\Loader.cfg"
        };

        private static readonly string[] PackageDirectories =
        {
            "MelonLoader",
            "Mods",
            @"ScheduleI-ControlCenter\dist",
            "ScheduleI-ControlCenter"
        };

        // Directories that must be replaced wholesale (with stale files removed)
        // instead of merged, so previous versions cannot leave conflicting files
        // behind. User-owned subfolders such as InstallRecords and Backups live
        // outside these paths and are never touched.
        private static readonly string[] ReplaceDirectories =
        {
            "MelonLoader",
            @"ScheduleI-ControlCenter\dist"
        };

        public static bool IsAttached(string gameDirectory)
        {
            if (!GameLocator.IsValidGameDirectory(gameDirectory))
            {
                return false;
            }

            return
                File.Exists(Path.Combine(gameDirectory, @"ScheduleI-ControlCenter\dist\ScheduleIControlCenter.exe")) &&
                File.Exists(Path.Combine(gameDirectory, @"Mods\ScheduleIControlBridge.dll")) &&
                File.Exists(Path.Combine(gameDirectory, @"version.dll")) &&
                File.Exists(Path.Combine(gameDirectory, @"MelonLoader\net6\MelonLoader.dll"));
        }

        public static string ControlCenterExePath(string gameDirectory)
        {
            return Path.Combine(gameDirectory, Program.ControlCenterRelativeExe);
        }

        public static AttachSummary Attach(string sourceRoot, string gameDirectory)
        {
            var summary = new AttachSummary();
            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

            // This executable is a package runner, not a self-extracting binary.
            // If a copied launcher is run from inside the old game folder,
            // sourceRoot and gameDirectory are identical and every copy below
            // would otherwise be reported as already present. Fail explicitly
            // so a stale install can never be reported as successfully attached.
            if (PathsEqual(sourceRoot, gameDirectory))
            {
                summary.Errors++;
                summary.ErrorDetails.Add("The launcher is running from the target game folder. Extract the complete Control Center package to a separate folder and run the launcher there.");
                WriteAttachRecord(gameDirectory, sourceRoot, summary, timestamp);
                return summary;
            }

            foreach (string relative in PackageFiles)
            {
                CopyFileIfNeeded(Path.Combine(sourceRoot, relative), Path.Combine(gameDirectory, relative), summary, timestamp);
            }

            foreach (string relative in PackageDirectories)
            {
                string sourceDirectory = Path.Combine(sourceRoot, relative);
                string destinationDirectory = Path.Combine(gameDirectory, relative);
                bool replace = false;
                foreach (string candidate in ReplaceDirectories)
                {
                    if (string.Equals(candidate, relative, StringComparison.OrdinalIgnoreCase))
                    {
                        replace = true;
                        break;
                    }
                }
                if (replace)
                    ReplaceDirectoryIfNeeded(sourceDirectory, destinationDirectory, summary, timestamp);
                else
                    CopyDirectoryIfNeeded(sourceDirectory, destinationDirectory, summary, timestamp);
            }

            // Attach the launcher itself so it is available from the game folder too.
            string self = Assembly.GetExecutingAssembly().Location;
            string selfDestination = Path.Combine(gameDirectory, Path.GetFileName(self));
            if (!PathsEqual(self, selfDestination))
            {
                CopyFileIfNeeded(self, selfDestination, summary, timestamp);
            }

            WriteAttachRecord(gameDirectory, sourceRoot, summary, timestamp);
            return summary;
        }

        private static void ReplaceDirectoryIfNeeded(string sourceDirectory, string destinationDirectory, AttachSummary summary, string timestamp)
        {
            if (!Directory.Exists(sourceDirectory))
            {
                summary.MissingSource++;
                return;
            }
            if (PathsEqual(sourceDirectory, destinationDirectory))
            {
                summary.Skipped++;
                return;
            }
            if (!Directory.Exists(destinationDirectory))
            {
                CopyDirectoryIfNeeded(sourceDirectory, destinationDirectory, summary, timestamp);
                return;
            }
            if (IsTreeCurrent(sourceDirectory, destinationDirectory))
            {
                summary.Skipped++;
                return;
            }

            string backup = NextBackupPath(destinationDirectory, timestamp);
            try
            {
                Directory.Move(destinationDirectory, backup);
            }
            catch (Exception ex)
            {
                summary.Errors++;
                if (summary.ErrorDetails.Count < 10)
                {
                    summary.ErrorDetails.Add("Could not move the old " + Path.GetFileName(destinationDirectory) + " aside (close Schedule I first): " + ex.Message);
                }
                return;
            }

            try
            {
                Directory.CreateDirectory(destinationDirectory);
                CopyDirectoryContents(sourceDirectory, destinationDirectory, summary);
                // The new tree is complete; remove the stale backup so old
                // files can never conflict with the new version.
                Directory.Delete(backup, true);
            }
            catch (Exception ex)
            {
                summary.Errors++;
                if (summary.ErrorDetails.Count < 10)
                {
                    summary.ErrorDetails.Add("Replacing " + Path.GetFileName(destinationDirectory) + " failed: " + ex.Message);
                }
                try
                {
                    if (Directory.Exists(destinationDirectory))
                        Directory.Delete(destinationDirectory, true);
                    if (Directory.Exists(backup))
                        Directory.Move(backup, destinationDirectory);
                }
                catch
                {
                }
            }
        }

        private static bool IsTreeCurrent(string sourceDirectory, string destinationDirectory)
        {
            string directoryName = Path.GetFileName(sourceDirectory.TrimEnd('\\', '/'));
            string[] keyFiles = directoryName.Equals("dist", StringComparison.OrdinalIgnoreCase)
                ? new[] { "ScheduleIControlCenter.exe", "ScheduleIControlCenter.Cli.exe" }
                : new[] { @"net6\MelonLoader.dll", @"net6\Il2CppInterop.Generator.dll", @"net6\Il2CppInterop.Runtime.dll", @"Dependencies\Il2CppAssemblyGenerator\Config.cfg" };
            foreach (string relative in keyFiles)
            {
                if (PathsEqual(sourceDirectory, destinationDirectory))
                    continue;
                string source = Path.Combine(sourceDirectory, relative);
                string destination = Path.Combine(destinationDirectory, relative);
                if (!File.Exists(source) || !File.Exists(destination) || !FilesEqual(source, destination))
                    return false;
            }
            // The destination must not contain files that the package does not
            // ship; anything extra is stale leftover from an older version and
            // forces a wholesale replacement so it cannot conflict later.
            HashSet<string> sourceFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                sourceFiles.Add(file.Substring(sourceDirectory.Length).TrimStart('\\', '/'));
            }
            foreach (string file in Directory.GetFiles(destinationDirectory, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(destinationDirectory.Length).TrimStart('\\', '/');
                if (!sourceFiles.Contains(relative))
                    return false;
            }
            return true;
        }

        private static void CopyDirectoryIfNeeded(string sourceDirectory, string destinationDirectory, AttachSummary summary, string timestamp)
        {
            if (!Directory.Exists(sourceDirectory))
            {
                summary.MissingSource++;
                return;
            }
            if (PathsEqual(sourceDirectory, destinationDirectory))
            {
                summary.Skipped++;
                return;
            }

            try
            {
                Directory.CreateDirectory(destinationDirectory);
                CopyDirectoryContents(sourceDirectory, destinationDirectory, summary);
            }
            catch (Exception ex)
            {
                summary.Errors++;
                if (summary.ErrorDetails.Count < 10)
                {
                    summary.ErrorDetails.Add("Copy directory failed: " + ex.Message);
                }
            }
        }

        private static void CopyDirectoryContents(string sourceDirectory, string destinationDirectory, AttachSummary summary)
        {
            foreach (string file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(sourceDirectory.Length).TrimStart('\\', '/');
                CopyFileIfNeeded(file, Path.Combine(destinationDirectory, relative), summary, string.Empty);
            }
        }

        private static void CopyFileIfNeeded(string sourceFile, string destinationFile, AttachSummary summary, string timestamp)
        {
            try
            {
                if (PathsEqual(sourceFile, destinationFile))
                {
                    summary.Skipped++;
                    return;
                }
                if (!File.Exists(sourceFile))
                {
                    summary.MissingSource++;
                    return;
                }

                if (File.Exists(destinationFile))
                {
                    if (FilesEqual(sourceFile, destinationFile))
                    {
                        summary.Skipped++;
                        return;
                    }

                    // Preserve the previous version next to the file for rollback.
                    string backup = NextBackupPath(destinationFile, timestamp);
                    File.Move(destinationFile, backup);
                    summary.Updated++;
                }
                else
                {
                    summary.Copied++;
                }

                string directory = Path.GetDirectoryName(destinationFile);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.Copy(sourceFile, destinationFile, true);
            }
            catch (Exception ex)
            {
                summary.Errors++;
                if (summary.ErrorDetails.Count < 10)
                {
                    summary.ErrorDetails.Add(Path.GetFileName(sourceFile) + ": " + ex.Message);
                }
            }
        }

        private static string NextBackupPath(string destination, string timestamp)
        {
            string suffix = string.IsNullOrEmpty(timestamp)
                ? DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")
                : timestamp;
            string candidate = destination + ".bak-" + suffix;
            int attempt = 1;
            while (File.Exists(candidate) || Directory.Exists(candidate))
            {
                candidate = destination + ".bak-" + suffix + "-" + attempt.ToString();
                attempt++;
            }
            return candidate;
        }

        private static bool FilesEqual(string first, string second)
        {
            try
            {
                using (var a = File.OpenRead(first))
                using (var b = File.OpenRead(second))
                {
                    if (a.Length != b.Length)
                    {
                        return false;
                    }
                    using (var sha = SHA256.Create())
                    {
                        byte[] hashA = sha.ComputeHash(a);
                        byte[] hashB = sha.ComputeHash(b);
                        return hashA.SequenceEqual(hashB);
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool PathsEqual(string first, string second)
        {
            try
            {
                return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void WriteAttachRecord(string gameDirectory, string sourceRoot, AttachSummary summary, string timestamp)
        {
            try
            {
                string recordDirectory = Path.Combine(gameDirectory, @"ScheduleI-ControlCenter\InstallRecords");
                Directory.CreateDirectory(recordDirectory);

                var sb = new StringBuilder();
                sb.AppendLine("Schedule I Control Center launcher attach record");
                sb.AppendLine("Timestamp: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("Source: " + sourceRoot);
                sb.AppendLine("Target: " + gameDirectory);
                sb.AppendLine("Copied: " + summary.Copied);
                sb.AppendLine("Updated: " + summary.Updated);
                sb.AppendLine("Skipped (already present and identical): " + summary.Skipped);
                sb.AppendLine("Missing in package: " + summary.MissingSource);
                sb.AppendLine("Errors: " + summary.Errors);
                foreach (string detail in summary.ErrorDetails)
                {
                    sb.AppendLine("  - " + detail);
                }

                File.WriteAllText(Path.Combine(recordDirectory, "launcher-attach-" + timestamp + ".log"), sb.ToString());
            }
            catch
            {
            }
        }

        public static int CheckState(string gameDirectory, StringBuilder output)
        {
            if (string.IsNullOrWhiteSpace(gameDirectory))
            {
                output.AppendLine("NO_DIR");
                return 1;
            }

            string resolved;
            try
            {
                resolved = Path.GetFullPath(gameDirectory);
            }
            catch (Exception ex)
            {
                output.AppendLine("INVALID_PATH: " + ex.Message);
                return 2;
            }

            if (!Directory.Exists(resolved))
            {
                output.AppendLine("NOT_A_DIRECTORY: " + resolved);
                return 1;
            }
            if (!GameLocator.IsValidGameDirectory(resolved))
            {
                output.AppendLine("NOT_A_GAME_FOLDER: " + resolved);
                return 1;
            }

            output.AppendLine("GAME_FOLDER: " + resolved);
            string[] required =
            {
                @"ScheduleI-ControlCenter\dist\ScheduleIControlCenter.exe",
                @"Mods\ScheduleIControlBridge.dll",
                @"version.dll",
                @"MelonLoader\net6\MelonLoader.dll",
                @"UserData\Loader.cfg"
            };

            var missing = new List<string>();
            foreach (string relative in required)
            {
                if (!File.Exists(Path.Combine(resolved, relative)))
                {
                    missing.Add(relative);
                }
            }

            output.AppendLine("ATTACHED: " + (missing.Count == 0 ? "TRUE" : "FALSE"));
            if (missing.Count > 0)
            {
                output.AppendLine("MISSING:");
                foreach (string item in missing)
                {
                    output.AppendLine("  " + item);
                }
                return 2;
            }
            return 0;
        }

        public static int AttachCli(string gameDirectory, StringBuilder output)
        {
            if (string.IsNullOrWhiteSpace(gameDirectory))
            {
                output.AppendLine("NO_DIR");
                return 1;
            }

            string resolved;
            try
            {
                resolved = Path.GetFullPath(gameDirectory);
            }
            catch (Exception ex)
            {
                output.AppendLine("INVALID_PATH: " + ex.Message);
                return 2;
            }

            if (!Directory.Exists(resolved))
            {
                output.AppendLine("NOT_A_DIRECTORY: " + resolved);
                return 1;
            }
            if (!GameLocator.IsValidGameDirectory(resolved))
            {
                output.AppendLine("NOT_A_GAME_FOLDER: " + resolved);
                return 1;
            }

            string sourceRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            AttachSummary summary = Attach(sourceRoot, resolved);
            output.AppendLine("TARGET: " + resolved);
            output.AppendLine("COPIED: " + summary.Copied);
            output.AppendLine("UPDATED: " + summary.Updated);
            output.AppendLine("SKIPPED: " + summary.Skipped);
            output.AppendLine("MISSING_IN_PACKAGE: " + summary.MissingSource);
            output.AppendLine("ERRORS: " + summary.Errors);
            foreach (string detail in summary.ErrorDetails)
            {
                output.AppendLine("ERROR_DETAIL: " + detail);
            }
            return summary.Errors == 0 && summary.MissingSource == 0 ? 0 : 3;
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly Label statusLabel;
        private readonly TextBox pathBox;
        private readonly Button browseButton;
        private readonly Button attachButton;
        private readonly Button launchButton;
        private readonly Button cancelButton;
        private readonly Label detailLabel;

        private string gameDirectory;

        public MainForm()
        {
            Text = "Schedule I Control Center - Attach & Launch";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new System.Drawing.Size(640, 240);
            Font = new System.Drawing.Font("Segoe UI", 9F);

            statusLabel = new Label
            {
                AutoSize = false,
                Location = new System.Drawing.Point(16, 14),
                Size = new System.Drawing.Size(608, 22),
                Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold),
                Text = "Searching for Schedule I..."
            };

            var pathLabel = new Label
            {
                Text = "Game installation:",
                Location = new System.Drawing.Point(16, 50),
                AutoSize = true
            };

            pathBox = new TextBox
            {
                ReadOnly = true,
                Location = new System.Drawing.Point(16, 72),
                Size = new System.Drawing.Size(500, 23)
            };

            browseButton = new Button
            {
                Text = "Browse...",
                Location = new System.Drawing.Point(524, 70),
                Size = new System.Drawing.Size(100, 27)
            };

            detailLabel = new Label
            {
                AutoSize = false,
                Location = new System.Drawing.Point(16, 104),
                Size = new System.Drawing.Size(608, 58),
                ForeColor = System.Drawing.Color.DimGray
            };

            attachButton = new Button
            {
                Text = "Attach and launch",
                Location = new System.Drawing.Point(16, 196),
                Size = new System.Drawing.Size(150, 30)
            };

            launchButton = new Button
            {
                Text = "Launch only",
                Location = new System.Drawing.Point(176, 196),
                Size = new System.Drawing.Size(120, 30)
            };

            cancelButton = new Button
            {
                Text = "Cancel",
                Location = new System.Drawing.Point(540, 196),
                Size = new System.Drawing.Size(80, 30),
                DialogResult = DialogResult.Cancel
            };

            Controls.Add(statusLabel);
            Controls.Add(pathLabel);
            Controls.Add(pathBox);
            Controls.Add(browseButton);
            Controls.Add(detailLabel);
            Controls.Add(attachButton);
            Controls.Add(launchButton);
            Controls.Add(cancelButton);

            browseButton.Click += BrowseClicked;
            attachButton.Click += AttachClicked;
            launchButton.Click += LaunchClicked;
            cancelButton.Click += (s, e) => Close();
            AcceptButton = attachButton;
            CancelButton = cancelButton;

            attachButton.Enabled = false;
            launchButton.Enabled = false;
            Shown += async (s, e) => await RunSearchAsync();
        }

        private async Task RunSearchAsync()
        {
            statusLabel.Text = "Searching for Schedule I...";
            attachButton.Enabled = false;
            launchButton.Enabled = false;

            string found = await Task.Run(() => GameLocator.FindGame());
            if (IsDisposed)
            {
                return;
            }

            if (found != null)
            {
                SetGameDirectory(found);
                statusLabel.Text = "Found:";
                detailLabel.Text = Attacher.IsAttached(found)
                    ? "The Control Center runtime is already attached to this installation - no changes are needed."
                    : "Attach will copy MelonLoader, the bridge mod, the Control Center, version.dll and loader configuration into this folder. Saves, backups and bridge settings are never touched.";
            }
            else
            {
                statusLabel.Text = "Schedule I was not found automatically.";
                detailLabel.Text = "Browse to the game folder that contains Schedule I.exe. If you already installed the Control Center there, use Launch only.";
            }
        }

        private void SetGameDirectory(string directory)
        {
            gameDirectory = directory;
            pathBox.Text = directory;
            attachButton.Enabled = GameLocator.IsValidGameDirectory(directory);
            launchButton.Enabled = File.Exists(Attacher.ControlCenterExePath(directory));
        }

        private void BrowseClicked(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select the Schedule I folder that contains Schedule I.exe";
                dialog.ShowNewFolderButton = false;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                string selected = dialog.SelectedPath;
                if (!GameLocator.IsValidGameDirectory(selected))
                {
                    MessageBox.Show(this, "That folder does not contain Schedule I.exe.", "Not a game folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                SetGameDirectory(selected);
                statusLabel.Text = "Selected:";
                detailLabel.Text = Attacher.IsAttached(selected)
                    ? "The Control Center runtime is already attached to this installation - no changes are needed."
                    : "Attach will copy MelonLoader, the bridge mod, the Control Center, version.dll and loader configuration into this folder. Saves, backups and bridge settings are never touched.";
            }
        }

        private async void AttachClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(gameDirectory))
            {
                return;
            }

            if (IsGameOrControlCenterRunning())
            {
                var warning = MessageBox.Show(this,
                    "Schedule I or the Control Center is currently running. Game files may be locked and attaching could fail. Close them first (recommended), or continue anyway?",
                    "Application is running",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (warning != DialogResult.Yes)
                {
                    return;
                }
            }

            var confirm = MessageBox.Show(this,
                "Attach the Control Center runtime to:\r\n\r\n" + gameDirectory +
                "\r\n\r\nThis copies MelonLoader, the bridge mod, the Control Center, version.dll and loader configuration into the game folder. Saves, backups and existing bridge settings will not be changed.",
                "Confirm attach",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            SetBusy(true);
            string sourceRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            AttachSummary summary = null;
            string error = null;
            try
            {
                summary = await Task.Run(() => Attacher.Attach(sourceRoot, gameDirectory));
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            if (IsDisposed)
            {
                return;
            }
            SetBusy(false);

            if (error != null)
            {
                MessageBox.Show(this, "Attach failed: " + error, "Attach", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            statusLabel.Text = "Attached.";
            detailLabel.Text = "Copied: " + summary.Copied + " | Updated: " + summary.Updated + " | Already present: " + summary.Skipped;
            if (summary.Errors > 0 || summary.MissingSource > 0)
            {
                detailLabel.Text += " | Missing package files: " + summary.MissingSource + " | Errors: " + summary.Errors;
                MessageBox.Show(this,
                    "The attach did not complete.\r\n\r\n" + string.Join("\r\n", summary.ErrorDetails.Take(5)) +
                    (summary.MissingSource > 0 ? "\r\nMissing package files: " + summary.MissingSource : string.Empty),
                    "Attach completed with errors",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            LaunchControlCenter();
        }

        private void LaunchClicked(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(gameDirectory))
            {
                LaunchControlCenter();
            }
        }

        private void LaunchControlCenter()
        {
            string exe = Attacher.ControlCenterExePath(gameDirectory);
            if (!File.Exists(exe))
            {
                MessageBox.Show(this,
                    "The Control Center executable is not installed at:\r\n" + exe + "\r\n\r\nAttach the runtime first, or check the selected folder.",
                    "Not installed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = Path.GetDirectoryName(exe),
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not start the Control Center: " + ex.Message, "Launch", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Close();
        }

        private static bool IsGameOrControlCenterRunning()
        {
            try
            {
                return Process.GetProcessesByName("Schedule I").Length > 0 ||
                       Process.GetProcessesByName("ScheduleIControlCenter").Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private void SetBusy(bool busy)
        {
            bool hasDirectory = gameDirectory != null;
            attachButton.Enabled = !busy && hasDirectory && GameLocator.IsValidGameDirectory(gameDirectory);
            launchButton.Enabled = !busy && hasDirectory && File.Exists(Attacher.ControlCenterExePath(gameDirectory));
            browseButton.Enabled = !busy;
        }
    }
}
