using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ScheduleIControlCenter
{
    internal sealed class SaveService
    {
        private readonly GameEnvironment environment;
        private const int MinSafeUnitPrice = 1;
        private const int MaxSafeUnitPrice = 16777215;
        private const decimal MaxSafePriceFactor = 1000000m;

        public SaveService(GameEnvironment environment)
        {
            this.environment = environment;
        }

        public List<SaveDescriptor> DiscoverSaves()
        {
            List<SaveDescriptor> saves = new List<SaveDescriptor>();
            if (!Directory.Exists(environment.SaveRoot))
                return saves;

            string lastLoaded = FindLastLoadedSaveKey();
            foreach (string ownerFolder in Directory.GetDirectories(environment.SaveRoot))
            {
                string owner = Path.GetFileName(ownerFolder);
                foreach (string slotFolder in Directory.GetDirectories(ownerFolder, "SaveGame_*"))
                {
                    string metadataPath = Path.Combine(slotFolder, "Metadata.json");
                    string gamePath = Path.Combine(slotFolder, "Game.json");
                    if (!File.Exists(metadataPath) || !File.Exists(gamePath))
                        continue;

                    try
                    {
                        Dictionary<string, object> metadata = JsonUtil.ReadObject(metadataPath);
                        Dictionary<string, object> game = JsonUtil.ReadObject(gamePath);
                        Dictionary<string, object> settings = null;
                        object settingsValue;
                        if (game.TryGetValue("Settings", out settingsValue))
                            settings = JsonUtil.AsObject(settingsValue);

                        SaveDescriptor save = new SaveDescriptor
                        {
                            OwnerId = owner,
                            SlotName = Path.GetFileName(slotFolder),
                            FolderPath = slotFolder,
                            GameVersion = JsonUtil.GetString(metadata, "LastSaveVersion", JsonUtil.GetString(metadata, "GameVersion", "?")),
                            LastPlayed = ReadGameDate(metadata, "LastPlayedDate"),
                            LastWriteTime = Directory.GetLastWriteTime(slotFolder),
                            ConsoleEnabled = JsonUtil.GetBool(settings, "ConsoleEnabled", false)
                        };
                        save.IsLastLoaded = string.Equals(save.Key, lastLoaded, StringComparison.OrdinalIgnoreCase);
                        saves.Add(save);
                    }
                    catch
                    {
                        // Invalid save folders remain visible through Validate only after the JSON can be parsed.
                    }
                }
            }

            return saves
                .OrderByDescending(s => s.IsLastLoaded)
                .ThenByDescending(s => s.LastPlayed == DateTime.MinValue ? s.LastWriteTime : s.LastPlayed)
                .ToList();
        }

        public OperationResult ValidateSave(SaveDescriptor save)
        {
            try
            {
                int count = 0;
                foreach (string file in Directory.GetFiles(save.FolderPath, "*.json", SearchOption.AllDirectories))
                {
                    JsonUtil.ValidateFile(file);
                    count++;
                }
                return OperationResult.Ok(string.Format("Validated {0} JSON files. No parse errors found.", count));
            }
            catch (Exception ex)
            {
                return OperationResult.Fail("Validation failed: " + ex.Message, ex);
            }
        }

        public OperationResult CreateBackup(SaveDescriptor save, string reason)
        {
            try
            {
                string path = BackupInternal(save, reason);
                return new OperationResult
                {
                    Success = true,
                    Message = "Backup completed.",
                    BackupPath = path,
                    AppliedMode = "backup"
                };
            }
            catch (Exception ex)
            {
                return OperationResult.Fail("Backup failed: " + ex.Message, ex);
            }
        }

        public OperationResult EnableConsoleOffline(SaveDescriptor save)
        {
            try
            {
                AssertOfflineWriteAllowed();
                string gamePath = Path.Combine(save.FolderPath, "Game.json");
                Dictionary<string, object> game = JsonUtil.ReadObject(gamePath);
                Dictionary<string, object> settings;
                object settingsValue;
                if (!game.TryGetValue("Settings", out settingsValue) || (settings = JsonUtil.AsObject(settingsValue)) == null)
                {
                    settings = new Dictionary<string, object>();
                    game["Settings"] = settings;
                }

                if (JsonUtil.GetBool(settings, "ConsoleEnabled", false))
                    return OperationResult.Ok("The in-game console is already enabled for this save.");

                string backup = BackupInternal(save, "enable-console");
                settings["ConsoleEnabled"] = true;
                JsonUtil.WriteObjectAtomic(gamePath, game);
                JsonUtil.ValidateFile(gamePath);
                save.ConsoleEnabled = true;

                return new OperationResult
                {
                    Success = true,
                    Message = "In-game console enabled. Load or reload the save before using it.",
                    AppliedMode = "offline-save",
                    ReloadRequired = true,
                    BackupPath = backup
                };
            }
            catch (Exception ex)
            {
                return OperationResult.Fail("Console enable failed: " + ex.Message, ex);
            }
        }

        public List<PropertyState> GetProperties(SaveDescriptor save)
        {
            List<PropertyState> result = new List<PropertyState>();
            string[] folders = { "Properties", "Businesses" };
            foreach (string folderName in folders)
            {
                string folder = Path.Combine(save.FolderPath, folderName);
                if (!Directory.Exists(folder))
                    continue;

                foreach (string file in Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        Dictionary<string, object> data = JsonUtil.ReadObject(file);
                        string code = JsonUtil.GetString(data, "PropertyCode", string.Empty);
                        if (code.Length == 0)
                            continue;
                        result.Add(new PropertyState
                        {
                            Code = code,
                            IsOwned = JsonUtil.GetBool(data, "IsOwned", false),
                            RelativeFile = MakeRelative(save.FolderPath, file)
                        });
                    }
                    catch { }
                }
            }
            return result.OrderBy(p => p.Code).ToList();
        }

        public OperationResult OwnPropertyOffline(SaveDescriptor save, string code)
        {
            try
            {
                AssertOfflineWriteAllowed();
                PropertyState state = GetProperties(save).FirstOrDefault(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));
                if (state == null)
                    return OperationResult.Fail("Property/business code was not found in this save: " + code);
                if (state.IsOwned)
                    return OperationResult.Ok(code + " is already owned.");

                string backup = BackupInternal(save, "own-" + Sanitize(code));
                string path = Path.Combine(save.FolderPath, state.RelativeFile);
                Dictionary<string, object> data = JsonUtil.ReadObject(path);
                data["IsOwned"] = true;
                JsonUtil.WriteObjectAtomic(path, data);
                JsonUtil.ValidateFile(path);

                return new OperationResult
                {
                    Success = true,
                    Message = code + " set to owned in the save. Reload required. The live bridge is preferred for story-sensitive properties.",
                    AppliedMode = "offline-save",
                    ReloadRequired = true,
                    BackupPath = backup
                };
            }
            catch (Exception ex)
            {
                return OperationResult.Fail("Ownership change failed: " + ex.Message, ex);
            }
        }

        public OperationResult PreviewPriceFactor(SaveDescriptor save, string drugType, decimal factor, bool recaptureBaseline)
        {
            try
            {
                if (factor < 0.01m || factor > MaxSafePriceFactor)
                    return OperationResult.Fail("Factor must be between 0.01 and 1,000,000.");

                string productsPath = Path.Combine(save.FolderPath, "Products.json");
                Dictionary<string, object> products = JsonUtil.ReadObject(productsPath);
                Dictionary<string, int> current = ReadPrices(products);
                Dictionary<string, int> baseline = GetOrCapturePriceBaseline(save, current, recaptureBaseline);
                HashSet<string> targetIds = GetTargetProductIds(products, drugType);

                OperationResult result = OperationResult.Ok(string.Empty);
                foreach (KeyValuePair<string, int> pair in current.OrderBy(p => p.Key))
                {
                    if (!targetIds.Contains(pair.Key))
                        continue;

                    int basePrice;
                    if (!baseline.TryGetValue(pair.Key, out basePrice))
                    {
                        basePrice = pair.Value;
                        baseline[pair.Key] = pair.Value;
                    }

                    decimal scaled = decimal.Round(basePrice * factor, 0, MidpointRounding.AwayFromZero);
                    int newPrice = (int)Math.Max(MinSafeUnitPrice, Math.Min(MaxSafeUnitPrice, scaled));
                    result.PriceChanges.Add(new PriceChange
                    {
                        ProductId = pair.Key,
                        BaselinePrice = basePrice,
                        CurrentPrice = pair.Value,
                        NewPrice = newPrice
                    });
                }

                if (result.PriceChanges.Count == 0)
                    return OperationResult.Fail("No matching priced products were found for " + drugType + ".");

                SavePriceBaseline(save, baseline);
                result.Message = string.Format("Previewed {0} price changes using the captured baseline at {1}x.", result.PriceChanges.Count, factor);
                result.AppliedMode = "preview";
                return result;
            }
            catch (Exception ex)
            {
                return OperationResult.Fail("Price preview failed: " + ex.Message, ex);
            }
        }

        public OperationResult ApplyPriceFactorOffline(SaveDescriptor save, string drugType, decimal factor)
        {
            try
            {
                AssertOfflineWriteAllowed();
                OperationResult plan = PreviewPriceFactor(save, drugType, factor, false);
                if (!plan.Success)
                    return plan;

                string backup = BackupInternal(save, "prices-" + Sanitize(drugType) + "-x" + factor.ToString(CultureInfo.InvariantCulture));
                string productsPath = Path.Combine(save.FolderPath, "Products.json");
                Dictionary<string, object> products = JsonUtil.ReadObject(productsPath);
                Dictionary<string, int> changes = plan.PriceChanges.ToDictionary(p => p.ProductId, p => p.NewPrice, StringComparer.OrdinalIgnoreCase);

                object priceValue;
                if (!products.TryGetValue("ProductPrices", out priceValue))
                    return OperationResult.Fail("Products.json has no ProductPrices collection.");

                foreach (object item in JsonUtil.AsItems(priceValue))
                {
                    Dictionary<string, object> entry = JsonUtil.AsObject(item);
                    string id = JsonUtil.GetString(entry, "String", string.Empty);
                    int newPrice;
                    if (entry != null && changes.TryGetValue(id, out newPrice))
                        entry["Int"] = newPrice;
                }

                JsonUtil.WriteObjectAtomic(productsPath, products);
                JsonUtil.ValidateFile(productsPath);
                plan.Message = string.Format("Applied {0} offline price changes. Reload the save to use them.", plan.PriceChanges.Count);
                plan.AppliedMode = "offline-save";
                plan.ReloadRequired = true;
                plan.BackupPath = backup;
                return plan;
            }
            catch (Exception ex)
            {
                return OperationResult.Fail("Price apply failed: " + ex.Message, ex);
            }
        }

        private void AssertOfflineWriteAllowed()
        {
            if (environment.IsGameRunning())
                throw new InvalidOperationException("Schedule I is running. Offline save writes are blocked; use the live bridge or close the game.");
        }

        private string BackupInternal(SaveDescriptor save, string reason)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            string destination = Path.Combine(environment.ToolRoot, "Backups", save.OwnerId, save.SlotName, stamp + "_" + Sanitize(reason));
            Directory.CreateDirectory(destination);
            CopyDirectory(save.FolderPath, destination);

            Dictionary<string, object> manifest = new Dictionary<string, object>
            {
                { "CreatedUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) },
                { "SourceOwner", save.OwnerId },
                { "SourceSlot", save.SlotName },
                { "SourceGameVersion", save.GameVersion },
                { "Reason", reason }
            };
            JsonUtil.WriteObjectAtomic(Path.Combine(destination, "controlcenter-backup.json"), manifest);
            return destination;
        }

        private static void CopyDirectory(string source, string destination)
        {
            foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(destination + directory.Substring(source.Length));

            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = destination + file.Substring(source.Length);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, false);
            }
        }

        private Dictionary<string, int> GetOrCapturePriceBaseline(SaveDescriptor save, Dictionary<string, int> current, bool force)
        {
            string path = GetBaselinePath(save);
            if (!force && File.Exists(path))
            {
                Dictionary<string, object> data = JsonUtil.ReadObject(path);
                Dictionary<string, int> loaded = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                object prices;
                if (data.TryGetValue("Prices", out prices))
                {
                    Dictionary<string, object> obj = JsonUtil.AsObject(prices);
                    if (obj != null)
                    {
                        foreach (KeyValuePair<string, object> pair in obj)
                        {
                            try { loaded[pair.Key] = Convert.ToInt32(pair.Value, CultureInfo.InvariantCulture); }
                            catch { }
                        }
                    }
                }
                foreach (KeyValuePair<string, int> pair in current)
                    if (!loaded.ContainsKey(pair.Key)) loaded[pair.Key] = pair.Value;
                return loaded;
            }

            Dictionary<string, int> baseline = new Dictionary<string, int>(current, StringComparer.OrdinalIgnoreCase);
            SavePriceBaseline(save, baseline);
            return baseline;
        }

        private void SavePriceBaseline(SaveDescriptor save, Dictionary<string, int> baseline)
        {
            string path = GetBaselinePath(save);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Dictionary<string, object> prices = baseline.ToDictionary(p => p.Key, p => (object)p.Value, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, object> root = new Dictionary<string, object>
            {
                { "CapturedUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) },
                { "Owner", save.OwnerId },
                { "Slot", save.SlotName },
                { "Prices", prices }
            };
            JsonUtil.WriteObjectAtomic(path, root);
        }

        private string GetBaselinePath(SaveDescriptor save)
        {
            return Path.Combine(environment.ToolRoot, "Data", "Baselines", Sanitize(save.OwnerId + "_" + save.SlotName) + "_prices.json");
        }

        private static Dictionary<string, int> ReadPrices(Dictionary<string, object> products)
        {
            Dictionary<string, int> prices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            object priceValue;
            if (!products.TryGetValue("ProductPrices", out priceValue))
                return prices;

            foreach (object item in JsonUtil.AsItems(priceValue))
            {
                Dictionary<string, object> entry = JsonUtil.AsObject(item);
                string id = JsonUtil.GetString(entry, "String", string.Empty);
                if (id.Length > 0)
                    prices[id] = JsonUtil.GetInt(entry, "Int", 0);
            }
            return prices;
        }

        private static HashSet<string> GetTargetProductIds(Dictionary<string, object> products, string drugType)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string normalized = (drugType ?? string.Empty).Trim().ToLowerInvariant();

            if (normalized == "weed" || normalized == "all")
            {
                ids.UnionWith(new[] { "ogkush", "sourdiesel", "greencrack", "granddaddypurple" });
                AddCreatedIds(products, "CreatedWeed", ids);
            }
            if (normalized == "meth" || normalized == "all")
            {
                ids.Add("meth");
                AddCreatedIds(products, "CreatedMeth", ids);
            }
            if (normalized == "cocaine" || normalized == "all")
            {
                ids.Add("cocaine");
                AddCreatedIds(products, "CreatedCocaine", ids);
            }
            if (normalized == "shrooms" || normalized == "shroom" || normalized == "all")
            {
                ids.Add("shroom");
                AddCreatedIds(products, "CreatedShrooms", ids);
            }
            return ids;
        }

        private static void AddCreatedIds(Dictionary<string, object> products, string key, HashSet<string> ids)
        {
            object value;
            if (!products.TryGetValue(key, out value))
                return;
            foreach (object item in JsonUtil.AsItems(value))
            {
                Dictionary<string, object> product = JsonUtil.AsObject(item);
                string id = JsonUtil.GetString(product, "ID", string.Empty);
                if (id.Length > 0)
                    ids.Add(id);
            }
        }

        private string FindLastLoadedSaveKey()
        {
            try
            {
                string playerLog = Path.Combine(Directory.GetParent(environment.SaveRoot).FullName, "Player.log");
                if (!File.Exists(playerLog))
                    return null;
                string text = File.ReadAllText(playerLog);
                MatchCollection matches = Regex.Matches(text, @"Saves[\\/](?<owner>\d+)[\\/](?<slot>SaveGame_\d+)", RegexOptions.IgnoreCase);
                if (matches.Count == 0)
                    return null;
                Match match = matches[matches.Count - 1];
                return match.Groups["owner"].Value + "\\" + match.Groups["slot"].Value;
            }
            catch { return null; }
        }

        private static DateTime ReadGameDate(Dictionary<string, object> metadata, string key)
        {
            object value;
            Dictionary<string, object> date;
            if (!metadata.TryGetValue(key, out value) || (date = JsonUtil.AsObject(value)) == null)
                return DateTime.MinValue;
            try
            {
                return new DateTime(
                    JsonUtil.GetInt(date, "Year", 1),
                    JsonUtil.GetInt(date, "Month", 1),
                    JsonUtil.GetInt(date, "Day", 1),
                    JsonUtil.GetInt(date, "Hour", 0),
                    JsonUtil.GetInt(date, "Minute", 0),
                    JsonUtil.GetInt(date, "Second", 0));
            }
            catch { return DateTime.MinValue; }
        }

        private static string MakeRelative(string root, string path)
        {
            if (!root.EndsWith(Path.DirectorySeparatorChar.ToString()))
                root += Path.DirectorySeparatorChar;
            Uri rootUri = new Uri(root);
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(new Uri(path)).ToString()).Replace('/', Path.DirectorySeparatorChar);
        }

        private static string Sanitize(string value)
        {
            string result = value ?? "operation";
            foreach (char c in Path.GetInvalidFileNameChars())
                result = result.Replace(c, '_');
            return result.Replace(' ', '-').ToLowerInvariant();
        }
    }
}
