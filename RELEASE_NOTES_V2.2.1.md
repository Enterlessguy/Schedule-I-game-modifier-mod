# Schedule I Control Center V2.2.1

## Patch summary

V2.2.1 corrects the V2.2 inventory paging control model by providing separate
hotkeys for both directions.

## Inventory paging correction

- Added independent Previous Page and Next Page hotkey controls to Player &
  Inventory.
- Left Arrow and Right Arrow are the respective defaults.
- Both keys can be rebound through the same polished capture, Preview, and Apply
  workflow introduced in V2.2.
- Duplicate left/right bindings are rejected so paging direction remains
  unambiguous.
- Restored bounded backward and forward page movement; the right key no longer
  replaces both directions with a one-key cycle.
- Preserved input through storage, phone, and other non-typing interfaces while
  retaining the focused text-entry guard.
- Existing V2.2 configurations migrate their saved key to Next Page and receive
  Left Arrow as the new Previous Page default.

All V2.2 cash-unequip, persistence, rollback, save-scope, diagnostics, and
updater protections remain in place.
