using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ScheduleIControlCenter
{
    internal enum DiagnosticSeverity { Info, Warning, Error, Fatal }

    internal enum DiagnosticCategory
    {
        Startup, Ui, Task, Bridge, Protocol, Filesystem, Save, Backup, Resource,
        Validation, Compatibility, Security, Configuration, Unknown
    }

    internal sealed class DiagnosticIncident
    {
        public DateTime TimestampUtc { get; set; }
        public long Sequence { get; set; }
        public string CorrelationId { get; set; }
        public DiagnosticSeverity Severity { get; set; }
        public DiagnosticCategory Category { get; set; }
        public string Operation { get; set; }
        public string ExceptionType { get; set; }
        public string UserMessage { get; set; }
        public string TechnicalDetails { get; set; }
        public string Reasoning { get; set; }
        public string Evidence { get; set; }
        public string NextActions { get; set; }

        public string Summary
        {
            get { return string.Format("{0} [{1}] {2}", Severity, Category, UserMessage ?? "Unknown problem."); }
        }
        public override string ToString() { return Summary; }
    }

    internal sealed class DiagnosticHealthSnapshot
    {
        public DateTime CapturedUtc { get; set; }
        public string ApplicationVersion { get; set; }
        public string RuntimeVersion { get; set; }
        public string OperatingSystem { get; set; }
        public string ProcessArchitecture { get; set; }
        public string GameRoot { get; set; }
        public string SaveRoot { get; set; }
        public bool GameRunning { get; set; }
        public bool BridgeReady { get; set; }
        public bool SaveReady { get; set; }
        public string BridgeStatus { get; set; }

        public static DiagnosticHealthSnapshot Capture(GameEnvironment environment, bool bridgeReady, bool saveReady, string bridgeStatus)
        {
            bool running = false;
            string gameRoot = string.Empty;
            string saveRoot = string.Empty;
            try
            {
                if (environment != null)
                {
                    running = environment.IsGameRunning();
                    gameRoot = DiagnosticsService.SafePath(environment.GameRoot);
                    saveRoot = DiagnosticsService.SafePath(environment.SaveRoot);
                }
            }
            catch (Exception ex)
            {
                DiagnosticsService.Record(ex, "health.snapshot", DiagnosticCategory.Configuration, DiagnosticSeverity.Warning,
                    "Some environment health information could not be read.", null);
            }
            return new DiagnosticHealthSnapshot
            {
                CapturedUtc = DateTime.UtcNow,
                ApplicationVersion = ReleaseInfo.Version,
                RuntimeVersion = Environment.Version.ToString(),
                OperatingSystem = Environment.OSVersion.VersionString,
                ProcessArchitecture = Environment.Is64BitProcess ? "x64" : "x86",
                GameRoot = gameRoot,
                SaveRoot = saveRoot,
                GameRunning = running,
                BridgeReady = bridgeReady,
                SaveReady = saveReady,
                BridgeStatus = DiagnosticsService.Redact(bridgeStatus ?? "unknown")
            };
        }

        public string ToReportText()
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("HEALTH SNAPSHOT");
            text.AppendLine("Captured (UTC): " + CapturedUtc.ToString("o"));
            text.AppendLine("Application: " + (ApplicationVersion ?? "unknown"));
            text.AppendLine("Runtime: " + (RuntimeVersion ?? "unknown") + " | " + (OperatingSystem ?? "unknown") + " | " + (ProcessArchitecture ?? "unknown"));
            text.AppendLine("Game running: " + GameRunning);
            text.AppendLine("Bridge ready: " + BridgeReady + " (" + (BridgeStatus ?? "unknown") + ")");
            text.AppendLine("Save ready: " + SaveReady);
            text.AppendLine("Game root: " + (GameRoot ?? "unknown"));
            text.AppendLine("Save root: " + (SaveRoot ?? "unknown"));
            return text.ToString();
        }
    }

    internal sealed class DiagnosticCatalogEntry
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public string Symptoms { get; set; }
        public string Reasoning { get; set; }
        public string Evidence { get; set; }
        public string NextSteps { get; set; }
        public override string ToString() { return Title; }
    }

    internal static class DiagnosticCatalog
    {
        public static readonly IReadOnlyList<DiagnosticCatalogEntry> Entries = Build();

        private static IReadOnlyList<DiagnosticCatalogEntry> Build()
        {
            List<DiagnosticCatalogEntry> result = new List<DiagnosticCatalogEntry>();
            Add(result, "startup-resource", "Startup, image, or font resource", "The splash or main window does not appear, or startup reports a missing resource.", "The embedded logo/font may be absent, unreadable, or incompatible with the installed build.", "Check the reported resource name and versioned executable; reinstall or use the current release executable.", "Review the first startup incident and verify the EXE was not partially copied or quarantined.");
            Add(result, "null-reference", "Missing runtime object", "A workflow stops with a null-reference error.", "A save, bridge response, selected row, or UI control was not available when the operation ran.", "Refresh the save/bridge state, select the required item, and retry. If repeatable, include the incident ID.", "Inspect operation, selected-save state, and the stack trace in the safe report.");
            Add(result, "argument-invalid", "Invalid argument or operation state", "An action is rejected before changing the game or save.", "A value is outside the supported range or a workflow was invoked in the wrong state.", "Use the displayed bounds; refresh and create a new preview before applying.", "Inspect validation message, operation, and preview/revision evidence.");
            Add(result, "filesystem", "File, directory, or permission problem", "Backup, validation, or offline apply cannot read or write a file.", "The path may be missing, locked by another process, read-only, or inaccessible to this user.", "Close the game for offline writes, check the directory exists, and verify write permission; never delete the original without a backup.", "Inspect the redacted path, operation, exception type, and whether the game was running.");
            Add(result, "json", "Save JSON could not be parsed", "Validation or an offline edit reports invalid JSON.", "A save file may be incomplete, manually edited, or from a newer incompatible build.", "Restore the latest complete backup and validate again; keep the incident report with the save version.", "Inspect the named JSON file only; reports intentionally exclude save contents.");
            Add(result, "bridge", "Bridge offline, timeout, or protocol mismatch", "Live controls are unavailable or a bridge response is rejected.", "Schedule I/MelonLoader may not be running, the bridge may be missing, or the response may be stale/malformed.", "Start the game with the installed bridge, load the save as the same-user host, then refresh status. Reinstall the matching bridge for a protocol/version mismatch.", "Inspect bridge operation, response code, timeout/protocol evidence, and game build.");
            Add(result, "compatibility", "Unknown game build", "The bridge is present but live changes are gated.", "The installed game build has not passed the reviewed compatibility checks.", "Update the Control Center/bridge pair or use offline controls where supported; do not force unreviewed live mutations.", "Inspect gameVersion, expected build, and compatibility diagnostics in health/report output.");
            Add(result, "authority", "Save or multiplayer authority gate", "A live preview/apply is disabled while a save is loaded.", "The game is not loaded, the user is not the same-user solo host, or a remote player changed authority.", "Load the intended save as solo host, disconnect remote players, refresh status, and create a new preview.", "Inspect saveReady, isSoloHost, remote-player, and revision evidence.");
            Add(result, "preview", "Preview expired or revision conflict", "Apply is rejected after a preview appeared valid.", "Previews are short-lived and any intervening mutation invalidates their revision.", "Refresh the data and create a new preview immediately before applying.", "Inspect preview ID, expected/current revision, and timestamp.");
            Add(result, "verification", "Verification or rollback warning", "An apply reports success but readback does not match.", "The game may have rejected or normalized a value, or another mutation occurred between apply and readback.", "Stop repeated applies, refresh/read back, and restore the backup only if the resulting state is understood.", "Inspect expected versus actual values and backup path; reports omit save contents.");
            Add(result, "numeric", "Numeric or range validation", "A price, allowance, or multiplier is rejected or clipped.", "The value is outside the exact supported single-precision/whole-dollar range or violates a min/max relationship.", "Use values within the displayed range and ensure minimum does not exceed maximum.", "Inspect requested value and validation code in the incident details.");
            Add(result, "disposal-ui-task", "Disposed control, UI-thread, or background task error", "A late refresh or background action fails after closing or changing pages.", "An asynchronous callback completed after its control was disposed, or a UI update ran off the UI thread.", "Close/reopen the Control Center and retry; if repeatable, provide the incident ID and report.", "Inspect task/UI boundary, thread exception, and operation timing.");
            return result.AsReadOnly();
        }

        private static void Add(List<DiagnosticCatalogEntry> list, string key, string title, string symptoms, string reasoning, string nextSteps, string evidence)
        {
            list.Add(new DiagnosticCatalogEntry { Key = key, Title = title, Symptoms = symptoms, Reasoning = reasoning, NextSteps = nextSteps, Evidence = evidence });
        }

        public static DiagnosticCatalogEntry Match(DiagnosticIncident incident)
        {
            if (incident == null) return null;
            string type = (incident.ExceptionType ?? string.Empty).ToLowerInvariant();
            string operation = (incident.Operation ?? string.Empty).ToLowerInvariant();
            if (type.Contains("nullreference")) return Find("null-reference");
            if (type.Contains("json") || type.Contains("format") || operation.Contains("json")) return Find("json");
            if (type.Contains("unauthorized") || type.Contains("ioexception") || type.Contains("filenotfound") || type.Contains("directorynotfound") || operation.Contains("file") || operation.Contains("save")) return Find("filesystem");
            if (type.Contains("timeout") || incident.Category == DiagnosticCategory.Bridge || operation.Contains("bridge")) return Find("bridge");
            if (type.Contains("argument") || type.Contains("invalidoperation") || type.Contains("range")) return Find("argument-invalid");
            if (type.Contains("disposed") || type.Contains("task") || incident.Category == DiagnosticCategory.Ui) return Find("disposal-ui-task");
            if (incident.Category == DiagnosticCategory.Resource || operation.Contains("splash") || operation.Contains("font")) return Find("startup-resource");
            if (incident.Category == DiagnosticCategory.Compatibility) return Find("compatibility");
            return null;
        }

        public static DiagnosticCatalogEntry Find(string key)
        {
            return Entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
        }
    }

    internal sealed class DiagnosticsService
    {
        private const int MaxIncidents = 200;
        private const int MaxFieldLength = 4000;
        private const int MaxReportCharacters = 512 * 1024;
        private const int MaxLogCharacters = 256 * 1024;
        private static readonly object StaticSync = new object();
        private static DiagnosticsService current;
        private static bool handlersRegistered;
        private readonly object sync = new object();
        private readonly List<DiagnosticIncident> incidents = new List<DiagnosticIncident>();
        private readonly string storageDirectory;
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        private long nextSequence;
        private bool writing;
        private DiagnosticHealthSnapshot health;

        public DiagnosticsService(string directory = null)
        {
            storageDirectory = directory ?? DefaultStorageDirectory();
            LoadPersisted();
        }

        public static DiagnosticsService Current
        {
            get
            {
                lock (StaticSync)
                {
                    if (current == null) current = new DiagnosticsService();
                    return current;
                }
            }
        }

        public static void InitializeGlobalHandlers()
        {
            lock (StaticSync)
            {
                if (handlersRegistered) return;
                handlersRegistered = true;
                try { AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { Record(e.ExceptionObject as Exception ?? new Exception(Convert.ToString(e.ExceptionObject)), "appdomain.unhandled", DiagnosticCategory.Startup, DiagnosticSeverity.Fatal, "The application encountered an unhandled failure.", e.IsTerminating ? "Runtime marked the process terminating." : "Runtime continued after the unhandled failure."); }; } catch { }
                try { System.Windows.Forms.Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e) { Record(e.Exception, "ui.thread", DiagnosticCategory.Ui, DiagnosticSeverity.Error, "A user-interface operation failed.", null); }; } catch { }
                try { TaskScheduler.UnobservedTaskException += delegate(object s, UnobservedTaskExceptionEventArgs e) { Record(e.Exception, "task.unobserved", DiagnosticCategory.Task, DiagnosticSeverity.Error, "A background operation failed without being observed.", null); try { e.SetObserved(); } catch { } }; } catch { }
            }
        }

        public static DiagnosticIncident Record(Exception exception, string operation, DiagnosticCategory category, DiagnosticSeverity severity, string userMessage, string evidence)
        {
            return Current.RecordIncident(exception, operation, category, severity, userMessage, evidence, null);
        }

        public static DiagnosticIncident RecordFailure(OperationResult result, string operation, DiagnosticCategory category, string evidence)
        {
            if (result == null || result.Success) return null;
            string message = result.Message ?? "The operation failed.";
            DiagnosticIncident incident = Current.RecordIncident(null, operation, category, DiagnosticSeverity.Error, message, evidence, result.Code);
            return incident;
        }

        public DiagnosticIncident RecordIncident(Exception exception, string operation, DiagnosticCategory category, DiagnosticSeverity severity, string userMessage, string evidence, string code = null)
        {
            try
            {
                Exception root = Unwrap(exception);
                DiagnosticIncident incident = new DiagnosticIncident
                {
                    TimestampUtc = DateTime.UtcNow,
                    CorrelationId = Guid.NewGuid().ToString("N"),
                    Severity = severity,
                    Category = category,
                    Operation = Trim(Redact(operation ?? "unknown")),
                    ExceptionType = root == null ? string.Empty : Trim(root.GetType().FullName),
                    UserMessage = Trim(Redact(userMessage ?? "The operation failed.")),
                    TechnicalDetails = Trim(Redact(BuildTechnicalDetails(root, code))),
                    Reasoning = Trim(Redact(ReasoningFor(root, category, operation))),
                    Evidence = Trim(Redact(evidence ?? string.Empty)),
                    NextActions = Trim(Redact(NextActionsFor(root, category, operation)))
                };
                lock (sync)
                {
                    incident.Sequence = ++nextSequence;
                    incidents.Add(incident);
                    while (incidents.Count > MaxIncidents) incidents.RemoveAt(0);
                    PersistLocked(incident);
                }
                return incident;
            }
            catch
            {
                return null;
            }
        }

        public void SetHealth(DiagnosticHealthSnapshot snapshot)
        {
            lock (sync) health = snapshot;
        }

        public DiagnosticHealthSnapshot HealthSnapshot { get { lock (sync) return health; } }
        public IReadOnlyList<DiagnosticIncident> GetIncidents() { lock (sync) return incidents.OrderByDescending(i => i.Sequence).ToList().AsReadOnly(); }

        public string CreateSafeReport()
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("SCHEDULE I CONTROL CENTER DIAGNOSTIC REPORT");
            text.AppendLine("Generated (UTC): " + DateTime.UtcNow.ToString("o"));
            text.AppendLine("Application: " + ReleaseInfo.Version);
            DiagnosticHealthSnapshot snapshot;
            List<DiagnosticIncident> copy;
            lock (sync) { snapshot = health; copy = incidents.OrderBy(i => i.Sequence).ToList(); }
            if (snapshot != null) text.Append(snapshot.ToReportText());
            text.AppendLine("INCIDENTS (oldest first)");
            foreach (DiagnosticIncident incident in copy)
            {
                text.AppendLine(string.Format("#{0} {1:o} [{2}/{3}] {4} ({5})", incident.Sequence, incident.TimestampUtc, incident.Severity, incident.Category, incident.Operation, incident.CorrelationId));
                text.AppendLine("Message: " + incident.UserMessage);
                if (!string.IsNullOrEmpty(incident.ExceptionType)) text.AppendLine("Exception: " + incident.ExceptionType);
                text.AppendLine("Reasoning: " + incident.Reasoning);
                text.AppendLine("Evidence: " + incident.Evidence);
                text.AppendLine("Next actions: " + incident.NextActions);
                if (!string.IsNullOrEmpty(incident.TechnicalDetails)) text.AppendLine("Technical: " + incident.TechnicalDetails);
                text.AppendLine();
                if (text.Length >= MaxReportCharacters) { text.Length = MaxReportCharacters; break; }
            }
            return TrimReport(text.ToString());
        }

        public bool ExportSafeReport(string path, out string error)
        {
            error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A report path is required.");
                string directory = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(path, CreateSafeReport(), new UTF8Encoding(false));
                return true;
            }
            catch (Exception ex) { error = Redact(ex.Message); Record(ex, "diagnostics.export", DiagnosticCategory.Filesystem, DiagnosticSeverity.Warning, "The diagnostic report could not be exported.", null); return false; }
        }

        public static string DefaultStorageDirectory()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "ScheduleIControlCenter", "Diagnostics");
        }

        internal static string SafePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            string full = path;
            try { full = Path.GetFullPath(path); } catch { }
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(user) && full.StartsWith(user, StringComparison.OrdinalIgnoreCase)) return "%USERPROFILE%" + full.Substring(user.Length);
            return Redact(full);
        }

        internal static string Redact(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            string result = value;
            result = Regex.Replace(result, "(?i)(authorization\\s*[:=]\\s*)(bearer\\s+)?[A-Za-z0-9._~+/-]+", "$1[REDACTED]");
            result = Regex.Replace(result, "(?i)(password|token|secret|api[_-]?key|authorization)\\s*[:=]\\s*[^,;\\s]+", "$1=[REDACTED]");
            result = Regex.Replace(result, "(?i)(bearer\\s+)[A-Za-z0-9._~+/-]+", "$1[REDACTED]");
            result = Regex.Replace(result, "(?i)(pipe|request|correlation)[_-]?(id)\\s*[:=]\\s*[0-9a-f]{16,}", "$1$2=[REDACTED]");
            return result.Replace("\0", string.Empty);
        }

        private static string Trim(string value) { if (string.IsNullOrEmpty(value)) return string.Empty; return value.Length > MaxFieldLength ? value.Substring(0, MaxFieldLength) + "…" : value; }
        private static string TrimReport(string value) { return value.Length > MaxReportCharacters ? value.Substring(0, MaxReportCharacters) : value; }
        private static Exception Unwrap(Exception ex) { AggregateException aggregate = ex as AggregateException; return aggregate == null ? ex : aggregate.Flatten().InnerExceptions.FirstOrDefault() ?? aggregate; }
        private static string BuildTechnicalDetails(Exception ex, string code)
        {
            if (ex == null) return string.IsNullOrEmpty(code) ? string.Empty : "Code=" + code;
            StringBuilder details = new StringBuilder();
            details.Append(ex.Message ?? string.Empty);
            if (!string.IsNullOrEmpty(code)) details.Append(" | code=" + code);
            if (!string.IsNullOrEmpty(ex.StackTrace)) details.Append(" | stack=" + ex.StackTrace);
            Exception inner = ex.InnerException;
            while (inner != null) { details.Append(" | inner=" + inner.GetType().FullName + ": " + inner.Message); inner = inner.InnerException; }
            return details.ToString();
        }
        private static string ReasoningFor(Exception ex, DiagnosticCategory category, string operation)
        {
            DiagnosticIncident probe = new DiagnosticIncident { ExceptionType = ex == null ? string.Empty : ex.GetType().FullName, Category = category, Operation = operation };
            DiagnosticCatalogEntry match = DiagnosticCatalog.Match(probe);
            return match == null ? "The operation reported a failure; the captured operation, exception chain, and stack provide the best available evidence." : match.Reasoning;
        }
        private static string NextActionsFor(Exception ex, DiagnosticCategory category, string operation)
        {
            DiagnosticIncident probe = new DiagnosticIncident { ExceptionType = ex == null ? string.Empty : ex.GetType().FullName, Category = category, Operation = operation };
            DiagnosticCatalogEntry match = DiagnosticCatalog.Match(probe);
            return match == null ? "Retry once after refreshing state. If it repeats, export this safe report and include the incident correlation ID." : match.NextSteps;
        }

        private void LoadPersisted()
        {
            try
            {
                string path = Path.Combine(storageDirectory, "incidents.jsonl");
                if (!File.Exists(path)) return;
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                foreach (string line in lines.Skip(Math.Max(0, lines.Length - MaxIncidents)))
                {
                    try { DiagnosticIncident incident = serializer.Deserialize<DiagnosticIncident>(line); if (incident != null) { incidents.Add(incident); nextSequence = Math.Max(nextSequence, incident.Sequence); } } catch { }
                }
            }
            catch { }
        }

        private void PersistLocked(DiagnosticIncident incident)
        {
            if (writing) return;
            writing = true;
            try
            {
                Directory.CreateDirectory(storageDirectory);
                string path = Path.Combine(storageDirectory, "incidents.jsonl");
                File.AppendAllText(path, serializer.Serialize(incident) + Environment.NewLine, new UTF8Encoding(false));
                FileInfo info = new FileInfo(path);
                if (info.Length > MaxLogCharacters)
                {
                    string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                    string[] keep = lines.Skip(Math.Max(0, lines.Length - MaxIncidents)).ToArray();
                    RotateCurrentLog(path);
                    File.WriteAllLines(path, keep, new UTF8Encoding(false));
                }
            }
            catch { }
            finally { writing = false; }
        }

        private static void RotateCurrentLog(string path)
        {
            try
            {
                string oldest = path + ".4";
                if (File.Exists(oldest)) File.Delete(oldest);
                for (int index = 3; index >= 1; index--)
                {
                    string source = path + "." + index;
                    string target = path + "." + (index + 1);
                    if (File.Exists(target)) File.Delete(target);
                    if (File.Exists(source)) File.Move(source, target);
                }
                File.Move(path, path + ".1");
            }
            catch { }
        }
    }
}
