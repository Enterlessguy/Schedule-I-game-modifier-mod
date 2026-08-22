# Schedule I Control Bridge protocol v1

Transport: Windows named pipe `ScheduleI.ControlBridge.v1`, one same-user client, newline-delimited UTF-8 JSON.

The mod enables Unity background execution from its first `OnUpdate` so queued
main-thread requests continue to complete while Schedule I is unfocused or
minimized. This is required for a separate Control Center process to be usable.

The bridge is deliberately not a generic console, reflection, file, or code-execution service. Operations are allowlisted and validated.

Request:

```json
{"v":1,"id":"unique-id","op":"system.status","args":{},"dryRun":true}
```

Response:

```json
{"v":1,"id":"unique-id","ok":true,"code":"ok","message":"ready","revision":1,"data":{}}
```

Allowlisted operations:

- `system.status`
- `game.save`
- `product.price.list`
- `product.price.previewScale`
- `product.price.applyPreview`
- `sale.dealLimit.get`
- `sale.dealLimit.preview`
- `sale.dealLimit.applyPreview`
- `product.market.list`
- `product.market.previewSync`
- `product.market.applyPreview`
- `customer.allowance.list`
- `customer.allowance.preview`
- `customer.allowance.applyPreview`
- `business.launder.list`
- `business.launder.preview`
- `business.launder.applyPreview`
- `effects.list`
- `effects.preview`
- `effects.applyPreview`
- `player.settings.get`
- `player.settings.preview`
- `player.settings.applyPreview`
- `property.own`

Operation arguments:

- `system.status`: `{}`; always read-only and available even when the build is unknown.
- `game.save`: `{}`; `dryRun:false` starts the host save only after readiness checks.
- `product.price.list`: optional `drugType` and optional `productIds` array.
- `product.price.previewScale`: `mode:"currentFactor"` requires `factor` plus
  optional `drugType`/`productIds`; legacy requests without `mode` remain
  current-factor previews. `mode:"explicitValues"` instead requires 1-64
  unique `{productId,price}` targets. Unit prices must be whole values inside
  the managed live `ProductManager` bounds, `1..16777215` while the reviewed solo-host bridge is eligible. Returns `previewId`,
  `expectedRevision`, expiry, active bounds, and complete expected-old/new rows.
- `product.price.applyPreview`: `previewId` and `expectedRevision`. `dryRun:true`
  rechecks without changing prices; `dryRun:false` applies the complete batch.
- `sale.dealLimit.get`: `{}`; returns the distinct unit-price range, reviewed
  `$9,999` total-deal default, configured/effective total, and
  patch/persistence/enforcement readiness. The two legacy `*StaticMax` response
  names report the managed effective maximum for compatibility; they are not
  native IL2CPP field readbacks.
- `sale.dealLimit.preview`: `enabled` and `maxDealTotal`. Enabled custom totals
  are whole values in `9999..16777215`; disabled previews restore `9999`. Returns
  a 60-second preview plus bridge/config revisions and prior/new configuration.
- `sale.dealLimit.applyPreview`: `previewId`, `expectedRevision`, and
  `expectedConfigRevision`. Rechecks exact build, save readiness, solo-host
  authority, zero remote players, and prior configuration. A real apply writes
  `UserData\ScheduleIControlBridge.sell-price-limit.json` and activates reviewed
  Harmony replacements for the four native counteroffer/handover clamp paths.
  Native `MaxPrice` static wrappers are deliberately untouched because the
  reviewed native methods inline their `$9,999` constants. It never changes
  unit prices, fair-market values, customer allowances, or acceptance logic.
- `product.market.list`: optional `drugType` and optional `productIds`; returns
  live sell price, vanilla/effective fair-market value, absolute factor, the
  game's customer value proposition, alignment state, save scope, and config
  revision.
- `product.market.previewSync`: filter plus `mode`. `matchSellPrice` computes a
  separate absolute factor for every frozen product ID so effective fair-market
  value exactly matches its current sell price. `absoluteFactor` also requires
  a finite `factor` in `0.1..1000000`. `explicitValues` instead requires `targets`,
  a bounded array of unique `{productId,marketValue}` rows, and converts each
  requested value to an absolute factor relative to the frozen vanilla value.
  This is the bridge operation used when a user double-clicks a planned
  fair-market value in the Products table. Returns a short-lived preview with
  expected sell/vanilla/effective values, old/new factors, planned values,
  bridge/config revisions, and save scope.
- `product.market.applyPreview`: `previewId`, `expectedRevision`, and
  `expectedConfigRevision`. Rechecks the exact build, save scope, solo-host
  authority, zero remote clients, sell prices, vanilla/effective market values,
  and factors. `dryRun:true` is preflight only. A real apply persists a bounded
  bridge-owned sidecar, recalculates live values through the reviewed game API,
  and verifies the game customer-value result. Failure rolls back and requires
  readback if rollback is incomplete.
- `customer.allowance.list`: optional boolean `includeLocked` (default `false`).
  Returns one row per selected customer with `customerId`, `name`, `unlocked`,
  `originalMinWeeklySpend`, `originalMaxWeeklySpend`,
  `currentMinWeeklySpend`, `currentMaxWeeklySpend`, `adjustedWeeklySpend`,
  `ordersPerWeek`, `allowancePerOrder`, `hardOfferLimit`, and `overridden`.
- `customer.allowance.preview`: accepts one of two modes. `originalFactor`
  requires a finite `factor` in `0.1..1000000` plus optional `includeLocked`, and
  always multiplies the frozen original weekly-spend range rather than the
  currently overridden range. `explicitValues` requires a bounded array of
  unique `{customerId,minWeeklySpend,maxWeeklySpend}` targets. Explicit values
  must be finite, `0..16777215`, and satisfy `minWeeklySpend <= maxWeeklySpend`.
  The response freezes expected current/new ranges plus planned adjusted weekly
  spend, order count, allowance per order, and hard offer limit for every row,
  together with `previewId`, `expectedRevision`,
  `expectedConfigRevision`, expiry, and save scope.
- `customer.allowance.applyPreview`: `previewId`, `expectedRevision`, and
  `expectedConfigRevision`. Rechecks exact build, save scope, solo-host
  authority, zero remote clients, customer identity/data, original/current
  ranges, and the frozen affordability inputs. `dryRun:true` is preflight only.
  A real apply atomically replaces the save-scoped allowance profile, verifies
  live adjusted-spend and per-order results, and rolls back on mismatch.
- `player.settings.get`: `{}`; returns configured inventory mode, native
  `nativeHotbarSlots` (exactly `8` when `inventoryReady` is true), current page,
  `configuredPageCount` (`1`, `2`, `3`, or the mode-4 cap `8`),
  `allocatedPageCount` (the currently materialized backing pages, separate from
  the configured capacity), `inventoryReady`, `saveScope`,
  `sidecarLoaded`, `lastInventoryError`, and the configured/live movement-speed
  multiplier. `baseInventorySlots` is always `8` for protocol compatibility.
- `player.settings.preview`: `inventoryMode` (`1`, `2`, `3`, or `4` for
  on-demand pages with an explicit safe cap of 8 pages/64 slots) and a finite
  `speedMultiplier` from `0.1..10`.
  Returns complete old/new values and a 60-second preview. It requires the
  reviewed solo-host workflow.
- `player.settings.applyPreview`: `previewId`, `expectedRevision`, and
  `expectedConfigRevision`. A real apply persists the bridge-owned runtime
  profile and keeps the same eight native `HotbarSlot`/first-eight UI objects.
  Mode `4` allocates save-scoped backing pages on demand up to 8 pages rather
  than reporting infinity or allocating Unity UI objects. Left/right consumes
  the canonical `InventoryLeft`/`InventoryRight` action edge while focused and
  clamps at fixed-mode bounds; mode-4 right input allocates the next page until
  the cap. Page 0 remains authoritative in vanilla `Inventory.json`; extra
  pages are versioned, bounded, scope/build-checked, atomically replaced
  UserData sidecars. A downgrade with occupied higher pages is rejected.
  Preview responses expose `newConfiguredPageCount`, `newAllocatedPageCount`,
  and `newInventorySlotCount`: fixed modes 2/3 mean 16/24 configured slots;
  mode 4 means up to 64 slots while reporting its current allocation separately.
- `property.own`: `propertyCode`; a non-dry-run request also requires
  `expectedRevision`. No operation accepts an un-own value.

`drugType` accepts `Weed`/`Marijuana`, `Meth`/`Methamphetamine`, `Cocaine`,
`MDMA`, `Shrooms`, `Heroin`, or `All`. Factors are finite and bounded to
`0.01..1000000`. Product arrays are bounded to 64 allowlisted identifiers, request
IDs to 64 characters, request bodies to 16 KiB, previews to 60 seconds, and
stored previews to 32 per data workflow (16 for deal-limit previews).

Fair-market factors are finite `0.1..1000000`, explicit for at most 64 product IDs
per save, and stored in a fixed bridge-owned configuration no larger than 64
KiB. The request never accepts a path or save-scope key.

Customer allowance requests accept at most 96 manual targets. The separate
bridge-owned profile stores at most 128 customer ranges per save, 64 save
scopes, and 128 KiB total. The game currently exposes 65 customers (27 unlocked
and 38 locked), so the bounded profile can represent the full roster. Customer
IDs and save-scope keys are discovered by the bridge; callers cannot supply a
path or scope key.

`system.status` data fields are `protocolVersion`, `modVersion`, `gameVersion`,
`gameBuild`, `expectedGameBuild`, `knownBuild`, `sceneName`, `saveLoaded`, `saveReady`, `savePath`,
`isHost`, `isServer`, `isClient`, `mutationsAllowed`, `capabilities`, and
`buildHashes`. v0.2 also returns `remoteClientCountKnown`,
`remoteClientCount`, `isSoloHost`, `marketPatchActive`,
`marketPersistenceReady`, `marketConfigRevision`, `activeMarketOverrides`, and
`marketSaveScope`. v0.3 additionally returns `allowancePatchActive`,
`allowancePersistenceReady`, `allowanceConfigRevision`,
`activeAllowanceOverrides`, and `allowanceSaveScope`. v0.4 additionally returns
`sellPriceLimitPatchActive`, `sellPriceLimitPersistenceReady`,
`sellPriceLimitConfigRevision`, `sellPriceLimitOverrideEnabled`,
`sellPriceLimitOverrideApplied`, configured/current/default deal totals, and
the separate current unit-price bounds.

Mutation rules:

- Only a loaded host/server may mutate networked state.
- All Unity and IL2CPP object access occurs on the Unity main thread.
- Price changes use a short-lived preview ID. Apply rechecks every expected old value and aborts the complete batch if any price changed.
- `property.own` only acquires ownership through the vanilla path. Un-owning is not exposed.
- Requests have bounded sizes and numeric ranges. No request accepts a file path or arbitrary command string.
- Mutations remain disabled on an unknown game build; status remains available.
- A request that times out before main-thread execution is canceled and cannot
  mutate later. If execution already began, the response reports uncertainty
  instead of claiming that the mutation failed.
- Fair-market overrides activate only on the exact reviewed build with a loaded
  solo-host save and a known count of zero remote players. Losing authority,
  unloading/changing the save, or a remote player joining deactivates the local
  overrides and restores vanilla market values.
- The `ProductManager.CalculateProductValue(ProductDefinition,float)` patch is
  installed only for the reviewed build and scales only explicit configured
  product IDs. It does not patch customer decisions, item value, BasePrice, or
  the product-less calculation overload.
- The exact-overload `CustomerData.GetAdjustedWeeklySpend(float)` postfix is
  installed only for the reviewed build. It scales only configured customer
  data pointers from their original range to their configured range while
  preserving the native relationship interpolation and rank multiplier. It
  does not patch acceptance methods, their constants, preferences, addiction,
  order scheduling, product enjoyment, or randomness.
- Native affordability uses adjusted weekly spend divided by that customer's
  generated order-day count. Both counteroffer evaluation and offer-success
  calculation impose a hard ceiling at approximately three times that per-order
  allowance; above it the offer can reach zero chance. Raising allowance moves
  that native ceiling but does not force a sale.
- Customer allowance overrides activate only on the exact reviewed build with
  a loaded solo-host save and a known count of zero remote players. Losing a
  gate immediately clears the live pointer map. The original `CustomerData`
  fields are never mutated.
- The total-deal override patches `CounterofferInterface.Send`,
  `HandoverScreen.PriceChanged`, and `HandoverScreen.DonePressed` on the exact
  reviewed build. The separate product-price patch raises `ProductManager.MAX_PRICE`
  and routes eligible `ProductManager.SendPrice` commits through the host-side
  set path, bypassing the reviewed `$999` validation. Losing eligibility restores
  vanilla bounds and behavior; configured deal overrides reapply on the next
  eligible solo-host load.

Reload classifications:

- Offline save edits: load/reload required.
- Live product price: immediate after server replication; save for persistence.
- Live property ownership: immediate through vanilla ownership logic; save for persistence.
- Console enablement: offline in milestone one; load/reload required.
- Live fair-market value: immediate and persisted by the bridge-owned,
  save-scoped sidecar; it reapplies after a full process restart. It is not a
  native `Products.json` field and is intentionally disabled with remote peers.
- Live customer allowance: immediate and persisted in
  `UserData\ScheduleIControlBridge.customer-allowances.json`, a separate
  bounded save-scoped sidecar. It reapplies after a full process restart, is
  not a native customer-save field, and is intentionally disabled with remote
  peers. Removing the sidecar or clearing an override restores the original
  ScriptableObject range on the next eligible load.
- Live total-deal maximum: immediate in both counteroffer and handover entry,
  persisted in `UserData\ScheduleIControlBridge.sell-price-limit.json`, and
  reapplied on the next eligible full-process load. It is build-specific rather
  than save-scoped and restores to `$9,999` when disabled.
- Other market/effect multipliers remain postponed until separately reviewed.
