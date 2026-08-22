# Schedule I Control Center V2.2

## Release summary

V2.2 improves virtual inventory paging in live gameplay while preserving the
reviewed save-scoped page bank and native eight-slot hotbar surface.

## 2.2.0 — inventory hotkey and interface continuity

- Added a polished swap-hotkey capture control to Player & Inventory. Right
  Arrow remains the default, and the selected key is previewed, persisted, and
  reported by the bridge.
- Made the configured paging hotkey use the keyboard input surface directly so
  it remains available while storage, phone, and other non-typing interfaces
  are open.
- Kept text-entry fields protected: paging is suppressed while an actual input
  field has focus.
- Made the single swap key cycle from the final configured page back to page 1;
  on-demand mode still allocates pages safely up to its eight-page cap before
  cycling.
- Fixed overlapping first-person cash and hotbar visuals by invoking the held
  equippable's native unequip lifecycle before clearing and replacing a page.
- Expanded player-setting diagnostics with the configured hotkey and swap
  source while retaining cooldown, persistence, rollback, and deferred unequip
  verification.

## Compatibility and safety

V2.2 remains restricted to the reviewed Schedule I build, loaded save,
solo-host authority, and local same-user bridge. Page data remains save-scoped,
page 0 remains the vanilla save surface, and failed persistence still rolls the
operation back instead of reporting success.
