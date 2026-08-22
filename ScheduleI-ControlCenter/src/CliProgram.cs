using System;
using System.Globalization;
using System.Linq;

namespace ScheduleIControlCenter
{
    internal static class CliProgram
    {
        private static int Main(string[] args)
        {
            try
            {
                GameEnvironment environment = GameEnvironment.Detect();
                SaveService service = new SaveService(environment);
                SaveDescriptor save = service.DiscoverSaves().FirstOrDefault();

                if (args.Length == 0 || Is(args[0], "help") || Is(args[0], "--help"))
                {
                    PrintUsage();
                    return 0;
                }

                if (save == null)
                    return Print(OperationResult.Fail("No Schedule I save was discovered."));

                OperationResult result;
                if (Is(args[0], "status"))
                {
                    result = OperationResult.Ok(string.Format("Selected={0}; Version={1}; Console={2}; Game={3}", save.Key, save.GameVersion, save.ConsoleEnabled ? "enabled" : "disabled", environment.IsGameRunning() ? "running" : "stopped"));
                }
                else if (Is(args[0], "backup"))
                {
                    result = service.CreateBackup(save, "cli");
                }
                else if (Is(args[0], "validate"))
                {
                    result = service.ValidateSave(save);
                }
                else if (Is(args[0], "console-enable"))
                {
                    result = service.EnableConsoleOffline(save);
                }
                else if (Is(args[0], "property-own") && args.Length >= 2)
                {
                    result = service.OwnPropertyOffline(save, args[1]);
                }
                else if ((Is(args[0], "price-preview") || Is(args[0], "price-apply")) && args.Length >= 3)
                {
                    decimal factor;
                    if (!decimal.TryParse(args[2], NumberStyles.Number, CultureInfo.InvariantCulture, out factor))
                        result = OperationResult.Fail("Invalid factor: " + args[2]);
                    else if (Is(args[0], "price-preview"))
                        result = service.PreviewPriceFactor(save, args[1], factor, false);
                    else
                        result = service.ApplyPriceFactorOffline(save, args[1], factor);
                }
                else
                {
                    PrintUsage();
                    return 2;
                }

                return Print(result);
            }
            catch (Exception ex)
            {
                DiagnosticsService.Record(ex, "cli.main", DiagnosticCategory.Startup, DiagnosticSeverity.Error, "The command-line operation failed.", null);
                Console.Error.WriteLine("ERROR: " + ex);
                return 1;
            }
        }

        private static int Print(OperationResult result)
        {
            if (result != null && !result.Success)
                DiagnosticsService.RecordFailure(result, "cli.operation", DiagnosticCategory.Validation, "Code=" + (result.Code ?? "none"));
            Console.WriteLine((result.Success ? "OK: " : "ERROR: ") + result.Message);
            if (!string.IsNullOrEmpty(result.AppliedMode)) Console.WriteLine("Mode: " + result.AppliedMode);
            if (!string.IsNullOrEmpty(result.BackupPath)) Console.WriteLine("Backup: " + result.BackupPath);
            foreach (PriceChange change in result.PriceChanges)
                Console.WriteLine(string.Format("{0}: baseline={1} current={2} new={3}", change.ProductId, change.BaselinePrice, change.CurrentPrice, change.NewPrice));
            return result.Success ? 0 : 1;
        }

        private static bool Is(string actual, string expected)
        {
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Schedule I Control Center CLI");
            Console.WriteLine("  status");
            Console.WriteLine("  backup");
            Console.WriteLine("  validate");
            Console.WriteLine("  console-enable");
            Console.WriteLine("  property-own <code>");
            Console.WriteLine("  price-preview <Shrooms|Cocaine|Meth|Weed|All> <factor>");
            Console.WriteLine("  price-apply <Shrooms|Cocaine|Meth|Weed|All> <factor>");
        }
    }
}
