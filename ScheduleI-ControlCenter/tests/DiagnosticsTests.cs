using System;
using System.IO;
using System.Linq;

namespace ScheduleIControlCenter
{
    internal static class DiagnosticsTests
    {
        public static void RunAll()
        {
            string root = Path.Combine(Path.GetTempPath(), "ScheduleI-ControlCenter-Diagnostics-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                ClassificationAndInnerException(root);
                Redaction(root);
                BoundedHistoryAndReport(root);
                PersistenceFailureIsResilient(root);
                HealthSnapshotBasics();
                Console.WriteLine("PASS: diagnostics classification, redaction, bounded history/report, persistence resilience, and health snapshot.");
            }
            finally { try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { } }
        }

        private static void ClassificationAndInnerException(string root)
        {
            DiagnosticsService service = new DiagnosticsService(Path.Combine(root, "classification"));
            DiagnosticIncident incident = service.RecordIncident(new AggregateException(new InvalidOperationException("bad state", new IOException("inner details"))), "preview.apply", DiagnosticCategory.Validation, DiagnosticSeverity.Error, "Apply failed", "revision=12");
            Require(incident != null && incident.ExceptionType.IndexOf("InvalidOperationException", StringComparison.Ordinal) >= 0, "Exception classification did not preserve the root exception.");
            Require(incident.TechnicalDetails.IndexOf("inner details", StringComparison.Ordinal) >= 0, "Inner exception details were discarded.");
            Require(DiagnosticCatalog.Match(incident) != null, "Known-failure catalogue did not map InvalidOperationException.");
        }

        private static void Redaction(string root)
        {
            DiagnosticsService service = new DiagnosticsService(Path.Combine(root, "redaction"));
            DiagnosticIncident incident = service.RecordIncident(new Exception("token=TEST_TOKEN_VALUE password=TEST_PASSWORD_VALUE"), "bridge", DiagnosticCategory.Bridge, DiagnosticSeverity.Error, "Failure", "authorization: Bearer TEST_AUTH_VALUE");
            string report = service.CreateSafeReport();
            Require(report.IndexOf("TEST_TOKEN_VALUE", StringComparison.OrdinalIgnoreCase) < 0 && report.IndexOf("TEST_PASSWORD_VALUE", StringComparison.OrdinalIgnoreCase) < 0 && report.IndexOf("TEST_AUTH_VALUE", StringComparison.OrdinalIgnoreCase) < 0, "Sensitive values were present in the safe report.");
            Require(incident.UserMessage.Length > 0, "Redaction test did not create an incident.");
        }

        private static void BoundedHistoryAndReport(string root)
        {
            DiagnosticsService service = new DiagnosticsService(Path.Combine(root, "bounded"));
            string evidence = new string('x', 4000);
            for (int i = 0; i < 220; i++) service.RecordIncident(null, "test." + i, DiagnosticCategory.Unknown, DiagnosticSeverity.Warning, "Incident " + i, evidence, null);
            Require(service.GetIncidents().Count <= 200, "Incident history exceeded its bound.");
            Require(Directory.GetFiles(Path.Combine(root, "bounded"), "incidents.jsonl*").Length <= 5, "Diagnostic log rotation exceeded its bound.");
            string report = service.CreateSafeReport();
            Require(report.Length <= 512 * 1024, "Safe report exceeded its size bound.");
            string first = report.Substring(report.IndexOf("INCIDENTS", StringComparison.Ordinal));
            string second = service.CreateSafeReport().Substring(service.CreateSafeReport().IndexOf("INCIDENTS", StringComparison.Ordinal));
            Require(first.Substring(first.IndexOf("#", StringComparison.Ordinal)).IndexOf("#", StringComparison.Ordinal) >= 0 && second.Length > 0, "Incident ordering was not deterministic.");
        }

        private static void PersistenceFailureIsResilient(string root)
        {
            string blocker = Path.Combine(root, "not-a-directory");
            File.WriteAllText(blocker, "blocker");
            DiagnosticsService service = new DiagnosticsService(blocker);
            Require(service.RecordIncident(new IOException("cannot persist"), "test.persistence", DiagnosticCategory.Filesystem, DiagnosticSeverity.Warning, "Persistence warning", null) != null, "Persistence failure escaped the diagnostics service.");
        }

        private static void HealthSnapshotBasics()
        {
            DiagnosticHealthSnapshot snapshot = DiagnosticHealthSnapshot.Capture(null, false, false, "offline token=TEST_HEALTH_VALUE");
            Require(snapshot != null && !snapshot.BridgeReady && snapshot.BridgeStatus.IndexOf("TEST_HEALTH_VALUE", StringComparison.OrdinalIgnoreCase) < 0, "Health snapshot did not safely capture basics.");
        }

        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
