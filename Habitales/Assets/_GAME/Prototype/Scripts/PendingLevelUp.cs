// Intentionally empty. The level-up overlay is now driven purely by comparing
// PlayerProgressionSO.lastSeen* against current values on MainMenu.Start; no
// session-scoped state or run→menu handoff struct is needed. File kept (rather
// than deleted) so its .meta GUID stays stable and nothing else has to migrate.
