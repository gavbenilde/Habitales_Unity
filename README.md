## Habitales Event Authoring Reference

Create a new event asset via **right-click in Project → Habitales → Game Event**. Every event has the same fields — only the ones relevant to your trigger type matter.

***

## Fields Cheat Sheet

| Field | What it does |
|---|---|
| `eventID` | Unique key. No spaces. Use underscores. e.g. `year_one_start` |
| `fireOnce` | ✅ Almost always true. Untick only for repeatable flavor events |
| `headline` | Bold title text. Supports `{tokens}` |
| `bodyText` | Main paragraph. Supports `{tokens}` |
| `speakerName` | Fill for character dialogue format. Leave empty for news/headline format |
| `speakerPortrait` | Optional sprite. Only shows if `speakerName` is filled |
| `canInterruptAction` | Shows the Abort button. Leave false for now — abort logic isn't wired yet |
| `focusCameraOnTarget` | Pans camera before popup appears. Requires programmer to set target |
| `triggerType` | OnDay / OnZoneUnlock / OnHealthThreshold / Random / Manual |
| `triggerOnDay` | Day number to fire on. Only used by **OnDay** |
| `triggerBelowWorldHealth` | 0–100 threshold. Only used by **OnHealthThreshold** |
| `triggerChance` | 0.0–1.0 roll per action taken. Only used by **Random** |

***

## Always-Available Tokens

These work in any event without programmer involvement:

```
{current_year}        {current_day}
{available_workers}   {total_workers}    {recovering_workers}
{zone_count}          {world_health}
```

***

## Example 1 — Simple Scripted Briefing

A story beat that fires once on Day 1. No programmer needed.

```
eventID:         year_one_start
fireOnce:        ✅
triggerType:     OnDay
triggerOnDay:    1

headline:        Year {current_year} Begins
bodyText:        Your team of {total_workers} has arrived at the rehabilitation site.
                 The land is at {world_health}% health. You have one year.
speakerName:     (empty — uses headline format)
```

***

## Example 2 — Crisis Warning with Zone Context

Fires automatically when world health tanks. No programmer needed — all tokens are always-fresh globals.

```
eventID:              ecosystem_critical_warning
fireOnce:             ✅
triggerType:          OnHealthThreshold
triggerBelowWorldHealth: 35

headline:             ECOSYSTEM ALERT
bodyText:             World health has fallen to {world_health}%. 
                      Only {available_workers} of your {total_workers} people 
                      are available to respond. {zone_count} zones are at risk.
speakerName:          Azi
speakerPortrait:      [Azi portrait sprite]
```

***

## Example 3 — Fire Outbreak (Requires Programmer)

This fires manually from code — a `FireEntity` or similar system detects a fire tile and calls it. The camera pans to the fire before the popup appears. The programmer sets the tile-specific tokens and the camera target just before firing.

```
eventID:              fire_outbreak
fireOnce:             ❌  (fires every time a new fire breaks out)
triggerType:          Manual
focusCameraOnTarget:  ✅

headline:             Fire Detected in Zone {fire_zone}
bodyText:             A fire has broken out. Immediate suppression is recommended 
                      before it spreads to adjacent tiles.
                      {available_workers} workers are currently available.
speakerName:          (empty)
```

**The programmer's side** — wherever the fire spawns (e.g. inside `FireEntity.OnSpawn`):

```csharp
EventContext.SetOverride("fire_zone", tile.regionID.ToString());
EventContext.SetFocusTarget(tileManager.GridToWorldPosition(tile.gridPosition));
EventManager.Instance.FireEventByID("fire_outbreak");
```

The three lines are always the same pattern: set any custom tokens, set the camera target if needed, then fire by ID. The overrides and focus target clear themselves automatically after the event fires — nothing bleeds into the next event.

***

## The One Rule to Remember

**Manual** events are the only type where you need to coordinate with a programmer on token names. For everything else — OnDay, OnHealthThreshold, OnZoneUnlock, Random — you can author and test entirely on your own using the always-available globals. If a token is missing, it shows as `{token_name}` literally in the popup, which makes broken tokens immediately obvious during playtesting.
