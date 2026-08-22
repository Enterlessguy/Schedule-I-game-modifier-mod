using System;
using System.IO;
using System.Linq;

namespace ScheduleIControlCenter
{
    internal static class SmokeTests
    {
        private static int Main(string[] args)
        {
            string temp = Path.Combine(Path.GetTempPath(), "ScheduleI-ControlCenter-Smoke-" + Guid.NewGuid().ToString("N"));
            try
            {
                if (args != null && args.Length == 2 && string.Equals(args[0], "--verify-package", StringComparison.OrdinalIgnoreCase))
                {
                    UpdateTests.VerifyReleasePackage(args[1]);
                    Console.WriteLine("PASS: complete release ZIP passed updater extraction, identity, version, and transactional-install validation.");
                    return 0;
                }
                DiagnosticsTests.RunAll();
                InventoryPagingTests.RunAll();
                UpdateTests.RunAll();
                Console.WriteLine("PASS: native-eight paging model, transaction rollback, save gate, sidecar validation, downgrade, and speed-independence tests.");
                GameEnvironment realEnvironment;
                try
                {
                    realEnvironment = GameEnvironment.Detect();
                }
                catch (DirectoryNotFoundException)
                {
                    Console.WriteLine("SKIP: save-file integration tests require a local Schedule I installation; portable regression tests passed.");
                    return 0;
                }
                SaveService realService = new SaveService(realEnvironment);
                SaveDescriptor source = realService.DiscoverSaves().FirstOrDefault();
                if (source == null)
                {
                    Console.WriteLine("SKIP: save-file integration tests require a local Schedule I save; portable regression tests passed.");
                    return 0;
                }

                string saveRoot = Path.Combine(temp, "Saves");
                string copiedSlot = Path.Combine(saveRoot, source.OwnerId, source.SlotName);
                CopyDirectory(source.FolderPath, copiedSlot);

                GameEnvironment testEnvironment = new GameEnvironment(realEnvironment.GameRoot, saveRoot, Path.Combine(temp, "Tool"), () => false);
                SaveService service = new SaveService(testEnvironment);
                SaveDescriptor save = service.DiscoverSaves().FirstOrDefault();
                Require(save != null, "Copied save was not discovered.");

                Require(service.ValidateSave(save).Success, "Initial validation failed.");

                OperationResult console = service.EnableConsoleOffline(save);
                Require(console.Success, "Console enable failed: " + console.Message);
                Require(service.DiscoverSaves().First().ConsoleEnabled, "Console flag was not persisted.");

                OperationResult preview = service.PreviewPriceFactor(save, "Shrooms", 2m, true);
                Require(preview.Success && preview.PriceChanges.Count > 0, "Shroom price preview failed.");
                Require(preview.PriceChanges.All(p => p.NewPrice == Math.Max(1, Math.Min(16777215, p.BaselinePrice * 2))), "Preview did not apply the baseline factor within the uncapped unit-price range.");

                OperationResult aboveVanillaCap = service.PreviewPriceFactor(save, "Shrooms", 1000m, true);
                Require(aboveVanillaCap.Success && aboveVanillaCap.PriceChanges.Any(p => p.NewPrice > 999), "Offline preview still clamps unit prices to the vanilla $999 maximum.");

                OperationResult apply = service.ApplyPriceFactorOffline(save, "Shrooms", 2m);
                Require(apply.Success, "Price apply failed: " + apply.Message);
                OperationResult after = service.PreviewPriceFactor(save, "Shrooms", 2m, false);
                Require(after.PriceChanges.All(p => p.CurrentPrice == p.NewPrice), "Applied prices do not match the planned baseline values.");

                PropertyState unowned = service.GetProperties(save).FirstOrDefault(p => !p.IsOwned);
                if (unowned != null)
                {
                    OperationResult own = service.OwnPropertyOffline(save, unowned.Code);
                    Require(own.Success, "Ownership apply failed: " + own.Message);
                    Require(service.GetProperties(save).First(p => p.Code == unowned.Code).IsOwned, "Ownership flag did not persist.");
                }

                Require(service.ValidateSave(save).Success, "Final validation failed.");
                Console.WriteLine("PASS: backup, console, price baseline/preview/apply, ownership, and JSON validation completed on an isolated copy.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: " + ex);
                return 1;
            }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
                catch { }
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(destination + directory.Substring(source.Length));
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = destination + file.Substring(source.Length);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, false);
            }
        }
    }
}
