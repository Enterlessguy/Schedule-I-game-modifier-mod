using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using Il2CppFishNet;
using Il2CppFishNet.Connection;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Property;
using MelonLoader.Utils;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScheduleIControlBridge
{
    internal sealed class GameOperations
    {
        public const string ModVersion = "1.3.0";
        public const string ExpectedGameVersion = "0.4.6f13";
        public const string ExpectedGameBuild = "24705572";
        private const double PriceTolerance = 0.001;

        private readonly BuildFingerprint fingerprint;
        private readonly Action<string> audit;
        private readonly Action<string> warn;
        private readonly Func<bool> compatibilityModeEnabled;
        private readonly Func<bool> enableCompatibilityMode;
        private readonly CompatibilityDiagnosticsResult compatibilityDiagnostics;
        private readonly Dictionary<string, PricePreview> previews = new Dictionary<string, PricePreview>(StringComparer.Ordinal);
        private readonly Dictionary<string, PriceLimitPreview> priceLimitPreviews = new Dictionary<string, PriceLimitPreview>(StringComparer.Ordinal);
        private readonly Dictionary<string, MarketPreview> marketPreviews = new Dictionary<string, MarketPreview>(StringComparer.Ordinal);
        private readonly Dictionary<string, AllowancePreview> allowancePreviews = new Dictionary<string, AllowancePreview>(StringComparer.Ordinal);
        private readonly Dictionary<string, PlayerSettingsPreview> playerSettingsPreviews = new Dictionary<string, PlayerSettingsPreview>(StringComparer.Ordinal);
        private long revision = 1;

        public GameOperations(
            BuildFingerprint fingerprint,
            Action<string> audit,
            Action<string> warn,
            Func<bool> compatibilityModeEnabled,
            Func<bool> enableCompatibilityMode,
            CompatibilityDiagnosticsResult compatibilityDiagnostics)
        {
            this.fingerprint = fingerprint;
            this.audit = audit;
            this.warn = warn;
            this.compatibilityModeEnabled = compatibilityModeEnabled;
            this.enableCompatibilityMode = enableCompatibilityMode;
            this.compatibilityDiagnostics = compatibilityDiagnostics;
        }

        public long Revision { get { return revision; } }

        public void Tick()
        {
            RuntimeState state = ReadRuntimeState();
            ProductManager manager;
            string error;
            bool hasManager = TryGetProductManager(false, out manager, out error);
            bool commonEligible = IsKnownBuild()
                && state.SaveLoaded
                && !string.IsNullOrEmpty(state.SavePath)
                && state.IsHost
                && state.IsServer
                && state.RemoteClientCountKnown
                && state.RemoteClientCount == 0;

            SellPriceLimitManager.SetEligibility(commonEligible);
            PlayerRuntimeSettings.SetEligibility(commonEligible);
            if (commonEligible)
                SellPriceLimitManager.EnsureApplied(out error);

            if (!commonEligible)
            {
                if (MarketValueScaling.EligibilityActive || MarketValueScaling.ActiveOverrideCount > 0)
                    MarketValueScaling.Deactivate(hasManager ? manager : null);
                if (CustomerAllowanceScaling.EligibilityActive
                    || CustomerAllowanceScaling.ActiveOverrideCount > 0
                    || CustomerAllowanceScaling.ActiveSaveScope.Length > 0)
                    CustomerAllowanceScaling.Deactivate();
                BusinessLaunderScaling.ClearManagedState();
                EffectsIntensityManager.ClearManagedState();
                return;
            }

            if (!hasManager)
            {
                if (MarketValueScaling.EligibilityActive || MarketValueScaling.ActiveOverrideCount > 0)
                    MarketValueScaling.Deactivate(null);
            }
            else if (!MarketValueScaling.IsActiveFor(state.SavePath, manager))
            {
                if (MarketValueScaling.ActiveOverrideCount > 0
                    || MarketValueScaling.ActiveSaveScope.Length > 0)
                    MarketValueScaling.Deactivate(manager);
                if (state.SaveReady)
                {
                    MarketValueScaling.SetEligibility(true);
                    MarketValueScaling.EnsureSave(state.SavePath, manager, out error);
                }
            }

            if (!CustomerAllowanceScaling.IsActiveFor(state.SavePath))
            {
                if (CustomerAllowanceScaling.ActiveOverrideCount > 0
                    || CustomerAllowanceScaling.ActiveSaveScope.Length > 0)
                    CustomerAllowanceScaling.Deactivate();
                if (state.SaveReady)
                {
                    CustomerAllowanceScaling.SetEligibility(true);
                    CustomerAllowanceScaling.EnsureSave(state.SavePath, out error);
                }
            }

            if (state.SaveReady)
            {
                BusinessLaunderScaling.SetEligibility(true);
                BusinessLaunderScaling.EnsureSave(state.SavePath, out error);
                EffectsIntensityManager.SetEligibility(true);
                EffectsIntensityManager.EnsureSave(state.SavePath, out error);
            }
        }

        public void RestoreLiveOverrides()
        {
            try
            {
                ProductManager manager;
                string error;
                if (TryGetProductManager(false, out manager, out error))
                    MarketValueScaling.RestoreCurrent(manager);
            }
            catch (Exception ex)
            {
                warn("Could not restore fair-market values during bridge shutdown: " + ex.Message);
            }

            try
            {
                CustomerAllowanceScaling.Deactivate();
            }
            catch (Exception ex)
            {
                warn("Could not deactivate customer-allowance overrides during bridge shutdown: " + ex.Message);
            }

            SellPriceLimitManager.RestoreReviewedDefault();
            SellPriceLimitManager.ClearManagedState();
            PlayerRuntimeSettings.RestoreLiveOverrides();
            PlayerRuntimeSettings.ClearManagedState();
        }

        public string Handle(BridgeRequest request)
        {
            switch (request.Operation)
            {
                case "system.status":
                    return Status(request);
                case "system.compatibility.enable":
                    return EnableCompatibility(request);
                case "game.save":
                    return Save(request);
                case "product.price.list":
                    return ListPrices(request);
                case "product.price.previewScale":
                    return PreviewPrices(request);
                case "product.price.applyPreview":
                    return ApplyPricePreview(request);
                case "sale.dealLimit.get":
                    return GetPriceLimit(request);
                case "sale.dealLimit.preview":
                    return PreviewPriceLimit(request);
                case "sale.dealLimit.applyPreview":
                    return ApplyPriceLimitPreview(request);
                case "product.market.list":
                    return ListMarketValues(request);
                case "product.market.previewSync":
                    return PreviewMarketValues(request);
                case "product.market.applyPreview":
                    return ApplyMarketPreview(request);
                case "customer.allowance.list":
                    return ListCustomerAllowances(request);
                case "customer.allowance.preview":
                    return PreviewCustomerAllowances(request);
                case "customer.allowance.applyPreview":
                    return ApplyCustomerAllowancePreview(request);
                case "business.launder.list":
                    return ListLaunderLimits(request);
                case "business.launder.preview":
                    return PreviewLaunderLimits(request);
                case "business.launder.applyPreview":
                    return ApplyLaunderPreview(request);
                case "effects.list":
                    return ListEffects(request);
                case "effects.preview":
                    return PreviewEffects(request);
                case "effects.applyPreview":
                    return ApplyEffectPreview(request);
                case "player.settings.get":
                    return GetPlayerSettings(request);
                case "player.settings.preview":
                    return PreviewPlayerSettings(request);
                case "player.settings.applyPreview":
                    return ApplyPlayerSettings(request);
                case "property.own":
                    return OwnProperty(request);
                default:
                    return Fail(request, "operation_not_allowed", "The requested operation is not allowlisted.");
            }
        }

        private string EnableCompatibility(BridgeRequest request)
        {
            if (IsReviewedBuild())
                return Ok(request, "The reviewed build is already enabled; compatibility mode is unnecessary.", new JObject
                {
                    ["compatibilityModeEnabled"] = false,
                    ["reviewedBuild"] = true
                });

            if (compatibilityModeEnabled())
                return Ok(request, "Compatibility mode is already enabled.", new JObject
                {
                    ["compatibilityModeEnabled"] = true,
                    ["reviewedBuild"] = false,
                    ["diagnostics"] = compatibilityDiagnostics.ToJson()
                });

            if (compatibilityDiagnostics == null || !compatibilityDiagnostics.Passed)
                return Fail(request, "compatibility_diagnostics_failed", "Compatibility mode is unavailable because the bridge diagnostics did not pass.", compatibilityDiagnostics == null ? null : compatibilityDiagnostics.ToJson());

            if (!request.Arguments.Value<bool?>("confirm").GetValueOrDefault())
                return Fail(request, "explicit_confirmation_required", "Compatibility mode requires an explicit user confirmation.", compatibilityDiagnostics.ToJson());

            if (request.DryRun)
                return Ok(request, "Compatibility diagnostics passed; no patches were enabled because dryRun is true.", new JObject
                {
                    ["wouldEnableCompatibilityMode"] = true,
                    ["diagnostics"] = compatibilityDiagnostics.ToJson()
                });

            if (!enableCompatibilityMode())
                return Fail(request, "compatibility_enable_failed", "Compatibility mode could not enable every reviewed patch family.", compatibilityDiagnostics.ToJson());

            revision++;
            audit("op=system.compatibility.enable revision=" + revision.ToString(CultureInfo.InvariantCulture));
            return Ok(request, "Compatibility mode enabled after explicit confirmation.", new JObject
            {
                ["compatibilityModeEnabled"] = true,
                ["reviewedBuild"] = false,
                ["diagnostics"] = compatibilityDiagnostics.ToJson()
            });
        }

        private string Status(BridgeRequest request)
        {
            RuntimeState state = ReadRuntimeState();
            bool knownBuild = IsReviewedBuild();
            bool operationalBuild = IsKnownBuild();
            JArray capabilities = new JArray(
                "system.status",
                "game.save",
                "product.price.list",
                "product.price.previewScale",
                "product.price.applyPreview",
                "property.own");
            if (SellPriceLimitManager.PatchActive && SellPriceLimitManager.PersistenceReady)
            {
                capabilities.Add("sale.dealLimit.get");
                capabilities.Add("sale.dealLimit.preview");
                capabilities.Add("sale.dealLimit.applyPreview");
            }
            if (MarketValueScaling.PatchActive && MarketValueScaling.PersistenceReady)
            {
                capabilities.Add("product.market.list");
                capabilities.Add("product.market.previewSync");
                capabilities.Add("product.market.applyPreview");
            }
            if (CustomerAllowanceScaling.PatchActive && CustomerAllowanceScaling.PersistenceReady)
            {
                capabilities.Add("customer.allowance.list");
                capabilities.Add("customer.allowance.preview");
                capabilities.Add("customer.allowance.applyPreview");
            }
            if (BusinessLaunderScaling.PatchActive && BusinessLaunderScaling.PersistenceReady)
            {
                capabilities.Add("business.launder.list");
                capabilities.Add("business.launder.preview");
                capabilities.Add("business.launder.applyPreview");
            }
            if (operationalBuild && EffectsIntensityManager.PersistenceReady)
            {
                capabilities.Add("effects.list");
                capabilities.Add("effects.preview");
                capabilities.Add("effects.applyPreview");
            }
            if (operationalBuild && PlayerRuntimeSettings.PatchActive && PlayerRuntimeSettings.PersistenceReady)
            {
                capabilities.Add("player.settings.get");
                capabilities.Add("player.settings.preview");
                capabilities.Add("player.settings.applyPreview");
            }

            PlayerSettingsSnapshot playerSettings = PlayerRuntimeSettings.ReadSnapshot();
            JObject data = new JObject
            {
                ["protocolVersion"] = 1,
                ["modVersion"] = ModVersion,
                ["gameVersion"] = Application.version ?? string.Empty,
                ["gameBuild"] = knownBuild ? ExpectedGameBuild : string.Empty,
                ["expectedGameBuild"] = ExpectedGameBuild,
                ["knownBuild"] = knownBuild,
                ["sceneName"] = SceneManager.GetActiveScene().name ?? string.Empty,
                ["saveLoaded"] = state.SaveLoaded,
                ["saveReady"] = state.SaveReady,
                ["savePath"] = state.SavePath,
                ["isHost"] = state.IsHost,
                ["isServer"] = state.IsServer,
                ["isClient"] = state.IsClient,
                ["mutationsAllowed"] = operationalBuild && state.SaveReady && state.IsServer,
                ["compatibilityModeEnabled"] = compatibilityModeEnabled(),
                ["compatibilityModeAvailable"] = !knownBuild && compatibilityDiagnostics != null && compatibilityDiagnostics.Passed,
                ["compatibilityDiagnosticsPassed"] = compatibilityDiagnostics != null && compatibilityDiagnostics.Passed,
                ["compatibilityDiagnostics"] = compatibilityDiagnostics == null ? null : compatibilityDiagnostics.ToJson(),
                ["remoteClientCountKnown"] = state.RemoteClientCountKnown,
                ["remoteClientCount"] = state.RemoteClientCount,
                ["isSoloHost"] = state.IsHost && state.IsServer && state.RemoteClientCountKnown && state.RemoteClientCount == 0,
                ["marketPatchActive"] = MarketValueScaling.PatchActive,
                ["marketPersistenceReady"] = MarketValueScaling.PersistenceReady,
                ["marketConfigRevision"] = MarketValueScaling.ConfigRevision,
                ["activeMarketOverrides"] = MarketValueScaling.ActiveOverrideCount,
                ["marketSaveScope"] = MarketValueScaling.ActiveSaveScope,
                ["allowancePatchActive"] = CustomerAllowanceScaling.PatchActive,
                ["allowancePersistenceReady"] = CustomerAllowanceScaling.PersistenceReady,
                ["allowanceConfigRevision"] = CustomerAllowanceScaling.ConfigRevision,
                ["activeAllowanceOverrides"] = CustomerAllowanceScaling.ActiveOverrideCount,
                ["allowanceSaveScope"] = CustomerAllowanceScaling.ActiveSaveScope,
                ["launderPatchActive"] = BusinessLaunderScaling.PatchActive,
                ["launderPersistenceReady"] = BusinessLaunderScaling.PersistenceReady,
                ["launderConfigRevision"] = BusinessLaunderScaling.ConfigRevision,
                ["activeLaunderOverrides"] = BusinessLaunderScaling.ActiveOverrideCount,
                ["launderSaveScope"] = BusinessLaunderScaling.ActiveSaveScope,
                ["effectsPersistenceReady"] = EffectsIntensityManager.PersistenceReady,
                ["effectsConfigRevision"] = EffectsIntensityManager.ConfigRevision,
                ["activeEffectOverrides"] = EffectsIntensityManager.ActiveOverrideCount,
                ["effectsSaveScope"] = EffectsIntensityManager.ActiveSaveScope,
                ["sellPriceLimitPersistenceReady"] = SellPriceLimitManager.PersistenceReady,
                ["sellPriceLimitPatchActive"] = SellPriceLimitManager.PatchActive,
                ["sellPriceLimitConfigRevision"] = SellPriceLimitManager.ConfigRevision,
                ["sellPriceLimitOverrideEnabled"] = SellPriceLimitManager.DealTotalOverrideEnabled,
                ["sellPriceLimitOverrideApplied"] = SellPriceLimitManager.OverrideApplied,
                ["configuredMaxDealTotal"] = SellPriceLimitManager.ConfiguredDealTotalMax,
                ["currentCounterofferMax"] = SellPriceLimitManager.CurrentCounterofferMax,
                ["currentHandoverMax"] = SellPriceLimitManager.CurrentHandoverMax,
                ["reviewedDefaultMaxDealTotal"] = SellPriceLimitManager.ReviewedDefaultDealTotalMax,
                ["currentUnitPriceMin"] = SellPriceLimitManager.CurrentUnitPriceMin,
                ["currentUnitPriceMax"] = SellPriceLimitManager.CurrentUnitPriceMax,
                ["playerSettingsPatchActive"] = PlayerRuntimeSettings.PatchActive,
                ["playerSettingsPersistenceReady"] = PlayerRuntimeSettings.PersistenceReady,
                ["playerSettingsConfigRevision"] = PlayerRuntimeSettings.ConfigRevision,
                 ["configuredInventoryMode"] = playerSettings.ConfiguredInventoryMode,
                 ["configuredSpeedMultiplier"] = playerSettings.ConfiguredSpeedMultiplier,
                 ["baseInventorySlots"] = playerSettings.BaseInventorySlots,
                 ["inventoryReady"] = playerSettings.InventoryReady,
                 ["nativeHotbarSlots"] = playerSettings.NativeHotbarSlots,
                 ["inventorySlotCount"] = playerSettings.InventorySlotCount,
                 ["inventoryPage"] = playerSettings.InventoryPage,
                 ["currentPage"] = playerSettings.CurrentPage,
                 ["inventoryPageCount"] = playerSettings.InventoryPageCount,
                 ["configuredPageCount"] = playerSettings.ConfiguredPageCount,
                 ["allocatedPageCount"] = playerSettings.AllocatedPageCount,
                 ["inventorySaveScope"] = playerSettings.SaveScope,
                 ["inventorySidecarLoaded"] = playerSettings.SidecarLoaded,
                 ["lastInventoryError"] = playerSettings.LastInventoryError,
                 ["playerSpeedMultiplier"] = playerSettings.PlayerSpeedMultiplier,
                ["capabilities"] = capabilities,
                ["buildHashes"] = fingerprint.ToJson()
            };
            return Ok(request, "ready", data);
        }

        private string Save(BridgeRequest request)
        {
            RuntimeState state;
            string gate = RequireMutationState(true, out state);
            if (gate != null)
                return Fail(request, "save_not_ready", gate);
            if (!SaveManager.InstanceExists || SaveManager.Instance == null)
                return Fail(request, "save_manager_unavailable", "SaveManager is not available.");

            SaveManager manager = SaveManager.Instance;
            if (manager.IsSaving)
                return Fail(request, "save_in_progress", "The game is already saving.");

            if (request.DryRun)
                return Ok(request, "Save preconditions passed; no save was started because dryRun is true.", new JObject { ["wouldSave"] = true });

            manager.Save();
            revision++;
            audit(string.Format(CultureInfo.InvariantCulture, "op=game.save revision={0} savePath={1}", revision, state.SavePath));
            return Ok(request, "Game save started.", new JObject { ["saveStarted"] = true });
        }

        private string ListPrices(BridgeRequest request)
        {
            if (!IsKnownBuild())
                return Fail(request, "unknown_build", "Product access is disabled for this game build.");

            RuntimeState state = ReadRuntimeState();
            if (!state.SaveLoaded)
                return Fail(request, "save_not_loaded", "Load a save before listing live products.");

            ProductManager manager;
            string error;
            if (!TryGetProductManager(false, out manager, out error))
                return Fail(request, "product_manager_unavailable", error);

            ProductFilter filter;
            if (!TryReadProductFilter(request.Arguments, out filter, out error))
                return Fail(request, "invalid_args", error);

            List<LiveProduct> products = ReadProducts(manager, filter);
            JArray items = new JArray();
            foreach (LiveProduct product in products)
            {
                JObject row = product.ToJson();
                float fairValue = product.Definition == null ? 0f : product.Definition.MarketValue;
                row["fairMarketValue"] = fairValue;
                row["valueProposition"] = product.Definition == null ? 0f : ReadValueProposition(product.Definition, product.Price);
                row["alignedWithFairMarket"] = Math.Abs(fairValue - product.Price) <= PriceTolerance;
                items.Add(row);
            }

            return Ok(request, string.Format(CultureInfo.InvariantCulture, "Listed {0} live products.", products.Count), new JObject
            {
                ["count"] = products.Count,
                ["minPrice"] = SellPriceLimitManager.CurrentUnitPriceMin,
                ["maxPrice"] = SellPriceLimitManager.CurrentUnitPriceMax,
                ["products"] = items
            });
        }

        private string PreviewPrices(BridgeRequest request)
        {
            if (!IsKnownBuild())
                return Fail(request, "unknown_build", "Price previews are disabled for this game build.");

            RuntimeState state = ReadRuntimeState();
            if (!state.SaveLoaded)
                return Fail(request, "save_not_loaded", "Load a save before previewing live prices.");

            string mode = (request.Arguments.Value<string>("mode") ?? "currentFactor").Trim();
            bool explicitValues = string.Equals(mode, "explicitValues", StringComparison.OrdinalIgnoreCase);
            bool currentFactor = string.Equals(mode, "currentFactor", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "scale", StringComparison.OrdinalIgnoreCase);
            if (!explicitValues && !currentFactor)
                return Fail(request, "invalid_mode", "mode must be currentFactor or explicitValues.");

            double? factorValue = request.Arguments.Value<double?>("factor");
            if (currentFactor && (!factorValue.HasValue || double.IsNaN(factorValue.Value) || double.IsInfinity(factorValue.Value) || factorValue.Value < 0.01 || factorValue.Value > 1000000.0))
                return Fail(request, "invalid_factor", "currentFactor mode requires a finite factor between 0.01 and 1,000,000.");

            string error;
            ProductFilter filter = null;
            Dictionary<string, float> explicitTargets = null;
            if (explicitValues)
            {
                if (!TryReadManualPriceTargets(request.Arguments, out explicitTargets, out error))
                    return Fail(request, "invalid_args", error);
            }
            else
            {
                if (!TryReadProductFilter(request.Arguments, out filter, out error))
                    return Fail(request, "invalid_args", error);
            }

            ProductManager manager;
            if (!TryGetProductManager(false, out manager, out error))
                return Fail(request, "product_manager_unavailable", error);

            List<LiveProduct> products;
            if (explicitValues)
            {
                Dictionary<string, LiveProduct> map = ReadProductMap(manager);
                products = new List<LiveProduct>();
                foreach (KeyValuePair<string, float> target in explicitTargets)
                {
                    LiveProduct product;
                    if (!map.TryGetValue(target.Key, out product))
                        return Fail(request, "product_changed", "A requested product is not available: " + target.Key);
                    products.Add(product);
                }
                products.Sort((left, right) => string.Compare(left.Id, right.Id, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                products = ReadProducts(manager, filter);
            }
            if (products.Count == 0)
                return Fail(request, "no_matching_products", "No matching live products were found.");

            float minPrice = SellPriceLimitManager.CurrentUnitPriceMin;
            float maxPrice = SellPriceLimitManager.CurrentUnitPriceMax;
            if (!AreValidPriceBounds(minPrice, maxPrice))
                return Fail(request, "invalid_price_bounds", "ProductManager returned invalid price bounds; price mutation is disabled.");

            PricePreview preview = new PricePreview
            {
                Id = Guid.NewGuid().ToString("N"),
                Revision = revision,
                ExpiresUtc = DateTime.UtcNow.AddSeconds(60),
                Mode = explicitValues ? "explicitValues" : "currentFactor",
                Factor = currentFactor ? factorValue.Value : 0.0,
                MinPrice = minPrice,
                MaxPrice = maxPrice
            };

            foreach (LiveProduct product in products)
            {
                if (!IsFinite(product.Price) || product.Price < minPrice || product.Price > maxPrice)
                    return Fail(request, "invalid_current_price", "A matching product has a non-finite or out-of-bounds current price: " + product.Id);
                float next = explicitValues
                    ? explicitTargets[product.Id]
                    : (float)Math.Round(product.Price * factorValue.Value, 0, MidpointRounding.AwayFromZero);
                if (!IsFinite(next))
                    return Fail(request, "invalid_new_price", "The requested plan produced a non-finite price for: " + product.Id);
                if (explicitValues && (next < minPrice || next > maxPrice))
                    return Fail(request, "invalid_new_price", string.Format(CultureInfo.InvariantCulture, "The requested sell price for {0} is outside the active {1}-{2} bounds.", product.Id, minPrice, maxPrice));
                if (!explicitValues)
                    next = Math.Max(minPrice, Math.Min(maxPrice, next));
                preview.Changes.Add(new PriceChange
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    DrugType = product.DrugType,
                    ExpectedOldPrice = product.Price,
                    NewPrice = next
                });
            }

            PurgeExpiredPreviews();
            if (previews.Count >= 32)
                RemoveOldestPreview();
            previews[preview.Id] = preview;
            audit(string.Format(CultureInfo.InvariantCulture, "op=product.price.previewScale previewId={0} revision={1} count={2} mode={3} factor={4}", preview.Id, preview.Revision, preview.Changes.Count, preview.Mode, preview.Factor));

            return Ok(request, string.Format(CultureInfo.InvariantCulture, "Previewed {0} live price changes.", preview.Changes.Count), preview.ToJson());
        }

        private string ApplyPricePreview(BridgeRequest request)
        {
            RuntimeState state;
            string gate = RequireMutationState(false, out state);
            if (gate != null)
                return Fail(request, "mutation_not_ready", gate);

            string previewId = request.Arguments.Value<string>("previewId") ?? string.Empty;
            long? expectedRevision = request.Arguments.Value<long?>("expectedRevision");
            if (!PipeServer.IsSafeIdentifier(previewId, 64) || !expectedRevision.HasValue)
                return Fail(request, "invalid_args", "previewId and expectedRevision are required.");

            PurgeExpiredPreviews();
            PricePreview preview;
            if (!previews.TryGetValue(previewId, out preview))
                return Fail(request, "preview_not_found", "The preview was not found or has expired.");
            if (expectedRevision.Value != revision || preview.Revision != revision)
                return Fail(request, "revision_conflict", "Live state changed after the preview. Create a new preview.");

            ProductManager manager;
            string error;
            if (!TryGetProductManager(true, out manager, out error))
                return Fail(request, "product_manager_unavailable", error);

            Dictionary<string, LiveProduct> current = ReadProductMap(manager);
            float minPrice = SellPriceLimitManager.CurrentUnitPriceMin;
            float maxPrice = SellPriceLimitManager.CurrentUnitPriceMax;
            if (!AreValidPriceBounds(minPrice, maxPrice)
                || Math.Abs(minPrice - preview.MinPrice) > PriceTolerance
                || Math.Abs(maxPrice - preview.MaxPrice) > PriceTolerance)
                return Fail(request, "price_bounds_conflict", "ProductManager price bounds changed after the preview. Create a new preview.");
            foreach (PriceChange change in preview.Changes)
            {
                LiveProduct product;
                if (!current.TryGetValue(change.ProductId, out product))
                    return Fail(request, "product_changed", "A previewed product is no longer available: " + change.ProductId);
                if (!IsFinite(product.Price)
                    || !IsFinite(change.ExpectedOldPrice)
                    || !IsFinite(change.NewPrice)
                    || product.Price < minPrice
                    || product.Price > maxPrice
                    || change.ExpectedOldPrice < minPrice
                    || change.ExpectedOldPrice > maxPrice
                    || change.NewPrice < minPrice
                    || change.NewPrice > maxPrice)
                    return Fail(request, "invalid_price_state", "A previewed price became non-finite or out of bounds: " + change.ProductId);
                if (Math.Abs(product.Price - change.ExpectedOldPrice) > PriceTolerance)
                    return Fail(request, "price_conflict", string.Format(CultureInfo.InvariantCulture, "Price changed for {0}: expected {1}, found {2}.", change.ProductId, change.ExpectedOldPrice, product.Price));
            }

            if (request.DryRun)
                return Ok(request, "Preview apply preconditions passed; no prices were changed because dryRun is true.", new JObject
                {
                    ["wouldApply"] = preview.Changes.Count,
                    ["previewId"] = preview.Id
                });

            int submitted = 0;
            try
            {
                foreach (PriceChange change in preview.Changes)
                {
                    manager.SendPrice(change.ProductId, change.NewPrice);
                    submitted++;
                }
            }
            catch (Exception ex)
            {
                int rollbackSubmitted = 0;
                for (int i = submitted - 1; i >= 0; i--)
                {
                    try
                    {
                        PriceChange prior = preview.Changes[i];
                        manager.SendPrice(prior.ProductId, prior.ExpectedOldPrice);
                        rollbackSubmitted++;
                    }
                    catch
                    {
                        break;
                    }
                }

                previews.Clear();
                priceLimitPreviews.Clear();
                marketPreviews.Clear();
                allowancePreviews.Clear();
                revision++;
                audit(string.Format(CultureInfo.InvariantCulture, "op=product.price.applyPreview outcome=partial_failure previewId={0} revision={1} submitted={2} rollbackSubmitted={3} total={4} exception={5}", preview.Id, revision, submitted, rollbackSubmitted, preview.Changes.Count, ex.GetType().Name));
                warn("A live price RPC batch failed after execution began; a full product.price.list readback is required before retrying.");
                return ProtocolJson.Response(request.Id, false, "partial_apply_uncertain", "A price RPC failed after execution began. Rollback was attempted; list live prices before retrying.", revision, new JObject
                {
                    ["previewId"] = preview.Id,
                    ["submittedBeforeFailure"] = submitted,
                    ["rollbackSubmitted"] = rollbackSubmitted,
                    ["total"] = preview.Changes.Count,
                    ["readbackRequired"] = true
                });
            }

            previews.Remove(preview.Id);
            priceLimitPreviews.Clear();
            marketPreviews.Clear();
            allowancePreviews.Clear();
            revision++;
            audit(string.Format(CultureInfo.InvariantCulture, "op=product.price.applyPreview previewId={0} revision={1} count={2}", preview.Id, revision, preview.Changes.Count));
            return Ok(request, string.Format(CultureInfo.InvariantCulture, "Submitted {0} live price changes through ProductManager.SendPrice.", preview.Changes.Count), new JObject
            {
                ["applied"] = preview.Changes.Count,
                ["previewId"] = preview.Id,
                ["changes"] = preview.ChangesJson()
            });
        }

        private string GetPriceLimit(BridgeRequest request)
        {
            if (!IsKnownBuild())
                return Fail(request, "unknown_build", "Sell-limit access is disabled for this game build.");

            return Ok(request, "Read the live sell-value limits.", PriceLimitStatusJson());
        }

        private string PreviewPriceLimit(BridgeRequest request)
        {
            RuntimeState state = ReadRuntimeState();
            string error;
            if (!TryEnsurePriceLimitEligible(state, out error))
                return Fail(request, "sell_limit_unavailable", error);

            bool? enabledValue = request.Arguments.Value<bool?>("enabled");
            if (!enabledValue.HasValue)
                return Fail(request, "invalid_args", "enabled is required for a deal-limit preview.");
            int? requested = request.Arguments.Value<int?>("maxDealTotal");
            int next = enabledValue.Value
                ? (requested ?? 0)
                : SellPriceLimitManager.ReviewedDefaultDealTotalMax;
            if (next < SellPriceLimitManager.ReviewedDefaultDealTotalMax || next > SellPriceLimitManager.HardMaximumDealTotal)
                return Fail(request, "invalid_deal_limit", string.Format(CultureInfo.InvariantCulture, "maxDealTotal must be a whole number between {0} and {1}.", SellPriceLimitManager.ReviewedDefaultDealTotalMax, SellPriceLimitManager.HardMaximumDealTotal));

            PriceLimitPreview preview = new PriceLimitPreview
            {
                Id = Guid.NewGuid().ToString("N"),
                Revision = revision,
                ConfigRevision = SellPriceLimitManager.ConfigRevision,
                ExpiresUtc = DateTime.UtcNow.AddSeconds(60),
                ExpectedOverrideEnabled = SellPriceLimitManager.DealTotalOverrideEnabled,
                ExpectedConfiguredMax = SellPriceLimitManager.ConfiguredDealTotalMax,
                NewOverrideEnabled = enabledValue.Value,
                NewMaxDealTotal = next
            };

            PurgeExpiredPreviews();
            if (priceLimitPreviews.Count >= 16)
                RemoveOldestPriceLimitPreview();
            priceLimitPreviews[preview.Id] = preview;
            audit(string.Format(CultureInfo.InvariantCulture, "op=sale.dealLimit.preview previewId={0} revision={1} enabled={2} maxDealTotal={3}", preview.Id, preview.Revision, preview.NewOverrideEnabled, preview.NewMaxDealTotal));
            return Ok(request, "Previewed the deal-total maximum change.", preview.ToJson());
        }

        private string ApplyPriceLimitPreview(BridgeRequest request)
        {
            RuntimeState state = ReadRuntimeState();
            string error;
            if (!TryEnsurePriceLimitEligible(state, out error))
                return Fail(request, "sell_limit_unavailable", error);

            string previewId = request.Arguments.Value<string>("previewId") ?? string.Empty;
            long? expectedRevision = request.Arguments.Value<long?>("expectedRevision");
            long? expectedConfigRevision = request.Arguments.Value<long?>("expectedConfigRevision");
            if (!PipeServer.IsSafeIdentifier(previewId, 64) || !expectedRevision.HasValue || !expectedConfigRevision.HasValue)
                return Fail(request, "invalid_args", "previewId, expectedRevision, and expectedConfigRevision are required.");

            PurgeExpiredPreviews();
            PriceLimitPreview preview;
            if (!priceLimitPreviews.TryGetValue(previewId, out preview))
                return Fail(request, "preview_not_found", "The deal-limit preview was not found or has expired.");
            if (expectedRevision.Value != revision || preview.Revision != revision)
                return Fail(request, "revision_conflict", "Live state changed after the deal-limit preview. Create a new preview.");
            if (expectedConfigRevision.Value != SellPriceLimitManager.ConfigRevision
                || preview.ConfigRevision != SellPriceLimitManager.ConfigRevision)
                return Fail(request, "config_revision_conflict", "The deal-limit configuration changed after the preview. Create a new preview.");
            if (preview.ExpectedOverrideEnabled != SellPriceLimitManager.DealTotalOverrideEnabled
                || preview.ExpectedConfiguredMax != SellPriceLimitManager.ConfiguredDealTotalMax)
                return Fail(request, "sell_limit_conflict", "The configured deal limit changed after the preview.");

            if (request.DryRun)
                return Ok(request, "Deal-limit apply preconditions passed; nothing changed because dryRun is true.", new JObject
                {
                    ["wouldEnableOverride"] = preview.NewOverrideEnabled,
                    ["wouldSetMaxDealTotal"] = preview.NewMaxDealTotal,
                    ["previewId"] = preview.Id
                });

            if (!SellPriceLimitManager.TrySetOverride(preview.NewOverrideEnabled, preview.NewMaxDealTotal, out error))
                return Fail(request, "sell_limit_apply_failed", error);

            previews.Clear();
            priceLimitPreviews.Clear();
            marketPreviews.Clear();
            allowancePreviews.Clear();
            revision++;
            JObject data = PriceLimitStatusJson();
            data["applied"] = true;
            data["previewId"] = preview.Id;
            return Ok(request, preview.NewOverrideEnabled
                ? "Applied and persisted the custom maximum deal total."
                : "Restored and persisted the reviewed $9,999 deal-total maximum.", data);
        }

        private static JObject PriceLimitStatusJson()
        {
            return new JObject
            {
                ["unitPriceMin"] = SellPriceLimitManager.CurrentUnitPriceMin,
                ["unitPriceMax"] = SellPriceLimitManager.CurrentUnitPriceMax,
                ["reviewedDefaultMaxDealTotal"] = SellPriceLimitManager.ReviewedDefaultDealTotalMax,
                ["hardMaximumDealTotal"] = SellPriceLimitManager.HardMaximumDealTotal,
                ["overrideEnabled"] = SellPriceLimitManager.DealTotalOverrideEnabled,
                ["configuredMaxDealTotal"] = SellPriceLimitManager.ConfiguredDealTotalMax,
                ["effectiveMaxDealTotal"] = SellPriceLimitManager.EffectiveMaxDealTotal,
                ["counterofferStaticMax"] = SellPriceLimitManager.CurrentCounterofferMax,
                ["handoverStaticMax"] = SellPriceLimitManager.CurrentHandoverMax,
                ["overrideApplied"] = SellPriceLimitManager.OverrideApplied,
                ["patchActive"] = SellPriceLimitManager.PatchActive,
                ["persistenceReady"] = SellPriceLimitManager.PersistenceReady,
                ["configRevision"] = SellPriceLimitManager.ConfigRevision
            };
        }

        private string ListMarketValues(BridgeRequest request)
        {
            if (!IsKnownBuild())
                return Fail(request, "unknown_build", "Fair-market access is disabled for this game build.");
            if (!MarketValueScaling.PatchActive || !MarketValueScaling.PersistenceReady)
                return Fail(request, "market_patch_unavailable", "The fair-market patch or its fixed persistence store is unavailable.");

            RuntimeState state = ReadRuntimeState();
            if (!state.SaveLoaded)
                return Fail(request, "save_not_loaded", "Load a save before listing fair-market values.");

            ProductManager manager;
            string error;
            if (!TryGetProductManager(false, out manager, out error))
                return Fail(request, "product_manager_unavailable", error);
            if (!TryEnsureMarketEligible(state, manager, out error))
                return Fail(request, "market_scope_unavailable", error);

            ProductFilter filter;
            if (!TryReadProductFilter(request.Arguments, out filter, out error))
                return Fail(request, "invalid_args", error);

            List<LiveProduct> products = ReadProducts(manager, filter);
            JArray items = new JArray();
            foreach (LiveProduct product in products)
                items.Add(ReadMarketRow(product));

            return Ok(request, string.Format(CultureInfo.InvariantCulture, "Listed {0} live product economics rows.", products.Count), new JObject
            {
                ["count"] = products.Count,
                ["saveScope"] = MarketValueScaling.ActiveSaveScope,
                ["configRevision"] = MarketValueScaling.ConfigRevision,
                ["isSoloHost"] = state.IsHost && state.IsServer && state.RemoteClientCountKnown && state.RemoteClientCount == 0,
                ["products"] = items
            });
        }

        private string PreviewMarketValues(BridgeRequest request)
        {
            if (!IsKnownBuild())
                return Fail(request, "unknown_build", "Fair-market previews are disabled for this game build.");
            if (!MarketValueScaling.PatchActive || !MarketValueScaling.PersistenceReady)
                return Fail(request, "market_patch_unavailable", "The fair-market patch or its fixed persistence store is unavailable.");

            RuntimeState state = ReadRuntimeState();
            if (!state.SaveLoaded)
                return Fail(request, "save_not_loaded", "Load a save before previewing fair-market values.");

            string mode = (request.Arguments.Value<string>("mode") ?? "matchSellPrice").Trim();
            bool matchSellPrice = string.Equals(mode, "matchSellPrice", StringComparison.OrdinalIgnoreCase);
            bool absoluteFactor = string.Equals(mode, "absoluteFactor", StringComparison.OrdinalIgnoreCase);
            bool explicitValues = string.Equals(mode, "explicitValues", StringComparison.OrdinalIgnoreCase);
            if (!matchSellPrice && !absoluteFactor && !explicitValues)
                return Fail(request, "invalid_mode", "mode must be matchSellPrice, absoluteFactor, or explicitValues.");
            double? factorValue = request.Arguments.Value<double?>("factor");
            if (absoluteFactor && (!factorValue.HasValue
                || double.IsNaN(factorValue.Value)
                || double.IsInfinity(factorValue.Value)
                || factorValue.Value < MarketValueScaling.MinFactor
                || factorValue.Value > MarketValueScaling.MaxFactor))
                return Fail(request, "invalid_factor", "absoluteFactor mode requires a finite factor between 0.1 and 10.");

            string error;
            ProductFilter filter = null;
            Dictionary<string, float> explicitTargets = null;
            if (explicitValues)
            {
                if (!TryReadManualMarketTargets(request.Arguments, out explicitTargets, out error))
                    return Fail(request, "invalid_args", error);
            }
            else
            {
                if (!TryReadProductFilter(request.Arguments, out filter, out error))
                    return Fail(request, "invalid_args", error);
            }

            ProductManager manager;
            if (!TryGetProductManager(false, out manager, out error))
                return Fail(request, "product_manager_unavailable", error);
            if (!TryEnsureMarketEligible(state, manager, out error))
                return Fail(request, "market_scope_unavailable", error);

            List<LiveProduct> products;
            if (explicitValues)
            {
                Dictionary<string, LiveProduct> map = ReadProductMap(manager);
                products = new List<LiveProduct>();
                foreach (KeyValuePair<string, float> target in explicitTargets)
                {
                    LiveProduct product;
                    if (!map.TryGetValue(target.Key, out product))
                        return Fail(request, "product_changed", "A requested product is not available: " + target.Key);
                    products.Add(product);
                }
                products.Sort((left, right) => string.Compare(left.Id, right.Id, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                products = ReadProducts(manager, filter);
            }
            if (products.Count == 0)
                return Fail(request, "no_matching_products", "No matching live products were found.");

            MarketPreview preview = new MarketPreview
            {
                Id = Guid.NewGuid().ToString("N"),
                Revision = revision,
                ConfigRevision = MarketValueScaling.ConfigRevision,
                SaveScope = MarketValueScaling.ActiveSaveScope,
                ExpiresUtc = DateTime.UtcNow.AddSeconds(60),
                Mode = matchSellPrice ? "matchSellPrice" : (absoluteFactor ? "absoluteFactor" : "explicitValues"),
                Factor = absoluteFactor ? (float)factorValue.Value : 0f
            };

            foreach (LiveProduct product in products)
            {
                float vanilla = MarketValueScaling.CalculateVanilla(product.Definition);
                float current = product.Definition.MarketValue;
                float newFactor;
                float next;
                if (matchSellPrice)
                {
                    if (vanilla <= PriceTolerance && product.Price > PriceTolerance)
                        return Fail(request, "invalid_market_value", "A product with nonzero sell price has no positive vanilla market value: " + product.Id);
                    newFactor = vanilla <= PriceTolerance ? 1f : product.Price / vanilla;
                    next = product.Price;
                }
                else
                {
                    if (explicitValues)
                    {
                        next = explicitTargets[product.Id];
                        if (vanilla <= PriceTolerance)
                        {
                            if (next > PriceTolerance)
                                return Fail(request, "invalid_market_value", "A product with no positive vanilla market value cannot be assigned a positive manual value: " + product.Id);
                            newFactor = 1f;
                        }
                        else
                        {
                            newFactor = next / vanilla;
                        }
                    }
                    else
                    {
                        newFactor = preview.Factor;
                        next = vanilla * newFactor;
                    }
                }
                if (!IsFinite(vanilla) || vanilla < 0f || vanilla > MarketValueScaling.MaxMarketValue
                    || !IsFinite(current) || current < 0f || current > MarketValueScaling.MaxMarketValue
                    || !IsFinite(next) || next < 0f || next > MarketValueScaling.MaxMarketValue
                    || !IsFinite(newFactor) || newFactor < MarketValueScaling.MinFactor || newFactor > MarketValueScaling.MaxFactor)
                    return Fail(request, "invalid_market_value", "A matching product produced a non-finite or out-of-bounds fair-market value: " + product.Id);

                preview.Changes.Add(new MarketChange
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    DrugType = product.DrugType,
                    ExpectedSellPrice = product.Price,
                    ExpectedVanillaMarketValue = vanilla,
                    ExpectedCurrentMarketValue = current,
                    ExpectedOldFactor = MarketValueScaling.GetFactor(product.Id),
                    NewFactor = newFactor,
                    NewMarketValue = next,
                    CurrentValueProposition = ReadValueProposition(product.Definition, product.Price),
                    PlannedValueProposition = CalculateValueProposition(next, product.Price)
                });
            }

            PurgeExpiredPreviews();
            if (marketPreviews.Count >= 32)
                RemoveOldestMarketPreview();
            marketPreviews[preview.Id] = preview;
            audit(string.Format(CultureInfo.InvariantCulture, "op=product.market.previewSync previewId={0} revision={1} configRevision={2} saveScope={3} count={4} mode={5} factor={6}", preview.Id, preview.Revision, preview.ConfigRevision, preview.SaveScope, preview.Changes.Count, preview.Mode, preview.Factor));
            return Ok(request, string.Format(CultureInfo.InvariantCulture, "Previewed {0} fair-market synchronizations.", preview.Changes.Count), preview.ToJson());
        }

        private string ApplyMarketPreview(BridgeRequest request)
        {
            RuntimeState state;
            string gate = RequireMutationState(true, out state);
            if (gate != null)
                return Fail(request, "mutation_not_ready", gate);
            if (!state.RemoteClientCountKnown)
                return Fail(request, "multiplayer_state_unknown", "Remote-client state could not be verified; fair-market mutation is disabled.");
            if (state.RemoteClientCount != 0)
                return Fail(request, "multiplayer_not_supported", "Fair-market values are local and not replicated; disconnect remote players before applying.");
            if (!MarketValueScaling.PatchActive || !MarketValueScaling.PersistenceReady)
                return Fail(request, "market_patch_unavailable", "The fair-market patch or its fixed persistence store is unavailable.");

            string previewId = request.Arguments.Value<string>("previewId") ?? string.Empty;
            long? expectedRevision = request.Arguments.Value<long?>("expectedRevision");
            long? expectedConfigRevision = request.Arguments.Value<long?>("expectedConfigRevision");
            if (!PipeServer.IsSafeIdentifier(previewId, 64) || !expectedRevision.HasValue || !expectedConfigRevision.HasValue)
                return Fail(request, "invalid_args", "previewId, expectedRevision, and expectedConfigRevision are required.");

            PurgeExpiredPreviews();
            MarketPreview preview;
            if (!marketPreviews.TryGetValue(previewId, out preview))
                return Fail(request, "preview_not_found", "The fair-market preview was not found or has expired.");
            if (expectedRevision.Value != revision || preview.Revision != revision)
                return Fail(request, "revision_conflict", "Live state changed after the preview. Create a new preview.");
            if (expectedConfigRevision.Value != MarketValueScaling.ConfigRevision || preview.ConfigRevision != MarketValueScaling.ConfigRevision)
                return Fail(request, "config_revision_conflict", "Fair-market configuration changed after the preview. Create a new preview.");

            ProductManager manager;
            string error;
            if (!TryGetProductManager(false, out manager, out error))
                return Fail(request, "product_manager_unavailable", error);
            if (!TryEnsureMarketEligible(state, manager, out error))
                return Fail(request, "market_scope_unavailable", error);
            if (!string.Equals(preview.SaveScope, MarketValueScaling.ActiveSaveScope, StringComparison.Ordinal))
                return Fail(request, "save_scope_conflict", "A different save is loaded from the one used by the preview.");

            Dictionary<string, LiveProduct> current = ReadProductMap(manager);
            foreach (MarketChange change in preview.Changes)
            {
                LiveProduct product;
                if (!current.TryGetValue(change.ProductId, out product))
                    return Fail(request, "product_changed", "A previewed product is no longer available: " + change.ProductId);
                float vanilla = MarketValueScaling.CalculateVanilla(product.Definition);
                float market = product.Definition.MarketValue;
                float factor = MarketValueScaling.GetFactor(product.Id);
                if (Math.Abs(product.Price - change.ExpectedSellPrice) > PriceTolerance
                    || Math.Abs(vanilla - change.ExpectedVanillaMarketValue) > PriceTolerance
                    || Math.Abs(market - change.ExpectedCurrentMarketValue) > PriceTolerance
                    || Math.Abs(factor - change.ExpectedOldFactor) > PriceTolerance)
                    return Fail(request, "market_conflict", "Sell price, vanilla value, effective value, or factor changed for " + change.ProductId + ". Create a new preview.");
            }

            if (request.DryRun)
                return Ok(request, "Fair-market apply preconditions passed; no values changed because dryRun is true.", new JObject
                {
                    ["wouldApply"] = preview.Changes.Count,
                    ["previewId"] = preview.Id
                });

            Dictionary<string, float> priorFactors = MarketValueScaling.SnapshotFactors();
            Dictionary<string, float> nextFactors = new Dictionary<string, float>(priorFactors, StringComparer.OrdinalIgnoreCase);
            foreach (MarketChange change in preview.Changes)
                nextFactors[change.ProductId] = change.NewFactor;

            if (!MarketValueScaling.TrySetFactors(state.SavePath, nextFactors, manager, out error))
                return Fail(request, "market_apply_failed", error);

            JArray readback = new JArray();
            bool verified = true;
            foreach (MarketChange change in preview.Changes)
            {
                LiveProduct product = current[change.ProductId];
                float actual = product.Definition.MarketValue;
                float actualFactor = MarketValueScaling.GetFactor(change.ProductId);
                float actualProposition = ReadValueProposition(product.Definition, product.Price);
                if (Math.Abs(actual - change.NewMarketValue) > PriceTolerance
                    || Math.Abs(actualFactor - change.NewFactor) > PriceTolerance
                    || Math.Abs(actualProposition - change.PlannedValueProposition) > 0.002f)
                    verified = false;
                JObject row = change.ToJson();
                row["actualMarketValue"] = actual;
                row["actualFactor"] = actualFactor;
                row["actualValueProposition"] = actualProposition;
                readback.Add(row);
            }

            if (!verified)
            {
                string rollbackError;
                bool rolledBack = MarketValueScaling.TrySetFactors(state.SavePath, priorFactors, manager, out rollbackError);
                if (rolledBack)
                {
                    foreach (MarketChange change in preview.Changes)
                    {
                        LiveProduct product = current[change.ProductId];
                        if (Math.Abs(product.Definition.MarketValue - change.ExpectedCurrentMarketValue) > PriceTolerance
                            || Math.Abs(MarketValueScaling.GetFactor(change.ProductId) - change.ExpectedOldFactor) > PriceTolerance)
                        {
                            rolledBack = false;
                            rollbackError = "Rollback readback did not match the previewed original values.";
                            break;
                        }
                    }
                }
                marketPreviews.Clear();
                allowancePreviews.Clear();
                previews.Clear();
                priceLimitPreviews.Clear();
                revision++;
                return ProtocolJson.Response(request.Id, false, rolledBack ? "market_verification_failed" : "partial_apply_uncertain", rolledBack ? "Fair-market readback did not match and the configuration was rolled back." : "Fair-market readback did not match and rollback was incomplete: " + rollbackError, revision, new JObject
                {
                    ["previewId"] = preview.Id,
                    ["readbackRequired"] = true,
                    ["rows"] = readback
                });
            }

            marketPreviews.Remove(preview.Id);
            previews.Clear();
            priceLimitPreviews.Clear();
            allowancePreviews.Clear();
            revision++;
            audit(string.Format(CultureInfo.InvariantCulture, "op=product.market.applyPreview previewId={0} revision={1} configRevision={2} saveScope={3} count={4} mode={5} factor={6}", preview.Id, revision, MarketValueScaling.ConfigRevision, preview.SaveScope, preview.Changes.Count, preview.Mode, preview.Factor));
            return Ok(request, string.Format(CultureInfo.InvariantCulture, "Applied and verified {0} save-scoped fair-market overrides.", preview.Changes.Count), new JObject
            {
                ["applied"] = preview.Changes.Count,
                ["previewId"] = preview.Id,
                ["saveScope"] = preview.SaveScope,
                ["configRevision"] = MarketValueScaling.ConfigRevision,
                ["persistedBy"] = "bridge-sidecar",
                ["rows"] = readback
            });
        }

        private string ListCustomerAllowances(BridgeRequest request)
        {
            if (!IsKnownBuild())
                return Fail(request, "unknown_build", "Customer-allowance access is disabled for this game build.");
            if (!CustomerAllowanceScaling.PatchActive || !CustomerAllowanceScaling.PersistenceReady)
                return Fail(request, "allowance_patch_unavailable", "The customer-allowance patch or its fixed persistence store is unavailable.");

            bool includeLocked;
            string error;
            if (!TryReadIncludeLocked(request.Arguments, out includeLocked, out error))
                return Fail(request, "invalid_args", error);

            RuntimeState state = ReadRuntimeState();
            if (!state.SaveLoaded)
                return Fail(request, "save_not_loaded", "Load a save before listing customer allowances.");
            if (!TryEnsureAllowanceEligible(state, out error))
                return Fail(request, "allowance_scope_unavailable", error);

            List<LiveCustomerAllowance> customers = CustomerAllowanceScaling.SnapshotCustomers(includeLocked, out error);
            if (error != null)
                return Fail(request, "customer_graph_unavailable", error);

            JArray items = new JArray();
            foreach (LiveCustomerAllowance customer in customers)
            {
                AllowanceMetrics metrics;
                if (!TryReadAllowanceMetrics(
                    customer,
                    customer.CurrentMinWeeklySpend,
                    customer.CurrentMaxWeeklySpend,
                    out metrics,
                    out error))
                    return Fail(request, "invalid_allowance_state", error);
                items.Add(CustomerAllowanceRow(customer, metrics));
            }

            return Ok(request, string.Format(CultureInfo.InvariantCulture, "Listed {0} live customer allowance rows.", customers.Count), new JObject
            {
                ["count"] = customers.Count,
                ["includeLocked"] = includeLocked,
                ["saveScope"] = CustomerAllowanceScaling.ActiveSaveScope,
                ["configRevision"] = CustomerAllowanceScaling.ConfigRevision,
                ["isSoloHost"] = state.IsHost && state.IsServer && state.RemoteClientCountKnown && state.RemoteClientCount == 0,
                ["customers"] = items
            });
        }

        private string PreviewCustomerAllowances(BridgeRequest request)
        {
            if (!IsKnownBuild())
                return Fail(request, "unknown_build", "Customer-allowance previews are disabled for this game build.");
            if (!CustomerAllowanceScaling.PatchActive || !CustomerAllowanceScaling.PersistenceReady)
                return Fail(request, "allowance_patch_unavailable", "The customer-allowance patch or its fixed persistence store is unavailable.");

            RuntimeState state = ReadRuntimeState();
            if (!state.SaveLoaded)
                return Fail(request, "save_not_loaded", "Load a save before previewing customer allowances.");

            string mode = (request.Arguments.Value<string>("mode") ?? "originalFactor").Trim();
            bool originalFactor = string.Equals(mode, "originalFactor", StringComparison.OrdinalIgnoreCase);
            bool explicitValues = string.Equals(mode, "explicitValues", StringComparison.OrdinalIgnoreCase);
            if (!originalFactor && !explicitValues)
                return Fail(request, "invalid_mode", "mode must be originalFactor or explicitValues.");

            double? factorValue = request.Arguments.Value<double?>("factor");
            if (originalFactor && (!factorValue.HasValue
                || double.IsNaN(factorValue.Value)
                || double.IsInfinity(factorValue.Value)
                || factorValue.Value < 0.1
                || factorValue.Value > 1000000.0))
                return Fail(request, "invalid_factor", "originalFactor mode requires a finite factor between 0.1 and 1,000,000.");

            bool includeLocked = false;
            string error;
            Dictionary<string, AllowanceRange> explicitTargets = null;
            if (explicitValues)
            {
                if (!TryReadManualAllowanceTargets(request.Arguments, out explicitTargets, out error))
                    return Fail(request, "invalid_args", error);
            }
            else if (!TryReadIncludeLocked(request.Arguments, out includeLocked, out error))
            {
                return Fail(request, "invalid_args", error);
            }

            if (!TryEnsureAllowanceEligible(state, out error))
                return Fail(request, "allowance_scope_unavailable", error);

            List<LiveCustomerAllowance> allCustomers = CustomerAllowanceScaling.SnapshotCustomers(true, out error);
            if (error != null)
                return Fail(request, "customer_graph_unavailable", error);
            Dictionary<string, LiveCustomerAllowance> customerMap = new Dictionary<string, LiveCustomerAllowance>(StringComparer.OrdinalIgnoreCase);
            foreach (LiveCustomerAllowance customer in allCustomers)
                customerMap[customer.Id] = customer;

            List<LiveCustomerAllowance> selected = new List<LiveCustomerAllowance>();
            if (explicitValues)
            {
                foreach (KeyValuePair<string, AllowanceRange> target in explicitTargets)
                {
                    LiveCustomerAllowance customer;
                    if (!customerMap.TryGetValue(target.Key, out customer))
                        return Fail(request, "customer_changed", "A requested customer is not available: " + target.Key);
                    selected.Add(customer);
                }
                selected.Sort((left, right) => string.Compare(left.Id, right.Id, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                foreach (LiveCustomerAllowance customer in allCustomers)
                {
                    if (includeLocked || customer.Unlocked)
                        selected.Add(customer);
                }
            }
            if (selected.Count == 0)
                return Fail(request, "no_matching_customers", "No matching live customers were found.");

            AllowancePreview preview = new AllowancePreview
            {
                Id = Guid.NewGuid().ToString("N"),
                Revision = revision,
                ConfigRevision = CustomerAllowanceScaling.ConfigRevision,
                SaveScope = CustomerAllowanceScaling.ActiveSaveScope,
                ExpiresUtc = DateTime.UtcNow.AddSeconds(60),
                Mode = originalFactor ? "originalFactor" : "explicitValues",
                Factor = originalFactor ? (float)factorValue.Value : 0f,
                IncludeLocked = includeLocked
            };

            foreach (LiveCustomerAllowance customer in selected)
            {
                float nextMin;
                float nextMax;
                if (originalFactor)
                {
                    nextMin = (float)(customer.OriginalMinWeeklySpend * factorValue.Value);
                    nextMax = (float)(customer.OriginalMaxWeeklySpend * factorValue.Value);
                }
                else
                {
                    AllowanceRange target = explicitTargets[customer.Id];
                    nextMin = target.MinWeeklySpend;
                    nextMax = target.MaxWeeklySpend;
                }

                if (!IsValidAllowanceRange(nextMin, nextMax))
                    return Fail(request, "invalid_allowance_value", "A requested allowance range is non-finite, reversed, or outside 0-16777215: " + customer.Id);
                if (!CanRepresentAllowanceRange(customer, nextMin, nextMax))
                    return Fail(request, "invalid_allowance_value", "The requested range cannot be represented safely for customer " + customer.Id + ".");

                AllowanceMetrics currentMetrics;
                AllowanceMetrics plannedMetrics;
                if (!TryReadAllowanceMetrics(
                    customer,
                    customer.CurrentMinWeeklySpend,
                    customer.CurrentMaxWeeklySpend,
                    out currentMetrics,
                    out error))
                    return Fail(request, "invalid_allowance_state", error);
                if (!TryReadAllowanceMetrics(customer, nextMin, nextMax, out plannedMetrics, out error))
                    return Fail(request, "invalid_allowance_value", error);

                preview.Changes.Add(new AllowanceChange
                {
                    CustomerId = customer.Id,
                    CustomerName = customer.Name,
                    Unlocked = customer.Unlocked,
                    DataPointer = customer.Data.Pointer.ToInt64(),
                    OriginalMinWeeklySpend = customer.OriginalMinWeeklySpend,
                    OriginalMaxWeeklySpend = customer.OriginalMaxWeeklySpend,
                    ExpectedCurrentMinWeeklySpend = customer.CurrentMinWeeklySpend,
                    ExpectedCurrentMaxWeeklySpend = customer.CurrentMaxWeeklySpend,
                    NewMinWeeklySpend = nextMin,
                    NewMaxWeeklySpend = nextMax,
                    Relationship = currentMetrics.Relationship,
                    Addiction = currentMetrics.Addiction,
                    OrdersPerWeek = currentMetrics.OrdersPerWeek,
                    CurrentAdjustedWeeklySpend = currentMetrics.AdjustedWeeklySpend,
                    CurrentAllowancePerOrder = currentMetrics.AllowancePerOrder,
                    CurrentHardOfferLimit = currentMetrics.HardOfferLimit,
                    PlannedAdjustedWeeklySpend = plannedMetrics.AdjustedWeeklySpend,
                    PlannedAllowancePerOrder = plannedMetrics.AllowancePerOrder,
                    PlannedHardOfferLimit = plannedMetrics.HardOfferLimit
                });
            }

            PurgeExpiredPreviews();
            if (allowancePreviews.Count >= 32)
                RemoveOldestAllowancePreview();
            allowancePreviews[preview.Id] = preview;
            audit(string.Format(CultureInfo.InvariantCulture,
                "op=customer.allowance.preview previewId={0} revision={1} configRevision={2} saveScope={3} count={4} mode={5} factor={6}",
                preview.Id, preview.Revision, preview.ConfigRevision, preview.SaveScope, preview.Changes.Count, preview.Mode, preview.Factor));
            return Ok(request, string.Format(CultureInfo.InvariantCulture, "Previewed {0} customer allowance changes.", preview.Changes.Count), preview.ToJson());
        }

        private string ApplyCustomerAllowancePreview(BridgeRequest request)
        {
            RuntimeState state;
            string gate = RequireMutationState(true, out state);
            if (gate != null)
                return Fail(request, "mutation_not_ready", gate);
            if (!state.RemoteClientCountKnown)
                return Fail(request, "multiplayer_state_unknown", "Remote-client state could not be verified; customer-allowance mutation is disabled.");
            if (state.RemoteClientCount != 0)
                return Fail(request, "multiplayer_not_supported", "Customer allowances are local and not replicated; disconnect remote players before applying.");
            if (!CustomerAllowanceScaling.PatchActive || !CustomerAllowanceScaling.PersistenceReady)
                return Fail(request, "allowance_patch_unavailable", "The customer-allowance patch or its fixed persistence store is unavailable.");

            string previewId = request.Arguments.Value<string>("previewId") ?? string.Empty;
            long? expectedRevision = request.Arguments.Value<long?>("expectedRevision");
            long? expectedConfigRevision = request.Arguments.Value<long?>("expectedConfigRevision");
            if (!PipeServer.IsSafeIdentifier(previewId, 64) || !expectedRevision.HasValue || !expectedConfigRevision.HasValue)
                return Fail(request, "invalid_args", "previewId, expectedRevision, and expectedConfigRevision are required.");

            PurgeExpiredPreviews();
            AllowancePreview preview;
            if (!allowancePreviews.TryGetValue(previewId, out preview))
                return Fail(request, "preview_not_found", "The customer-allowance preview was not found or has expired.");
            if (expectedRevision.Value != revision || preview.Revision != revision)
                return Fail(request, "revision_conflict", "Live state changed after the preview. Create a new preview.");

            string error;
            if (!TryEnsureAllowanceEligible(state, out error))
                return Fail(request, "allowance_scope_unavailable", error);
            if (expectedConfigRevision.Value != CustomerAllowanceScaling.ConfigRevision
                || preview.ConfigRevision != CustomerAllowanceScaling.ConfigRevision)
                return Fail(request, "config_revision_conflict", "Customer-allowance configuration changed after the preview. Create a new preview.");
            if (!string.Equals(preview.SaveScope, CustomerAllowanceScaling.ActiveSaveScope, StringComparison.Ordinal))
                return Fail(request, "save_scope_conflict", "A different save is loaded from the one used by the preview.");

            List<LiveCustomerAllowance> currentCustomers = CustomerAllowanceScaling.SnapshotCustomers(true, out error);
            if (error != null)
                return Fail(request, "customer_graph_unavailable", error);
            Dictionary<string, LiveCustomerAllowance> currentMap = new Dictionary<string, LiveCustomerAllowance>(StringComparer.OrdinalIgnoreCase);
            foreach (LiveCustomerAllowance customer in currentCustomers)
                currentMap[customer.Id] = customer;

            foreach (AllowanceChange change in preview.Changes)
            {
                LiveCustomerAllowance customer;
                if (!currentMap.TryGetValue(change.CustomerId, out customer))
                    return Fail(request, "customer_changed", "A previewed customer is no longer available: " + change.CustomerId);
                AllowanceMetrics metrics;
                if (!TryReadAllowanceMetrics(
                    customer,
                    customer.CurrentMinWeeklySpend,
                    customer.CurrentMaxWeeklySpend,
                    out metrics,
                    out error))
                    return Fail(request, "invalid_allowance_state", error);
                if (customer.Data.Pointer.ToInt64() != change.DataPointer
                    || customer.Unlocked != change.Unlocked
                    || !NearlyEqualAllowance(customer.OriginalMinWeeklySpend, change.OriginalMinWeeklySpend)
                    || !NearlyEqualAllowance(customer.OriginalMaxWeeklySpend, change.OriginalMaxWeeklySpend)
                    || !NearlyEqualAllowance(customer.CurrentMinWeeklySpend, change.ExpectedCurrentMinWeeklySpend)
                    || !NearlyEqualAllowance(customer.CurrentMaxWeeklySpend, change.ExpectedCurrentMaxWeeklySpend)
                    || !NearlyEqualAllowance(metrics.Relationship, change.Relationship)
                    || !NearlyEqualAllowance(metrics.Addiction, change.Addiction)
                    || metrics.OrdersPerWeek != change.OrdersPerWeek
                    || !NearlyEqualAllowance(metrics.AdjustedWeeklySpend, change.CurrentAdjustedWeeklySpend))
                    return Fail(request, "allowance_conflict", "Customer data, relationship, order cadence, or allowance changed for " + change.CustomerId + ". Create a new preview.");
            }

            if (request.DryRun)
                return Ok(request, "Customer-allowance apply preconditions passed; no values changed because dryRun is true.", new JObject
                {
                    ["wouldApply"] = preview.Changes.Count,
                    ["previewId"] = preview.Id
                });

            Dictionary<string, AllowanceRange> priorRanges = CustomerAllowanceScaling.SnapshotRanges();
            Dictionary<string, AllowanceRange> nextRanges = CloneAllowanceRanges(priorRanges);
            foreach (AllowanceChange change in preview.Changes)
            {
                nextRanges[change.CustomerId] = new AllowanceRange
                {
                    MinWeeklySpend = change.NewMinWeeklySpend,
                    MaxWeeklySpend = change.NewMaxWeeklySpend
                };
            }

            if (!CustomerAllowanceScaling.TrySetRanges(state.SavePath, nextRanges, out error))
                return Fail(request, "allowance_apply_failed", error);

            JArray readback = new JArray();
            bool verified = VerifyAllowanceReadback(preview.Changes, false, out readback, out error);
            if (!verified)
            {
                string rollbackError;
                bool rolledBack = CustomerAllowanceScaling.TrySetRanges(state.SavePath, priorRanges, out rollbackError);
                if (rolledBack)
                {
                    JArray rollbackReadback;
                    rolledBack = VerifyAllowanceReadback(preview.Changes, true, out rollbackReadback, out rollbackError);
                }

                allowancePreviews.Clear();
                marketPreviews.Clear();
                previews.Clear();
                priceLimitPreviews.Clear();
                revision++;
                audit(string.Format(CultureInfo.InvariantCulture,
                    "op=customer.allowance.applyPreview outcome={0} previewId={1} revision={2} count={3}",
                    rolledBack ? "verification_failed_rolled_back" : "partial_apply_uncertain", preview.Id, revision, preview.Changes.Count));
                return ProtocolJson.Response(request.Id, false,
                    rolledBack ? "allowance_verification_failed" : "partial_apply_uncertain",
                    rolledBack
                        ? "Customer-allowance readback did not match and the configuration was rolled back."
                        : "Customer-allowance readback did not match and rollback was incomplete: " + (rollbackError ?? error),
                    revision,
                    new JObject
                    {
                        ["previewId"] = preview.Id,
                        ["readbackRequired"] = true,
                        ["rows"] = readback
                    });
            }

            allowancePreviews.Remove(preview.Id);
            marketPreviews.Clear();
            previews.Clear();
            priceLimitPreviews.Clear();
            revision++;
            audit(string.Format(CultureInfo.InvariantCulture,
                "op=customer.allowance.applyPreview previewId={0} revision={1} configRevision={2} saveScope={3} count={4} mode={5} factor={6}",
                preview.Id, revision, CustomerAllowanceScaling.ConfigRevision, preview.SaveScope, preview.Changes.Count, preview.Mode, preview.Factor));
            return Ok(request, string.Format(CultureInfo.InvariantCulture, "Applied and verified {0} save-scoped customer allowance overrides.", preview.Changes.Count), new JObject
            {
                ["applied"] = preview.Changes.Count,
                ["previewId"] = preview.Id,
                ["saveScope"] = preview.SaveScope,
                ["configRevision"] = CustomerAllowanceScaling.ConfigRevision,
                ["persistedBy"] = "bridge-sidecar",
                ["rows"] = readback
            });
        }

        private static JObject ReadMarketRow(LiveProduct product)
        {
            float vanilla = MarketValueScaling.CalculateVanilla(product.Definition);
            float effective = product.Definition.MarketValue;
            return new JObject
            {
                ["productId"] = product.Id,
                ["name"] = product.Name,
                ["drugType"] = product.DrugType,
                ["sellPrice"] = product.Price,
                ["vanillaMarketValue"] = vanilla,
                ["effectiveMarketValue"] = effective,
                ["factor"] = MarketValueScaling.GetFactor(product.Id),
                ["valueProposition"] = ReadValueProposition(product.Definition, product.Price),
                ["alignedWithSellPrice"] = Math.Abs(effective - product.Price) <= PriceTolerance
            };
        }

        private static float ReadValueProposition(ProductDefinition product, float price)
        {
            try
            {
                return Customer.GetValueProposition(product, price);
            }
            catch
            {
                return price <= 0f ? 0f : product.MarketValue / price;
            }
        }

        private static float CalculateValueProposition(float marketValue, float price)
        {
            if (price <= 0f)
                return 0f;
            double ratio = marketValue / price;
            if (ratio < 1.0)
                ratio = Math.Pow(Math.Max(0.0, ratio), 2.5);
            return (float)Math.Max(0.0, Math.Min(2.0, ratio));
        }

        private string ListLaunderLimits(BridgeRequest request)
        {
            if (!IsKnownBuild())
                return Fail(request, "unknown_build", "Launder-limit access is disabled for this game build.");
            if (!BusinessLaunderScaling.PatchActive || !BusinessLaunderScaling.PersistenceReady)
                return Fail(request, "launder_patch_unavailable", "The launder-limit patch or its fixed persistence store is unavailable.");

            RuntimeState state = ReadRuntimeState();
            if (!state.SaveLoaded)
                return Fail(request, "save_not_loaded", "Load a save before listing launder limits.");
            if (!TryEnsureLaunderEligible(state, out string error))
                return Fail(request, "launder_scope_unavailable", error);

            JArray items = new JArray();
            try
            {
                foreach (Business business in Business.OwnedBusinesses)
                {
                    if (business == null)
                        continue;
                    string code = business.propertyCode ?? string.Empty;
                    bool overridden = BusinessLaunderScaling.IsOverridden(code);
                    items.Add(new JObject
                    {
                        ["businessCode"] = code,
                        ["appliedLaunderLimit"] = business.appliedLaunderLimit,
                        ["currentLaunderTotal"] = business.currentLaunderTotal,
                        ["launderCapacity"] = business.LaunderCapacity,
                        ["customDailyLimit"] = BusinessLaunderScaling.GetLimit(code),
                        ["isOverridden"] = overridden,
                        ["remainingWithCustom"] = overridden ? BusinessLaunderScaling.RemainingFor(business) : business.LaunderCapacity,
                        ["operations"] = business.LaunderingOperations == null ? 0 : business.LaunderingOperations.Count
                    });
                }
            }
            catch (Exception ex)
            {
                return Fail(request, "business_graph_unavailable", "Failed to read owned business laundering state: " + ex.GetType().Name + ": " + ex.Message);
            }

            return Ok(request, string.Format(CultureInfo.InvariantCulture, "Listed {0} owned business laundering rows.", items.Count), new JObject
            {
                ["count"] = items.Count,
                ["saveScope"] = BusinessLaunderScaling.ActiveSaveScope,
                ["configRevision"] = BusinessLaunderScaling.ConfigRevision,
                ["businesses"] = items
            });
        }

        private string PreviewLaunderLimits(BridgeRequest request)
        {
            if (!IsKnownBuild())
                return Fail(request, "unknown_build", "Launder-limit previews are disabled for this game build.");
            if (!BusinessLaunderScaling.PatchActive || !BusinessLaunderScaling.PersistenceReady)
                return Fail(request, "launder_patch_unavailable", "The launder-limit patch or its fixed persistence store is unavailable.");

            RuntimeState state = ReadRuntimeState();
            if (!state.SaveLoaded)
                return Fail(request, "save_not_loaded", "Load a save before previewing launder limits.");
            if (!TryEnsureLaunderEligible(state, out string error))
                return Fail(request, "launder_scope_unavailable", error);

            Dictionary<string, int> targets;
            if (!TryReadLaunderTargets(request.Arguments, out targets, out error))
                return Fail(request, "invalid_args", error);

            LaunderPreviewGroup group = BusinessLaunderScaling.CreateGroupPreview(targets, out error);
            if (group == null)
                return Fail(request, "launder_target_invalid", error);

            JArray items = new JArray();
            foreach (LaunderPreview preview in group.Previews)
                items.Add(preview.ToJson());

            return Ok(request, string.Format(CultureInfo.InvariantCulture, "Previewed {0} launder-limit changes.", items.Count), new JObject
            {
                ["previewId"] = group.Id,
                ["expectedRevision"] = revision,
                ["expectedConfigRevision"] = BusinessLaunderScaling.ConfigRevision,
                ["count"] = items.Count,
                ["businesses"] = items
            });
        }

        private string ApplyLaunderPreview(BridgeRequest request)
        {
            string previewId = request.Arguments.Value<string>("previewId") ?? string.Empty;
            long? expectedRevision = request.Arguments.Value<long?>("expectedRevision");
            long? expectedConfigRevision = request.Arguments.Value<long?>("expectedConfigRevision");
            if (!PipeServer.IsSafeIdentifier(previewId, 64) || !expectedRevision.HasValue || !expectedConfigRevision.HasValue)
                return Fail(request, "invalid_args", "previewId, expectedRevision, and expectedConfigRevision are required.");

            LaunderPreviewGroup preview = BusinessLaunderScaling.TakePreview(previewId, out string error);
            if (preview == null)
                return Fail(request, "preview_not_found", error);
            if (expectedRevision.Value != revision || expectedConfigRevision.Value != BusinessLaunderScaling.ConfigRevision)
                return Fail(request, "revision_conflict", "The launder-limit preview is stale; refresh and preview again.");

            RuntimeState state;
            string gate = RequireMutationState(true, out state);
            if (gate != null)
                return Fail(request, "mutation_not_ready", gate);
            if (!TryEnsureLaunderEligible(state, out error))
                return Fail(request, "launder_scope_unavailable", error);
            if (!BusinessLaunderScaling.ApplyGroupPreview(preview, state.SavePath, out error))
                return Fail(request, "launder_apply_failed", error);

            revision++;
            audit(string.Format(CultureInfo.InvariantCulture, "op=business.launder.applyPreview previewId={0} revision={1} count={2}", previewId, revision, preview.Previews.Count));
            return Ok(request, string.Format(CultureInfo.InvariantCulture, "Applied and persisted {0} business launder-limit overrides.", preview.Previews.Count), new JObject
            {
                ["count"] = preview.Previews.Count
            });
        }

        private string ListEffects(BridgeRequest request)
        {
            if (!IsKnownBuild())
                return Fail(request, "unknown_build", "Effect access is disabled for this game build.");
            if (!EffectsIntensityManager.PersistenceReady)
                return Fail(request, "effects_profile_unavailable", "The effect profile store is unavailable.");

            RuntimeState state = ReadRuntimeState();
            if (!state.SaveLoaded)
                return Fail(request, "save_not_loaded", "Load a save before listing effect profiles.");
            if (!TryEnsureEffectsEligible(state, out string error))
                return Fail(request, "effects_scope_unavailable", error);

            List<LiveEffect> effects = EffectsIntensityManager.ListEffects(out error);
            if (effects == null)
                return Fail(request, "effects_graph_unavailable", error);

            JArray items = new JArray();
            foreach (LiveEffect effect in effects)
                items.Add(effect.ToJson());

            return Ok(request, string.Format(CultureInfo.InvariantCulture, "Listed {0} loaded effect profiles.", items.Count), new JObject
            {
                ["count"] = items.Count,
                ["saveScope"] = EffectsIntensityManager.ActiveSaveScope,
                ["configRevision"] = EffectsIntensityManager.ConfigRevision,
                ["effects"] = items
            });
        }

        private string PreviewEffects(BridgeRequest request)
        {
            if (!IsKnownBuild())
                return Fail(request, "unknown_build", "Effect previews are disabled for this game build.");
            if (!EffectsIntensityManager.PersistenceReady)
                return Fail(request, "effects_profile_unavailable", "The effect profile store is unavailable.");

            RuntimeState state = ReadRuntimeState();
            if (!state.SaveLoaded)
                return Fail(request, "save_not_loaded", "Load a save before previewing effect profiles.");
            if (!TryEnsureEffectsEligible(state, out string error))
                return Fail(request, "effects_scope_unavailable", error);

            List<EffectTarget> targets;
            if (!TryReadEffectTargets(request.Arguments, out targets, out error))
                return Fail(request, "invalid_args", error);

            EffectPreviewGroup group = EffectsIntensityManager.CreateGroupPreview(targets, out error);
            if (group == null)
                return Fail(request, "effect_target_invalid", error);

            JArray items = new JArray();
            foreach (EffectPreview preview in group.Previews)
                items.Add(preview.ToJson());

            return Ok(request, string.Format(CultureInfo.InvariantCulture, "Previewed {0} effect changes.", items.Count), new JObject
            {
                ["previewId"] = group.Id,
                ["expectedRevision"] = revision,
                ["expectedConfigRevision"] = EffectsIntensityManager.ConfigRevision,
                ["count"] = items.Count,
                ["effects"] = items
            });
        }

        private string ApplyEffectPreview(BridgeRequest request)
        {
            string previewId = request.Arguments.Value<string>("previewId") ?? string.Empty;
            long? expectedRevision = request.Arguments.Value<long?>("expectedRevision");
            long? expectedConfigRevision = request.Arguments.Value<long?>("expectedConfigRevision");
            if (!PipeServer.IsSafeIdentifier(previewId, 64) || !expectedRevision.HasValue || !expectedConfigRevision.HasValue)
                return Fail(request, "invalid_args", "previewId, expectedRevision, and expectedConfigRevision are required.");

            EffectPreviewGroup preview = EffectsIntensityManager.TakePreview(previewId, out string error);
            if (preview == null)
                return Fail(request, "preview_not_found", error);
            if (expectedRevision.Value != revision || expectedConfigRevision.Value != EffectsIntensityManager.ConfigRevision)
                return Fail(request, "revision_conflict", "The effect preview is stale; refresh and preview again.");

            RuntimeState state;
            string gate = RequireMutationState(true, out state);
            if (gate != null)
                return Fail(request, "mutation_not_ready", gate);
            if (!TryEnsureEffectsEligible(state, out error))
                return Fail(request, "effects_scope_unavailable", error);
            if (!EffectsIntensityManager.ApplyGroupPreview(preview, state.SavePath, out error))
                return Fail(request, "effects_apply_failed", error);

            revision++;
            audit(string.Format(CultureInfo.InvariantCulture, "op=effects.applyPreview previewId={0} revision={1} count={2}", previewId, revision, preview.Previews.Count));
            return Ok(request, string.Format(CultureInfo.InvariantCulture, "Applied and persisted {0} effect overrides.", preview.Previews.Count), new JObject
            {
                ["count"] = preview.Previews.Count
            });
        }

        private string GetPlayerSettings(BridgeRequest request)
        {
            if (!IsKnownBuild() || !PlayerRuntimeSettings.PatchActive || !PlayerRuntimeSettings.PersistenceReady)
                return Fail(request, "player_settings_unavailable", "Player settings are unavailable for this game build.");

            PlayerSettingsSnapshot snapshot = PlayerRuntimeSettings.ReadSnapshot();
            JObject data = snapshot.ToJson();
            data["configRevision"] = PlayerRuntimeSettings.ConfigRevision;
            data["patchActive"] = PlayerRuntimeSettings.PatchActive;
            data["persistenceReady"] = PlayerRuntimeSettings.PersistenceReady;
            data["inventoryModeLabel"] = InventoryModeLabel(snapshot.ConfiguredInventoryMode);
            return Ok(request, "Read current player settings.", data);
        }

        private string PreviewPlayerSettings(BridgeRequest request)
        {
            RuntimeState state;
            string gate = RequirePlayerMutationState(out state);
            if (gate != null)
                return Fail(request, "mutation_not_ready", gate);

            int inventoryMode = request.Arguments.Value<int?>("inventoryMode") ?? 0;
            float speedMultiplier = request.Arguments.Value<float?>("speedMultiplier") ?? float.NaN;
            string swapHotkey = request.Arguments.Value<string>("swapHotkey") ?? string.Empty;
            if (inventoryMode < 1 || inventoryMode > 4)
                return Fail(request, "invalid_args", "inventoryMode must be 1, 2, 3, or 4 (on-demand pages, capped at 8 pages).");
            if (float.IsNaN(speedMultiplier) || float.IsInfinity(speedMultiplier) || speedMultiplier < 0.1f || speedMultiplier > 10f)
                return Fail(request, "invalid_args", "speedMultiplier must be finite and between 0.1 and 10.0.");
            if (!InventoryPagingModel.TryNormalizeSwapHotkey(swapHotkey, out string normalizedSwapHotkey))
                return Fail(request, "invalid_args", "swapHotkey is not a supported keyboard key.");

            PlayerSettingsSnapshot current = PlayerRuntimeSettings.ReadSnapshot();
            PlayerSettingsPreview preview = new PlayerSettingsPreview
            {
                Id = Guid.NewGuid().ToString("N"),
                ExpectedRevision = revision,
                ExpectedConfigRevision = PlayerRuntimeSettings.ConfigRevision,
                ExpiresUtc = DateTime.UtcNow.AddSeconds(60),
                OldInventoryMode = current.ConfiguredInventoryMode,
                NewInventoryMode = inventoryMode,
                OldSpeedMultiplier = current.ConfiguredSpeedMultiplier,
                NewSpeedMultiplier = speedMultiplier,
                OldSwapHotkey = current.ConfiguredSwapHotkey,
                NewSwapHotkey = normalizedSwapHotkey,
                OldInventorySlotCount = current.InventorySlotCount,
                NewConfiguredPageCount = InventoryPagingModel.ConfiguredPageCountForMode(inventoryMode),
                NewAllocatedPageCount = inventoryMode == 4
                    ? Math.Max(1, Math.Min(InventoryPagingModel.Mode4PageCap, current.AllocatedPageCount))
                    : inventoryMode,
                NewInventorySlotCount = InventoryPagingModel.CapacityForMode(inventoryMode, inventoryMode == 4 ? InventoryPagingModel.Mode4PageCap : inventoryMode),
                NewNativeHotbarSlots = current.NativeHotbarSlots,
                NewInventoryReady = current.InventoryReady,
                BaseInventorySlots = current.BaseInventorySlots
            };
            lock (playerSettingsPreviews)
            {
                playerSettingsPreviews[preview.Id] = preview;
                if (playerSettingsPreviews.Count > 16)
                {
                    string oldestId = null;
                    DateTime oldest = DateTime.MaxValue;
                    foreach (KeyValuePair<string, PlayerSettingsPreview> pair in playerSettingsPreviews)
                    {
                        if (pair.Value.ExpiresUtc < oldest)
                        {
                            oldest = pair.Value.ExpiresUtc;
                            oldestId = pair.Key;
                        }
                    }
                    if (oldestId != null)
                        playerSettingsPreviews.Remove(oldestId);
                }
            }
            JObject data = preview.ToJson();
            data["previewId"] = preview.Id;
            data["expectedRevision"] = preview.ExpectedRevision;
            data["expectedConfigRevision"] = preview.ExpectedConfigRevision;
            data["expiresUtc"] = preview.ExpiresUtc.ToString("o", CultureInfo.InvariantCulture);
            return Ok(request, "Player settings preview ready; no change was made.", data);
        }

        private string ApplyPlayerSettings(BridgeRequest request)
        {
            string previewId = request.Arguments.Value<string>("previewId") ?? string.Empty;
            long? expectedRevision = request.Arguments.Value<long?>("expectedRevision");
            long? expectedConfigRevision = request.Arguments.Value<long?>("expectedConfigRevision");
            if (!PipeServer.IsSafeIdentifier(previewId, 64) || !expectedRevision.HasValue || !expectedConfigRevision.HasValue)
                return Fail(request, "invalid_args", "previewId, expectedRevision, and expectedConfigRevision are required.");

            PlayerSettingsPreview preview = null;
            lock (playerSettingsPreviews)
            {
                if (playerSettingsPreviews.TryGetValue(previewId, out preview))
                    playerSettingsPreviews.Remove(previewId);
            }
            if (preview == null || preview.ExpiresUtc < DateTime.UtcNow)
                return Fail(request, "preview_not_found", "The player settings preview was not found or has expired.");
            if (expectedRevision.Value != revision || expectedConfigRevision.Value != PlayerRuntimeSettings.ConfigRevision)
                return Fail(request, "revision_conflict", "The player settings preview is stale; refresh and preview again.");

            RuntimeState state;
            string gate = RequirePlayerMutationState(out state);
            if (gate != null)
                return Fail(request, "mutation_not_ready", gate);
            PlayerSettingsSnapshot current = PlayerRuntimeSettings.ReadSnapshot();
            if (current.ConfiguredInventoryMode != preview.OldInventoryMode
                || Math.Abs(current.ConfiguredSpeedMultiplier - preview.OldSpeedMultiplier) > 0.0001f
                || !string.Equals(current.ConfiguredSwapHotkey, preview.OldSwapHotkey, StringComparison.OrdinalIgnoreCase))
                return Fail(request, "state_changed", "Player settings changed after the preview; refresh and preview again.");

            if (request.DryRun)
                return Ok(request, "Player settings preconditions passed; no change was made because dryRun is true.", preview.ToJson());

            if (!PlayerRuntimeSettings.ApplySettings(preview.NewInventoryMode, preview.NewSpeedMultiplier, preview.NewSwapHotkey, out string error))
                return Fail(request, "player_settings_apply_failed", error);

            revision++;
            audit(string.Format(CultureInfo.InvariantCulture, "op=player.settings.applyPreview previewId={0} revision={1} inventoryMode={2} speedMultiplier={3:0.###} swapHotkey={4}", previewId, revision, preview.NewInventoryMode, preview.NewSpeedMultiplier, preview.NewSwapHotkey));
            PlayerSettingsSnapshot applied = PlayerRuntimeSettings.ReadSnapshot();
            JObject data = applied.ToJson();
            data["configRevision"] = PlayerRuntimeSettings.ConfigRevision;
            data["inventoryModeLabel"] = InventoryModeLabel(applied.ConfiguredInventoryMode);
            return Ok(request, "Player inventory and movement settings were applied and persisted.", data);
        }

        private static string InventoryModeLabel(int mode)
        {
            switch (mode)
            {
                case 2: return "2x";
                case 3: return "3x";
                case 4: return "on-demand pages (8-page cap)";
                default: return "1x";
            }
        }

        private string RequirePlayerMutationState(out RuntimeState state)
        {
            string gate = RequireMutationState(true, out state);
            if (gate != null)
                return gate;
            if (!state.RemoteClientCountKnown || state.RemoteClientCount != 0)
                return "Player inventory and speed settings require a solo host with zero remote players.";
            if (!PlayerRuntimeSettings.PatchActive || !PlayerRuntimeSettings.PersistenceReady)
                return "Player runtime settings are unavailable for this game build.";
            return null;
        }

        private string OwnProperty(BridgeRequest request)
        {
            RuntimeState state;
            string gate = RequireMutationState(false, out state);
            if (gate != null)
                return Fail(request, "mutation_not_ready", gate);

            string code = request.Arguments.Value<string>("propertyCode") ?? string.Empty;
            if (!PipeServer.IsSafeIdentifier(code, 48))
                return Fail(request, "invalid_property_code", "propertyCode must be 1-48 letters, digits, dots, underscores, or hyphens.");
            if (!PropertyManager.InstanceExists || PropertyManager.Instance == null)
                return Fail(request, "property_manager_unavailable", "PropertyManager is not available.");

            Property property = PropertyManager.Instance.GetProperty(code);
            if (property == null)
                return Fail(request, "property_not_found", "No property matched the allowlisted property code.");
            if (property.IsOwned)
                return Ok(request, "Property is already owned.", new JObject { ["propertyCode"] = property.PropertyCode, ["isOwned"] = true });

            long? expectedRevision = request.Arguments.Value<long?>("expectedRevision");
            if (!request.DryRun && (!expectedRevision.HasValue || expectedRevision.Value != revision))
                return Fail(request, "revision_conflict", "expectedRevision must match the current bridge revision.");

            if (request.DryRun)
                return Ok(request, "Property ownership preconditions passed; no change was made because dryRun is true.", new JObject
                {
                    ["propertyCode"] = property.PropertyCode,
                    ["isOwned"] = false,
                    ["wouldOwn"] = true
                });

            property.SetOwned();
            if (!property.IsOwned)
                return Fail(request, "ownership_verification_failed", "The vanilla ownership call returned, but IsOwned did not become true.");

            revision++;
            audit(string.Format(CultureInfo.InvariantCulture, "op=property.own propertyCode={0} revision={1}", property.PropertyCode, revision));
            return Ok(request, "Property acquired through the vanilla ownership path.", new JObject
            {
                ["propertyCode"] = property.PropertyCode,
                ["isOwned"] = true
            });
        }

        private string RequireMutationState(bool requireHost, out RuntimeState state)
        {
            state = ReadRuntimeState();
            if (!IsKnownBuild())
                return "Mutations are disabled because the game version or build hashes are unknown.";
            if (!state.SaveReady)
                return "A fully loaded, save-ready game is required.";
            if (!state.IsServer)
                return "Server authority is required.";
            if (requireHost && !state.IsHost)
                return "Host authority is required for this operation.";
            return null;
        }

        private bool TryEnsureMarketEligible(RuntimeState state, ProductManager manager, out string error)
        {
            error = null;
            if (!IsKnownBuild())
            {
                error = "Fair-market overrides are disabled because the game version or build hashes are unknown.";
                return false;
            }
            if (!MarketValueScaling.PatchActive || !MarketValueScaling.PersistenceReady)
            {
                error = "The fair-market patch or its fixed persistence store is unavailable.";
                return false;
            }
            if (!state.SaveLoaded || string.IsNullOrEmpty(state.SavePath))
            {
                error = "A loaded save is required for fair-market overrides.";
                return false;
            }
            if (!state.IsHost || !state.IsServer)
            {
                error = "Solo host/server authority is required for fair-market overrides.";
                return false;
            }
            if (!state.RemoteClientCountKnown || state.RemoteClientCount != 0)
            {
                error = "Fair-market values are local and not replicated; remote-client state must be known and contain zero remote players.";
                return false;
            }
            if (!state.SaveReady && !MarketValueScaling.IsActiveFor(state.SavePath, manager))
            {
                error = "The save must be fully ready before fair-market overrides are first activated.";
                return false;
            }

            MarketValueScaling.SetEligibility(true);
            return MarketValueScaling.EnsureSave(state.SavePath, manager, out error);
        }

        private bool TryEnsurePriceLimitEligible(RuntimeState state, out string error)
        {
            error = null;
            if (!IsKnownBuild())
            {
                error = "Deal-total-limit control is disabled because the game version or build hashes are unknown.";
                return false;
            }
            if (!SellPriceLimitManager.PatchActive || !SellPriceLimitManager.PersistenceReady)
            {
                error = "The reviewed deal-total patches or their fixed persistence store are unavailable.";
                return false;
            }
            if (!state.SaveReady || !state.SaveLoaded)
            {
                error = "A fully loaded, save-ready game is required for deal-total control.";
                return false;
            }
            if (!state.IsHost || !state.IsServer)
            {
                error = "Solo host/server authority is required for deal-total control.";
                return false;
            }
            if (!state.RemoteClientCountKnown || state.RemoteClientCount != 0)
            {
                error = "Deal-total overrides are restricted to a known zero-remote-player session.";
                return false;
            }
            SellPriceLimitManager.SetEligibility(true);
            return SellPriceLimitManager.EnsureApplied(out error);
        }

        private bool TryEnsureAllowanceEligible(RuntimeState state, out string error)
        {
            error = null;
            if (!IsKnownBuild())
            {
                error = "Customer-allowance overrides are disabled because the game version or build hashes are unknown.";
                return false;
            }
            if (!CustomerAllowanceScaling.PatchActive || !CustomerAllowanceScaling.PersistenceReady)
            {
                error = "The customer-allowance patch or its fixed persistence store is unavailable.";
                return false;
            }
            if (!state.SaveLoaded || string.IsNullOrEmpty(state.SavePath))
            {
                error = "A loaded save is required for customer-allowance overrides.";
                return false;
            }
            if (!state.IsHost || !state.IsServer)
            {
                error = "Solo host/server authority is required for customer-allowance overrides.";
                return false;
            }
            if (!state.RemoteClientCountKnown || state.RemoteClientCount != 0)
            {
                error = "Customer allowances are local and not replicated; remote-client state must be known and contain zero remote players.";
                return false;
            }
            if (!state.SaveReady && !CustomerAllowanceScaling.IsActiveFor(state.SavePath))
            {
                error = "The save must be fully ready before customer-allowance overrides are first activated.";
                return false;
            }

            CustomerAllowanceScaling.SetEligibility(true);
            return CustomerAllowanceScaling.EnsureSave(state.SavePath, out error);
        }

        private bool TryEnsureLaunderEligible(RuntimeState state, out string error)
        {
            error = null;
            if (!IsKnownBuild())
            {
                error = "Launder-limit overrides are disabled because the game version or build hashes are unknown.";
                return false;
            }
            if (!BusinessLaunderScaling.PatchActive || !BusinessLaunderScaling.PersistenceReady)
            {
                error = "The launder-limit patch or its fixed persistence store is unavailable.";
                return false;
            }
            if (!state.SaveLoaded || string.IsNullOrEmpty(state.SavePath))
            {
                error = "A loaded save is required for launder-limit overrides.";
                return false;
            }
            if (!state.IsHost || !state.IsServer)
            {
                error = "Solo host/server authority is required for launder-limit overrides.";
                return false;
            }
            if (!state.RemoteClientCountKnown || state.RemoteClientCount != 0)
            {
                error = "Launder-limit state is local and not replicated; remote-client state must be known and contain zero remote players.";
                return false;
            }
            if (!state.SaveReady && !string.Equals(BusinessLaunderScaling.ActiveSaveScope, MarketValueScaling.ComputeSaveScope(state.SavePath), StringComparison.Ordinal))
            {
                error = "The save must be fully ready before launder-limit overrides are first activated.";
                return false;
            }
            BusinessLaunderScaling.SetEligibility(true);
            return BusinessLaunderScaling.EnsureSave(state.SavePath, out error);
        }

        private bool TryEnsureEffectsEligible(RuntimeState state, out string error)
        {
            error = null;
            if (!IsKnownBuild())
            {
                error = "Effect overrides are disabled because the game version or build hashes are unknown.";
                return false;
            }
            if (!EffectsIntensityManager.PersistenceReady)
            {
                error = "The effect profile store is unavailable.";
                return false;
            }
            if (!state.SaveLoaded || string.IsNullOrEmpty(state.SavePath))
            {
                error = "A loaded save is required for effect overrides.";
                return false;
            }
            if (!state.IsHost || !state.IsServer)
            {
                error = "Solo host/server authority is required for effect overrides.";
                return false;
            }
            if (!state.RemoteClientCountKnown || state.RemoteClientCount != 0)
            {
                error = "Effect profiles are local and not replicated; remote-client state must be known and contain zero remote players.";
                return false;
            }
            if (!state.SaveReady && !string.Equals(EffectsIntensityManager.ActiveSaveScope, MarketValueScaling.ComputeSaveScope(state.SavePath), StringComparison.Ordinal))
            {
                error = "The save must be fully ready before effect overrides are first activated.";
                return false;
            }
            EffectsIntensityManager.SetEligibility(true);
            return EffectsIntensityManager.EnsureSave(state.SavePath, out error);
        }

        private static bool TryReadLaunderTargets(JObject arguments, out Dictionary<string, int> targets, out string error)
        {
            targets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            error = null;
            JArray array = arguments == null ? null : arguments["targets"] as JArray;
            if (array == null || array.Count == 0 || array.Count > 64)
            {
                error = "Launder-limit previews require targets containing 1-64 businessCode/limit objects.";
                return false;
            }
            foreach (JToken token in array)
            {
                JObject item = token as JObject;
                if (item == null)
                {
                    error = "Each launder-limit target must be an object.";
                    return false;
                }
                string code = item.Value<string>("businessCode") ?? string.Empty;
                int? limit = item.Value<int?>("limit");
                if (!PipeServer.IsSafeIdentifier(code, 48)
                    || !limit.HasValue
                    || (limit.Value != 0
                        && (limit.Value < BusinessLaunderScaling.MinimumDailyLimit
                            || limit.Value > BusinessLaunderScaling.HardMaximumDailyLimit)))
                {
                    error = "Each launder-limit target needs a safe businessCode and a limit of 0 (restore native) or a whole number from 1 to 16777215.";
                    return false;
                }
                if (targets.ContainsKey(code))
                {
                    error = "Launder-limit targets must not contain duplicate business codes.";
                    return false;
                }
                targets[code] = limit.Value;
            }
            return true;
        }

        private static bool TryReadEffectTargets(JObject arguments, out List<EffectTarget> targets, out string error)
        {
            targets = new List<EffectTarget>();
            error = null;
            JArray array = arguments == null ? null : arguments["targets"] as JArray;
            if (array == null || array.Count == 0 || array.Count > 64)
            {
                error = "Effect previews require targets containing 1-64 effect objects.";
                return false;
            }
            foreach (JToken token in array)
            {
                JObject item = token as JObject;
                if (item == null)
                {
                    error = "Each effect target must be an object.";
                    return false;
                }
                string effectId = item.Value<string>("effectId") ?? string.Empty;
                if (!PipeServer.IsSafeIdentifier(effectId, 64))
                {
                    error = "Each effect target needs a safe effectId.";
                    return false;
                }
                EffectTarget target = new EffectTarget { EffectId = effectId };
                JToken valueChangeToken = item["valueChange"];
                if (valueChangeToken != null)
                {
                    int? valueChange = valueChangeToken.Type == JTokenType.Integer ? valueChangeToken.Value<int?>() : null;
                    if (!valueChange.HasValue || valueChange.Value < -SellPriceLimitManager.PracticalMoneyMaximum || valueChange.Value > SellPriceLimitManager.PracticalMoneyMaximum)
                    {
                        error = "valueChange must be a whole number from -16777215 to 16777215.";
                        return false;
                    }
                    target.ValueChange = valueChange.Value;
                }
                float parsed;
                if (item["valueMultiplier"] != null)
                {
                    if (!TryReadFiniteFloat(item["valueMultiplier"], out parsed) || parsed < 0f || parsed > 1000000f)
                    {
                        error = "valueMultiplier must be a finite number from 0 to 1,000,000.";
                        return false;
                    }
                    target.ValueMultiplier = parsed;
                }
                if (item["addBaseValueMultiple"] != null)
                {
                    if (!TryReadFiniteFloat(item["addBaseValueMultiple"], out parsed) || parsed < -1000000f || parsed > 1000000f)
                    {
                        error = "addBaseValueMultiple must be a finite number from -1,000,000 to 1,000,000.";
                        return false;
                    }
                    target.AddBaseValueMultiple = parsed;
                }
                JToken tierToken = item["tier"];
                if (tierToken != null)
                {
                    int? tier = tierToken.Type == JTokenType.Integer ? tierToken.Value<int?>() : null;
                    if (!tier.HasValue || tier.Value < 0 || tier.Value > 5)
                    {
                        error = "tier must be a whole number from 0 to 5.";
                        return false;
                    }
                    target.Tier = tier.Value;
                }
                JArray parameters = item["parameters"] as JArray;
                if (parameters != null)
                {
                    target.Parameters = new List<EffectParameter>();
                    foreach (JToken paramToken in parameters)
                    {
                        JObject paramItem = paramToken as JObject;
                        if (paramItem == null)
                        {
                            error = "Each effect parameter must be an object.";
                            return false;
                        }
                        string name = paramItem.Value<string>("name") ?? string.Empty;
                        if (!PipeServer.IsSafeIdentifier(name, 48) || !TryReadFiniteFloat(paramItem["value"], out parsed))
                        {
                            error = "Each effect parameter needs a safe name and a finite value.";
                            return false;
                        }
                        target.Parameters.Add(new EffectParameter { Name = name, Value = parsed });
                    }
                }
                targets.Add(target);
            }
            return true;
        }

        private bool IsReviewedBuild()
        {
            return fingerprint.FilesMatch && string.Equals(Application.version, ExpectedGameVersion, StringComparison.Ordinal);
        }

        private bool IsKnownBuild()
        {
            return IsReviewedBuild() || compatibilityModeEnabled();
        }

        private RuntimeState ReadRuntimeState()
        {
            RuntimeState state = new RuntimeState();
            try
            {
                state.IsServer = InstanceFinder.IsServer;
                state.IsClient = InstanceFinder.IsClient;
                state.IsHost = InstanceFinder.IsHost;

                var networkManager = InstanceFinder.NetworkManager;
                if (networkManager != null)
                {
                    state.IsServer = networkManager.ServerManager != null && networkManager.ServerManager.Started;
                    state.IsClient = networkManager.ClientManager != null && networkManager.ClientManager.Started;
                    state.IsHost = networkManager.IsHost && state.IsServer && state.IsClient;
                    if (networkManager.ServerManager != null && networkManager.ServerManager.Clients != null)
                    {
                        state.RemoteClientCountKnown = true;
                        state.RemoteClientCount = 0;
                        foreach (NetworkConnection connection in networkManager.ServerManager.Clients.Values)
                        {
                            if (connection != null && !connection.IsLocalClient)
                                state.RemoteClientCount++;
                        }
                    }
                }
            }
            catch
            {
                state.IsServer = false;
                state.IsClient = false;
                state.IsHost = false;
            }

            if (!LoadManager.InstanceExists || LoadManager.Instance == null)
                return state;

            LoadManager load = LoadManager.Instance;
            state.SaveLoaded = load.IsInGameScene && load.IsGameLoaded && !load.IsLoading;
            if (load.ActiveSaveInfo != null)
                state.SavePath = load.ActiveSaveInfo.SavePath ?? string.Empty;
            if (string.IsNullOrEmpty(state.SavePath))
                state.SavePath = load.LoadedGameFolderPath ?? string.Empty;

            bool saveManagerReady = SaveManager.InstanceExists
                && SaveManager.Instance != null
                && SaveManager.Instance.saveFolderInitialized
                && !SaveManager.Instance.IsSaving;
            state.SaveReady = state.SaveLoaded
                && load.ActiveSaveInfo != null
                && !string.IsNullOrEmpty(load.ActiveSaveInfo.SavePath)
                && !string.IsNullOrEmpty(load.LoadedGameFolderPath)
                && saveManagerReady;
            return state;
        }

        private static bool TryGetProductManager(bool requireServerObject, out ProductManager manager, out string error)
        {
            manager = null;
            error = null;
            if (!ProductManager.InstanceExists || ProductManager.Instance == null)
            {
                error = "ProductManager is not available.";
                return false;
            }

            manager = ProductManager.Instance;
            if (manager.AllProducts == null)
            {
                error = "ProductManager has no live product list yet.";
                manager = null;
                return false;
            }
            if (requireServerObject && !manager.IsServerInitialized)
            {
                error = "The ProductManager server object is not initialized.";
                manager = null;
                return false;
            }
            return true;
        }

        private static bool TryReadProductFilter(JObject arguments, out ProductFilter filter, out string error)
        {
            filter = new ProductFilter();
            error = null;

            string requestedDrug = arguments.Value<string>("drugType");
            if (!string.IsNullOrEmpty(requestedDrug) && !string.Equals(requestedDrug, "All", StringComparison.OrdinalIgnoreCase))
            {
                EDrugType drugType;
                if (!TryParseDrugType(requestedDrug, out drugType))
                {
                    error = "drugType must be Weed/Marijuana, Meth/Methamphetamine, Cocaine, MDMA, Shrooms, Heroin, or All.";
                    return false;
                }
                filter.DrugType = drugType;
            }

            JToken idsToken = arguments["productIds"];
            if (idsToken != null)
            {
                JArray ids = idsToken as JArray;
                if (ids == null || ids.Count == 0 || ids.Count > 64)
                {
                    error = "productIds must be an array containing 1-64 product ids.";
                    return false;
                }
                filter.ProductIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (JToken token in ids)
                {
                    string id = token.Type == JTokenType.String ? token.Value<string>() : null;
                    if (!PipeServer.IsSafeIdentifier(id, 64))
                    {
                        error = "Each product id must be 1-64 letters, digits, dots, underscores, or hyphens.";
                        return false;
                    }
                    filter.ProductIds.Add(id);
                }
            }
            return true;
        }

        private static bool TryReadManualMarketTargets(
            JObject arguments,
            out Dictionary<string, float> targets,
            out string error)
        {
            targets = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            error = null;
            JArray array = arguments == null ? null : arguments["targets"] as JArray;
            if (array == null || array.Count == 0 || array.Count > 64)
            {
                error = "explicitValues mode requires targets containing 1-64 productId/marketValue objects.";
                return false;
            }

            foreach (JToken token in array)
            {
                JObject item = token as JObject;
                if (item == null)
                {
                    error = "Each manual fair-market target must be an object.";
                    return false;
                }
                string id = item["productId"] != null && item["productId"].Type == JTokenType.String
                    ? item.Value<string>("productId")
                    : null;
                float value;
                if (!PipeServer.IsSafeIdentifier(id, 64)
                    || !TryReadFiniteFloat(item["marketValue"], out value)
                    || value < 0f
                    || value > MarketValueScaling.MaxMarketValue)
                {
                    error = "Each manual fair-market target needs a safe productId and a finite marketValue from 0 to 16777215.";
                    return false;
                }
                if (targets.ContainsKey(id))
                {
                    error = "Manual fair-market targets must not contain duplicate product ids.";
                    return false;
                }
                targets[id] = value;
            }
            return true;
        }

        private static bool TryReadManualPriceTargets(
            JObject arguments,
            out Dictionary<string, float> targets,
            out string error)
        {
            targets = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            error = null;
            JArray array = arguments == null ? null : arguments["targets"] as JArray;
            if (array == null || array.Count == 0 || array.Count > 64)
            {
                error = "explicitValues mode requires targets containing 1-64 productId/price objects.";
                return false;
            }

            foreach (JToken token in array)
            {
                JObject item = token as JObject;
                if (item == null)
                {
                    error = "Each manual sell-price target must be an object.";
                    return false;
                }
                string id = item["productId"] != null && item["productId"].Type == JTokenType.String
                    ? item.Value<string>("productId")
                    : null;
                float value;
                if (!PipeServer.IsSafeIdentifier(id, 64)
                    || !TryReadFiniteFloat(item["price"], out value)
                    || value < 1f
                    || value > SellPriceLimitManager.PracticalMoneyMaximum
                    || Math.Abs(value - Math.Round(value, 0, MidpointRounding.AwayFromZero)) > PriceTolerance)
                {
                    error = "Each manual sell-price target needs a safe productId and a whole-dollar price from 1 to 16777215; the active runtime maximum is checked during preview.";
                    return false;
                }
                if (targets.ContainsKey(id))
                {
                    error = "Manual sell-price targets must not contain duplicate product ids.";
                    return false;
                }
                targets[id] = value;
            }
            return true;
        }

        private static bool TryReadManualAllowanceTargets(
            JObject arguments,
            out Dictionary<string, AllowanceRange> targets,
            out string error)
        {
            targets = new Dictionary<string, AllowanceRange>(StringComparer.OrdinalIgnoreCase);
            error = null;
            JArray array = arguments == null ? null : arguments["targets"] as JArray;
            if (array == null || array.Count == 0 || array.Count > CustomerAllowanceScaling.MaxManualTargetsPerRequest)
            {
                error = "explicitValues mode requires targets containing 1-96 customer allowance objects.";
                return false;
            }

            foreach (JToken token in array)
            {
                JObject item = token as JObject;
                if (item == null)
                {
                    error = "Each manual customer-allowance target must be an object.";
                    return false;
                }
                string id = item["customerId"] != null && item["customerId"].Type == JTokenType.String
                    ? item.Value<string>("customerId")
                    : null;
                float min;
                float max;
                if (!PipeServer.IsSafeIdentifier(id, 64)
                    || !TryReadFiniteFloat(item["minWeeklySpend"], out min)
                    || !TryReadFiniteFloat(item["maxWeeklySpend"], out max)
                    || !IsValidAllowanceRange(min, max))
                {
                    error = "Each manual allowance target needs a safe customerId and finite min/max weekly spend with 0 <= min <= max <= 16777215.";
                    return false;
                }
                if (targets.ContainsKey(id))
                {
                    error = "Manual customer-allowance targets must not contain duplicate customer ids.";
                    return false;
                }
                targets[id] = new AllowanceRange { MinWeeklySpend = min, MaxWeeklySpend = max };
            }
            return true;
        }

        private static bool TryReadIncludeLocked(JObject arguments, out bool includeLocked, out string error)
        {
            includeLocked = false;
            error = null;
            JToken token = arguments == null ? null : arguments["includeLocked"];
            if (token == null)
                return true;
            if (token.Type != JTokenType.Boolean)
            {
                error = "includeLocked must be a Boolean when supplied.";
                return false;
            }
            includeLocked = token.Value<bool>();
            return true;
        }

        private static bool TryReadFiniteFloat(JToken token, out float value)
        {
            value = 0f;
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
                return false;
            double number;
            try
            {
                number = token.Value<double>();
            }
            catch
            {
                return false;
            }
            if (double.IsNaN(number) || double.IsInfinity(number)
                || number < -float.MaxValue || number > float.MaxValue)
                return false;
            value = (float)number;
            return IsFinite(value);
        }

        private static bool IsValidAllowanceRange(float min, float max)
        {
            return IsFinite(min)
                && IsFinite(max)
                && min >= CustomerAllowanceScaling.MinWeeklySpend
                && max >= min
                && max <= CustomerAllowanceScaling.MaxWeeklySpend;
        }

        private static bool CanRepresentAllowanceRange(LiveCustomerAllowance customer, float min, float max)
        {
            if (customer == null || customer.Data == null || !IsValidAllowanceRange(min, max))
                return false;
            try
            {
                float atMinimum = CustomerAllowanceScaling.CalculateForRange(customer.Data, 0f, min, max);
                float atMaximum = CustomerAllowanceScaling.CalculateForRange(customer.Data, 1f, min, max);
                return IsFinite(atMinimum) && atMinimum >= 0f
                    && IsFinite(atMaximum) && atMaximum >= 0f;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadAllowanceMetrics(
            LiveCustomerAllowance customer,
            float minWeeklySpend,
            float maxWeeklySpend,
            out AllowanceMetrics metrics,
            out string error)
        {
            metrics = null;
            error = null;
            if (customer == null || customer.Customer == null || customer.Data == null
                || customer.Customer.NPC == null || customer.Customer.NPC.RelationData == null)
            {
                error = "A live customer is missing allowance or relationship data.";
                return false;
            }
            if (!IsValidAllowanceRange(minWeeklySpend, maxWeeklySpend))
            {
                error = "Customer " + customer.Id + " has an invalid requested allowance range.";
                return false;
            }

            try
            {
                float relationship = Clamp01(customer.Customer.NPC.RelationData.NormalizedRelationDelta);
                float addiction = customer.Customer.CurrentAddiction;
                float adjusted = CustomerAllowanceScaling.CalculateForRange(
                    customer.Data,
                    relationship,
                    minWeeklySpend,
                    maxWeeklySpend);
                var orderDays = new Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.GameTime.EDay>();
                customer.Data.GetOrderDays(addiction, relationship, orderDays);
                int orders = orderDays.Count;
                float perOrder = orders > 0 ? adjusted / orders : float.NaN;
                float hardLimit = perOrder * CustomerAllowanceScaling.HardOfferLimitMultiplier;
                if (!IsFinite(relationship) || !IsFinite(addiction)
                    || !IsFinite(adjusted) || adjusted < 0f
                    || orders <= 0 || orders > 7
                    || !IsFinite(perOrder) || perOrder < 0f
                    || !IsFinite(hardLimit) || hardLimit < 0f)
                {
                    error = "Customer " + customer.Id + " produced invalid allowance or order-cadence values.";
                    return false;
                }
                metrics = new AllowanceMetrics
                {
                    Relationship = relationship,
                    Addiction = addiction,
                    AdjustedWeeklySpend = adjusted,
                    OrdersPerWeek = orders,
                    AllowancePerOrder = perOrder,
                    HardOfferLimit = hardLimit
                };
                return true;
            }
            catch (Exception ex)
            {
                error = "Could not calculate allowance metrics for " + customer.Id + ": "
                    + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static JObject CustomerAllowanceRow(LiveCustomerAllowance customer, AllowanceMetrics metrics)
        {
            return new JObject
            {
                ["customerId"] = customer.Id,
                ["name"] = customer.Name,
                ["unlocked"] = customer.Unlocked,
                ["originalMinWeeklySpend"] = customer.OriginalMinWeeklySpend,
                ["originalMaxWeeklySpend"] = customer.OriginalMaxWeeklySpend,
                ["currentMinWeeklySpend"] = customer.CurrentMinWeeklySpend,
                ["currentMaxWeeklySpend"] = customer.CurrentMaxWeeklySpend,
                ["adjustedWeeklySpend"] = metrics.AdjustedWeeklySpend,
                ["ordersPerWeek"] = metrics.OrdersPerWeek,
                ["allowancePerOrder"] = metrics.AllowancePerOrder,
                ["hardOfferLimit"] = metrics.HardOfferLimit,
                ["overridden"] = !NearlyEqualAllowance(customer.CurrentMinWeeklySpend, customer.OriginalMinWeeklySpend)
                    || !NearlyEqualAllowance(customer.CurrentMaxWeeklySpend, customer.OriginalMaxWeeklySpend)
            };
        }

        private static Dictionary<string, AllowanceRange> CloneAllowanceRanges(Dictionary<string, AllowanceRange> source)
        {
            Dictionary<string, AllowanceRange> result =
                new Dictionary<string, AllowanceRange>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, AllowanceRange> pair in source)
                result[pair.Key] = pair.Value.Clone();
            return result;
        }

        private static bool VerifyAllowanceReadback(
            List<AllowanceChange> changes,
            bool verifyOriginal,
            out JArray readback,
            out string error)
        {
            readback = new JArray();
            error = null;
            string snapshotError;
            List<LiveCustomerAllowance> customers = CustomerAllowanceScaling.SnapshotCustomers(true, out snapshotError);
            if (snapshotError != null)
            {
                error = snapshotError;
                return false;
            }
            Dictionary<string, LiveCustomerAllowance> map = new Dictionary<string, LiveCustomerAllowance>(StringComparer.OrdinalIgnoreCase);
            foreach (LiveCustomerAllowance customer in customers)
                map[customer.Id] = customer;

            bool verified = true;
            foreach (AllowanceChange change in changes)
            {
                LiveCustomerAllowance customer;
                if (!map.TryGetValue(change.CustomerId, out customer))
                {
                    verified = false;
                    continue;
                }

                float expectedMin = verifyOriginal ? change.ExpectedCurrentMinWeeklySpend : change.NewMinWeeklySpend;
                float expectedMax = verifyOriginal ? change.ExpectedCurrentMaxWeeklySpend : change.NewMaxWeeklySpend;
                float expectedAdjusted = verifyOriginal ? change.CurrentAdjustedWeeklySpend : change.PlannedAdjustedWeeklySpend;
                float expectedPerOrder = verifyOriginal ? change.CurrentAllowancePerOrder : change.PlannedAllowancePerOrder;
                float expectedHardLimit = verifyOriginal ? change.CurrentHardOfferLimit : change.PlannedHardOfferLimit;
                AllowanceMetrics metrics;
                string metricsError;
                if (!TryReadAllowanceMetrics(customer, customer.CurrentMinWeeklySpend, customer.CurrentMaxWeeklySpend, out metrics, out metricsError))
                {
                    verified = false;
                    continue;
                }

                float actualAdjusted;
                try
                {
                    actualAdjusted = customer.Data.GetAdjustedWeeklySpend(metrics.Relationship);
                }
                catch
                {
                    actualAdjusted = float.NaN;
                }
                float actualPerOrder = metrics.OrdersPerWeek > 0 ? actualAdjusted / metrics.OrdersPerWeek : float.NaN;
                float actualHardLimit = actualPerOrder * CustomerAllowanceScaling.HardOfferLimitMultiplier;
                if (customer.Data.Pointer.ToInt64() != change.DataPointer
                    || !NearlyEqualAllowance(customer.CurrentMinWeeklySpend, expectedMin)
                    || !NearlyEqualAllowance(customer.CurrentMaxWeeklySpend, expectedMax)
                    || !NearlyEqualAllowance(metrics.Relationship, change.Relationship)
                    || !NearlyEqualAllowance(metrics.Addiction, change.Addiction)
                    || metrics.OrdersPerWeek != change.OrdersPerWeek
                    || !NearlyEqualAllowance(actualAdjusted, expectedAdjusted)
                    || !NearlyEqualAllowance(actualPerOrder, expectedPerOrder)
                    || !NearlyEqualAllowance(actualHardLimit, expectedHardLimit))
                    verified = false;

                JObject row = change.ToJson();
                row["actualMinWeeklySpend"] = customer.CurrentMinWeeklySpend;
                row["actualMaxWeeklySpend"] = customer.CurrentMaxWeeklySpend;
                row["actualAdjustedWeeklySpend"] = actualAdjusted;
                row["actualOrdersPerWeek"] = metrics.OrdersPerWeek;
                row["actualAllowancePerOrder"] = actualPerOrder;
                row["actualHardOfferLimit"] = actualHardLimit;
                readback.Add(row);
            }

            if (!verified)
                error = verifyOriginal
                    ? "Rollback readback did not match the previewed original allowances."
                    : "Applied allowance readback did not match the preview.";
            return verified;
        }

        private static bool NearlyEqualAllowance(float left, float right)
        {
            if (!IsFinite(left) || !IsFinite(right))
                return false;
            double scale = Math.Max(1.0, Math.Max(Math.Abs(left), Math.Abs(right)));
            return Math.Abs(left - right) <= Math.Max(0.01, scale * 0.00001);
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        private static bool TryParseDrugType(string value, out EDrugType drugType)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "weed":
                case "marijuana": drugType = EDrugType.Marijuana; return true;
                case "meth":
                case "methamphetamine": drugType = EDrugType.Methamphetamine; return true;
                case "cocaine": drugType = EDrugType.Cocaine; return true;
                case "mdma": drugType = EDrugType.MDMA; return true;
                case "shroom":
                case "shrooms": drugType = EDrugType.Shrooms; return true;
                case "heroin": drugType = EDrugType.Heroin; return true;
                default: drugType = default(EDrugType); return false;
            }
        }

        private static bool AreValidPriceBounds(float minPrice, float maxPrice)
        {
            return IsFinite(minPrice) && IsFinite(maxPrice) && minPrice >= 0f && maxPrice > minPrice && maxPrice <= SellPriceLimitManager.PracticalMoneyMaximum;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static List<LiveProduct> ReadProducts(ProductManager manager, ProductFilter filter)
        {
            List<LiveProduct> result = new List<LiveProduct>();
            var all = manager.AllProducts;
            for (int i = 0; i < all.Count && result.Count < 512; i++)
            {
                ProductDefinition product = all[i];
                if (product == null)
                    continue;
                string id = product.ID ?? string.Empty;
                if (!PipeServer.IsSafeIdentifier(id, 64))
                    continue;
                if (filter.DrugType.HasValue && product.DrugType != filter.DrugType.Value)
                    continue;
                if (filter.ProductIds != null && !filter.ProductIds.Contains(id))
                    continue;

                result.Add(new LiveProduct
                {
                    Definition = product,
                    Id = id,
                    Name = product.Name ?? string.Empty,
                    DrugType = product.DrugType.ToString(),
                    Price = manager.GetPrice(product)
                });
            }
            result.Sort((left, right) => string.Compare(left.Id, right.Id, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        private static Dictionary<string, LiveProduct> ReadProductMap(ProductManager manager)
        {
            List<LiveProduct> products = ReadProducts(manager, new ProductFilter());
            Dictionary<string, LiveProduct> map = new Dictionary<string, LiveProduct>(StringComparer.OrdinalIgnoreCase);
            foreach (LiveProduct product in products)
                map[product.Id] = product;
            return map;
        }

        private void PurgeExpiredPreviews()
        {
            DateTime now = DateTime.UtcNow;
            List<string> expired = new List<string>();
            foreach (KeyValuePair<string, PricePreview> pair in previews)
            {
                if (pair.Value.ExpiresUtc <= now || pair.Value.Revision != revision)
                    expired.Add(pair.Key);
            }
            foreach (string id in expired)
                previews.Remove(id);

            expired.Clear();
            foreach (KeyValuePair<string, PriceLimitPreview> pair in priceLimitPreviews)
            {
                if (pair.Value.ExpiresUtc <= now || pair.Value.Revision != revision)
                    expired.Add(pair.Key);
            }
            foreach (string id in expired)
                priceLimitPreviews.Remove(id);

            expired.Clear();
            foreach (KeyValuePair<string, MarketPreview> pair in marketPreviews)
            {
                if (pair.Value.ExpiresUtc <= now || pair.Value.Revision != revision)
                    expired.Add(pair.Key);
            }
            foreach (string id in expired)
                marketPreviews.Remove(id);

            expired.Clear();
            foreach (KeyValuePair<string, AllowancePreview> pair in allowancePreviews)
            {
                if (pair.Value.ExpiresUtc <= now || pair.Value.Revision != revision)
                    expired.Add(pair.Key);
            }
            foreach (string id in expired)
                allowancePreviews.Remove(id);
        }

        private void RemoveOldestPreview()
        {
            string oldestId = null;
            DateTime oldest = DateTime.MaxValue;
            foreach (KeyValuePair<string, PricePreview> pair in previews)
            {
                if (pair.Value.ExpiresUtc < oldest)
                {
                    oldest = pair.Value.ExpiresUtc;
                    oldestId = pair.Key;
                }
            }
            if (oldestId != null)
                previews.Remove(oldestId);
        }

        private void RemoveOldestMarketPreview()
        {
            string oldestId = null;
            DateTime oldest = DateTime.MaxValue;
            foreach (KeyValuePair<string, MarketPreview> pair in marketPreviews)
            {
                if (pair.Value.ExpiresUtc < oldest)
                {
                    oldest = pair.Value.ExpiresUtc;
                    oldestId = pair.Key;
                }
            }
            if (oldestId != null)
                marketPreviews.Remove(oldestId);
        }

        private void RemoveOldestPriceLimitPreview()
        {
            string oldestId = null;
            DateTime oldest = DateTime.MaxValue;
            foreach (KeyValuePair<string, PriceLimitPreview> pair in priceLimitPreviews)
            {
                if (pair.Value.ExpiresUtc < oldest)
                {
                    oldest = pair.Value.ExpiresUtc;
                    oldestId = pair.Key;
                }
            }
            if (oldestId != null)
                priceLimitPreviews.Remove(oldestId);
        }

        private void RemoveOldestAllowancePreview()
        {
            string oldestId = null;
            DateTime oldest = DateTime.MaxValue;
            foreach (KeyValuePair<string, AllowancePreview> pair in allowancePreviews)
            {
                if (pair.Value.ExpiresUtc < oldest)
                {
                    oldest = pair.Value.ExpiresUtc;
                    oldestId = pair.Key;
                }
            }
            if (oldestId != null)
                allowancePreviews.Remove(oldestId);
        }

        private string Ok(BridgeRequest request, string message, JObject data)
        {
            return ProtocolJson.Response(request.Id, true, "ok", message, revision, data);
        }

        private string Fail(BridgeRequest request, string code, string message)
        {
            return ProtocolJson.Response(request.Id, false, code, message, revision, null);
        }

        private string Fail(BridgeRequest request, string code, string message, JObject data)
        {
            return ProtocolJson.Response(request.Id, false, code, message, revision, data);
        }
    }

    internal sealed class RuntimeState
    {
        public bool SaveLoaded;
        public bool SaveReady;
        public string SavePath = string.Empty;
        public bool IsHost;
        public bool IsServer;
        public bool IsClient;
        public bool RemoteClientCountKnown;
        public int RemoteClientCount;
    }

    internal sealed class ProductFilter
    {
        public EDrugType? DrugType;
        public HashSet<string> ProductIds;
    }

    internal sealed class LiveProduct
    {
        public ProductDefinition Definition;
        public string Id;
        public string Name;
        public string DrugType;
        public float Price;

        public JObject ToJson()
        {
            return new JObject
            {
                ["productId"] = Id,
                ["name"] = Name,
                ["drugType"] = DrugType,
                ["price"] = Price
            };
        }
    }

    internal sealed class PriceChange
    {
        public string ProductId;
        public string ProductName;
        public string DrugType;
        public float ExpectedOldPrice;
        public float NewPrice;

        public JObject ToJson()
        {
            return new JObject
            {
                ["productId"] = ProductId,
                ["name"] = ProductName,
                ["drugType"] = DrugType,
                ["expectedOldPrice"] = ExpectedOldPrice,
                ["newPrice"] = NewPrice
            };
        }
    }

    internal sealed class PricePreview
    {
        public string Id;
        public long Revision;
        public DateTime ExpiresUtc;
        public string Mode;
        public double Factor;
        public float MinPrice;
        public float MaxPrice;
        public readonly List<PriceChange> Changes = new List<PriceChange>();

        public JArray ChangesJson()
        {
            JArray array = new JArray();
            foreach (PriceChange change in Changes)
                array.Add(change.ToJson());
            return array;
        }

        public JObject ToJson()
        {
            return new JObject
            {
                ["previewId"] = Id,
                ["expectedRevision"] = Revision,
                ["expiresUtc"] = ExpiresUtc.ToString("o", CultureInfo.InvariantCulture),
                ["mode"] = Mode,
                ["factor"] = Factor,
                ["minPrice"] = MinPrice,
                ["maxPrice"] = MaxPrice,
                ["count"] = Changes.Count,
                ["changes"] = ChangesJson()
            };
        }
    }

    internal sealed class PriceLimitPreview
    {
        public string Id;
        public long Revision;
        public long ConfigRevision;
        public DateTime ExpiresUtc;
        public bool ExpectedOverrideEnabled;
        public int ExpectedConfiguredMax;
        public bool NewOverrideEnabled;
        public int NewMaxDealTotal;

        public JObject ToJson()
        {
            return new JObject
            {
                ["previewId"] = Id,
                ["expectedRevision"] = Revision,
                ["expectedConfigRevision"] = ConfigRevision,
                ["expiresUtc"] = ExpiresUtc.ToString("o", CultureInfo.InvariantCulture),
                ["expectedOverrideEnabled"] = ExpectedOverrideEnabled,
                ["expectedConfiguredMaxDealTotal"] = ExpectedConfiguredMax,
                ["newOverrideEnabled"] = NewOverrideEnabled,
                ["newMaxDealTotal"] = NewMaxDealTotal
            };
        }
    }

    internal sealed class MarketChange
    {
        public string ProductId;
        public string ProductName;
        public string DrugType;
        public float ExpectedSellPrice;
        public float ExpectedVanillaMarketValue;
        public float ExpectedCurrentMarketValue;
        public float ExpectedOldFactor;
        public float NewFactor;
        public float NewMarketValue;
        public float CurrentValueProposition;
        public float PlannedValueProposition;

        public JObject ToJson()
        {
            return new JObject
            {
                ["productId"] = ProductId,
                ["name"] = ProductName,
                ["drugType"] = DrugType,
                ["expectedSellPrice"] = ExpectedSellPrice,
                ["expectedVanillaMarketValue"] = ExpectedVanillaMarketValue,
                ["expectedCurrentMarketValue"] = ExpectedCurrentMarketValue,
                ["expectedOldFactor"] = ExpectedOldFactor,
                ["newFactor"] = NewFactor,
                ["newMarketValue"] = NewMarketValue,
                ["currentValueProposition"] = CurrentValueProposition,
                ["plannedValueProposition"] = PlannedValueProposition
            };
        }
    }

    internal sealed class MarketPreview
    {
        public string Id;
        public long Revision;
        public long ConfigRevision;
        public string SaveScope;
        public DateTime ExpiresUtc;
        public string Mode;
        public float Factor;
        public readonly List<MarketChange> Changes = new List<MarketChange>();

        public JArray ChangesJson()
        {
            JArray array = new JArray();
            foreach (MarketChange change in Changes)
                array.Add(change.ToJson());
            return array;
        }

        public JObject ToJson()
        {
            return new JObject
            {
                ["previewId"] = Id,
                ["expectedRevision"] = Revision,
                ["expectedConfigRevision"] = ConfigRevision,
                ["saveScope"] = SaveScope,
                ["expiresUtc"] = ExpiresUtc.ToString("o", CultureInfo.InvariantCulture),
                ["factor"] = Factor,
                ["mode"] = Mode,
                ["count"] = Changes.Count,
                ["changes"] = ChangesJson()
            };
        }
    }

    internal sealed class AllowanceMetrics
    {
        public float Relationship;
        public float Addiction;
        public float AdjustedWeeklySpend;
        public int OrdersPerWeek;
        public float AllowancePerOrder;
        public float HardOfferLimit;
    }

    internal sealed class AllowanceChange
    {
        public string CustomerId;
        public string CustomerName;
        public bool Unlocked;
        public long DataPointer;
        public float OriginalMinWeeklySpend;
        public float OriginalMaxWeeklySpend;
        public float ExpectedCurrentMinWeeklySpend;
        public float ExpectedCurrentMaxWeeklySpend;
        public float NewMinWeeklySpend;
        public float NewMaxWeeklySpend;
        public float Relationship;
        public float Addiction;
        public int OrdersPerWeek;
        public float CurrentAdjustedWeeklySpend;
        public float CurrentAllowancePerOrder;
        public float CurrentHardOfferLimit;
        public float PlannedAdjustedWeeklySpend;
        public float PlannedAllowancePerOrder;
        public float PlannedHardOfferLimit;

        public JObject ToJson()
        {
            return new JObject
            {
                ["customerId"] = CustomerId,
                ["name"] = CustomerName,
                ["unlocked"] = Unlocked,
                ["originalMinWeeklySpend"] = OriginalMinWeeklySpend,
                ["originalMaxWeeklySpend"] = OriginalMaxWeeklySpend,
                ["expectedCurrentMinWeeklySpend"] = ExpectedCurrentMinWeeklySpend,
                ["expectedCurrentMaxWeeklySpend"] = ExpectedCurrentMaxWeeklySpend,
                ["newMinWeeklySpend"] = NewMinWeeklySpend,
                ["newMaxWeeklySpend"] = NewMaxWeeklySpend,
                ["currentAdjustedWeeklySpend"] = CurrentAdjustedWeeklySpend,
                ["plannedAdjustedWeeklySpend"] = PlannedAdjustedWeeklySpend,
                ["ordersPerWeek"] = OrdersPerWeek,
                ["currentAllowancePerOrder"] = CurrentAllowancePerOrder,
                ["plannedAllowancePerOrder"] = PlannedAllowancePerOrder,
                ["currentHardOfferLimit"] = CurrentHardOfferLimit,
                ["plannedHardOfferLimit"] = PlannedHardOfferLimit
            };
        }
    }

    internal sealed class AllowancePreview
    {
        public string Id;
        public long Revision;
        public long ConfigRevision;
        public string SaveScope;
        public DateTime ExpiresUtc;
        public string Mode;
        public float Factor;
        public bool IncludeLocked;
        public readonly List<AllowanceChange> Changes = new List<AllowanceChange>();

        public JArray ChangesJson()
        {
            JArray array = new JArray();
            foreach (AllowanceChange change in Changes)
                array.Add(change.ToJson());
            return array;
        }

        public JObject ToJson()
        {
            return new JObject
            {
                ["previewId"] = Id,
                ["expectedRevision"] = Revision,
                ["expectedConfigRevision"] = ConfigRevision,
                ["saveScope"] = SaveScope,
                ["expiresUtc"] = ExpiresUtc.ToString("o", CultureInfo.InvariantCulture),
                ["factor"] = Factor,
                ["mode"] = Mode,
                ["includeLocked"] = IncludeLocked,
                ["count"] = Changes.Count,
                ["changes"] = ChangesJson()
            };
        }
    }

    internal sealed class PlayerSettingsPreview
    {
        public string Id;
        public long ExpectedRevision;
        public long ExpectedConfigRevision;
        public DateTime ExpiresUtc;
        public int OldInventoryMode;
        public int NewInventoryMode;
        public float OldSpeedMultiplier;
        public float NewSpeedMultiplier;
        public string OldSwapHotkey;
        public string NewSwapHotkey;
        public int OldInventorySlotCount;
        public int NewInventorySlotCount;
        public int NewConfiguredPageCount;
        public int NewAllocatedPageCount;
        public int NewNativeHotbarSlots;
        public bool NewInventoryReady;
        public int BaseInventorySlots;

        public JObject ToJson()
        {
            return new JObject
            {
                ["previewId"] = Id,
                ["expectedRevision"] = ExpectedRevision,
                ["expectedConfigRevision"] = ExpectedConfigRevision,
                ["expiresUtc"] = ExpiresUtc.ToString("o", CultureInfo.InvariantCulture),
                ["oldInventoryMode"] = OldInventoryMode,
                ["newInventoryMode"] = NewInventoryMode,
                ["oldSpeedMultiplier"] = OldSpeedMultiplier,
                ["newSpeedMultiplier"] = NewSpeedMultiplier,
                ["oldSwapHotkey"] = OldSwapHotkey ?? InventoryPagingModel.DefaultSwapHotkey,
                ["newSwapHotkey"] = NewSwapHotkey ?? InventoryPagingModel.DefaultSwapHotkey,
                ["oldInventorySlotCount"] = OldInventorySlotCount,
                ["newInventorySlotCount"] = NewInventorySlotCount,
                ["newConfiguredPageCount"] = NewConfiguredPageCount,
                ["newAllocatedPageCount"] = NewAllocatedPageCount,
                ["newNativeHotbarSlots"] = NewNativeHotbarSlots,
                ["newInventoryReady"] = NewInventoryReady,
                ["baseInventorySlots"] = BaseInventorySlots,
                ["newInventoryModeLabel"] = InventoryModeLabel(NewInventoryMode)
            };
        }

        private static string InventoryModeLabel(int mode)
        {
            switch (mode)
            {
                case 2: return "2x";
                case 3: return "3x";
                case 4: return "on-demand pages (8-page cap)";
                default: return "1x";
            }
        }
    }

    internal sealed class BuildFingerprint
    {
        private const string ExpectedGameAssembly = "9531A85606AF7A5C545EF44895C19F3F08F77CC221479B686539FA5B72141626";
        private const string ExpectedMetadata = "3A5A6E46BD8E6687F63228211978FA94E0885DCB6AE3950AA8D01F047355DE5F";
        private const string ExpectedExecutable = "EC99366083AE5A4068D0C8091D80F857B65C57FB6EF603AB952D835469246ECF";

        public string GameAssemblyHash = string.Empty;
        public string MetadataHash = string.Empty;
        public string ExecutableHash = string.Empty;
        public bool FilesMatch;
        public string Error = string.Empty;

        public static BuildFingerprint Read()
        {
            BuildFingerprint result = new BuildFingerprint();
            try
            {
                string root = MelonEnvironment.GameRootDirectory;
                result.GameAssemblyHash = HashFile(Path.Combine(root, "GameAssembly.dll"));
                result.MetadataHash = HashFile(Path.Combine(MelonEnvironment.Il2CppDataDirectory, "Metadata", "global-metadata.dat"));
                result.ExecutableHash = HashFile(MelonEnvironment.GameExecutablePath);
                result.FilesMatch = string.Equals(result.GameAssemblyHash, ExpectedGameAssembly, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(result.MetadataHash, ExpectedMetadata, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(result.ExecutableHash, ExpectedExecutable, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                result.FilesMatch = false;
                result.Error = ex.GetType().Name + ": " + ex.Message;
            }
            return result;
        }

        public JObject ToJson()
        {
            return new JObject
            {
                ["gameAssembly"] = GameAssemblyHash,
                ["globalMetadata"] = MetadataHash,
                ["gameExecutable"] = ExecutableHash,
                ["filesMatch"] = FilesMatch,
                ["error"] = Error
            };
        }

        private static string HashFile(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }
    }
}
