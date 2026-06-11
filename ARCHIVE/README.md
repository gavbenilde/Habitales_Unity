# ARCHIVE — Frozen Pre-Renovation Snapshot

This folder is a **frozen snapshot of the pre-renovation Habitales scripts**, taken at the start of the Alpha architecture renovation (Phase 0).

## Rules
- **Do not modify.** Do not add new files here.
- This is the **behavioral reference / diff target** for all porting subagents. The renovation reproduces proven behavior by diffing the new `Scripts/` tree against these archived sources (see `HABITALES_ARCHITECTURE.md` → PORT instruction).

## What's here
- All `.cs` scripts from `Habitales/Assets/_GAME/` and `Habitales/Assets/_UTILITIES/` (non-plugin), preserving their original relative folder structure (150 files).
- Includes the disposable prototype cube/planting system (`_GAME/Prototype/Scripts/`), which is **archive-then-remove** — this is its only reference after it is deleted from the live tree.

## Notes
- Located at the **repo root, outside `Assets/`**, so Unity does not import or compile it — no GUID conflicts, no duplicate-class compile errors. Files keep the `.cs` extension for readable diffing.
- Only scripts are archived (not art/prefabs/SO assets/scenes); the renovation diffs *behavior*, which lives in code.
- The April-5 "reverse brief" referenced by `_GAME/Prototype/CLAUDE.md` is `ARCHIVE_Habitales_Design_Direction_Synthesis.md` at the repo root.
