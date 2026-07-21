# Habitales — Alpha Onboarding: Build Plan & Swarm Work-Orders

> ## ⛔ DEPRECATED (2026-07-21)
> This plan's **beat model is superseded** by the 18-phase rebuild. The current
> source of truth is [`ONBOARDING_HANDOFF.md`](ONBOARDING_HANDOFF.md) +
> [`ONBOARDING_TUTORIAL_PLAN.md`](ONBOARDING_TUTORIAL_PLAN.md). The legacy 11-beat
> `OnboardingDirector` is retired; the two juice systems (drag inset, delta-tip
> chain) are **dormant**. Kept only for historical context — the **façade work**
> (layer 1, Narrative Popup) is still live and in use.

**Companion to:** `Downloads/habitales_onboarding_alpha.md` (the Hodent onboarding sequence) and `HABITALES_ARCHITECTURE.md` (the Laws).
**Status:** The **Narrative Popup façade** (layer 1) is built — see "What's already done." Layers 2–3 below are the delegatable work.

---

## The three layers

| Layer | What | Who builds | New? |
|---|---|---|---|
| **1. Narrative Popup façade** | One reusable surface; 4 popup flavours behind one call | **Done (this session)** | mostly new, wraps existing |
| **2. Messaging app FX** | Icon badge + aggressive shake + ribbon slide-in | **1 Sonnet agent** | new |
| **3. Onboarding coach** | The 11-beat sequence: stall-detect, coach-marks, juice, drag-inset | **Sonnet swarm ×4** | new |

**Model policy for the swarm:** implementation agents run on **Sonnet**; trivial mechanical tasks (a one-line getter, a self-contained tween coroutine) run on **Haiku**. **Never Opus for subagents** — Opus orchestrates (splits work, reviews), Sonnet executes. This mirrors how `HABITALES_ARCHITECTURE.md` is written.

**Every work-order carries the three Laws verbatim** (Architecture §1): getters-not-setters, hooks-on-meaning-not-mutation, loud-fail-scales-with-distance. Plus: **agents write C#; they cannot wire prefabs in the Inspector — the human does scene wiring.** Each agent ends by listing the exact wiring steps the human must do.

---

## What's already done (layer 1 — the façade)

New, under `Assets/_GAME/Scripts/UI/Narrative/`:

- **`PopupStyle.cs`** — `enum ScreenAnchor`, `struct PopupStyle` with two axes (`intrusive`, `showPortrait`) + `autoAdvanceSeconds`, and three presets: `PopupStyle.Dialog` / `.Character` / `.Text`.
- **`DialoguePopupView.cs`** — the **Dialog Popup**: intrusive, dims + blocks, portrait, plays a thread (Next per line), pauses the sim via `RunManager.PauseForEvent()` and releases on completion.
- **`SideNarrativeBubble.cs`** — the **Character** (portrait) and **Text** (no portrait) popups: a non-blocking side bubble, anchored to a screen corner, advances on tap or auto-timer, LeanTween slide-in. Generalises the prototype `AziSpeechBubbleUI` (self-heal + loud-fail preserved).
- **`NarrativePopupManager.cs`** — the **single front door**. `PlayThread(DialogueThreadSO, style)`, `PlayLines(IList<ResolvedLine>, style)`, `Say(body, portrait, speaker, style)`, `ShowHeadline(...)` (delegates to the existing one-shot `EventPopupUI`), `HideAll()`.

Reuse wins:
- A `DialogueThreadSO` authored once renders in **chat OR as a popup** — `DialogueManager.ResolveThreadLines(thread)` is now the single resolution path (S2).
- The existing one-shot **`EventPopupUI` is kept** as the Headline flavour; `EventManager` now routes its events through the façade (with a temporary fallback so the running build never breaks before the façade GameObject is wired).

### Human wiring needed to activate layer 1
1. Create a `NarrativePopupManager` GameObject on the gameplay/Phone canvas; assign **EventPopupUI** (existing), a new **DialoguePopupView**, and a new **SideNarrativeBubble**.
2. Build the two new view prefabs (dim overlay + card + portrait box + body + Next for Dialog; small anchored box + optional portrait box + tap target for the side bubble) and wire their serialized fields. Law-3 errors name any unwired ref.
3. Author **Priority Zero** (the Captain intro) as a `GameEventSO` (`triggerOnDay = 0` / run-start) — fires through the façade, zero code.
4. Once verified in-scene, remove the migration fallback in `EventManager.ShowPopup` (marked with a `TODO`).

---

## Work-Order A — Messaging app FX  *(1 Sonnet agent)*

**Goal:** the Discord-style messaging-app icon: a badge with a white unread count, an aggressive shake on new message, and a Messenger-style ribbon that previews incoming text.

**Context the agent gets:** the three Laws; `DialogueManager.cs` (events `OnUnreadChanged`/`OnMessagesUpdated`, private `_unreadTabs`); `ChatAppUI.cs` (per-tab dots already exist, the phone button calls `ToggleChatApp()`); `ObjectiveBannerUI.cs` (static banner — the ribbon is the animated cousin); `EventManager.cs` (the "queue during sweep, release at rest" pattern to mirror); LeanTween is available.

**Tasks:**
1. **`DialogueManager.UnreadCount`** — add `public int UnreadCount => _unreadTabs.Count;` (Law 1, getter only). *(Haiku-trivial; fold into this agent.)*
2. **`MessagingAppIconUI`** (new, `Habitales.UI`): subscribes to `DialogueManager.OnUnreadChanged`/`OnMessagesUpdated`. Renders the badge (hidden at 0, white number on a coloured pip). On a new message, LeanTween an aggressive shake (rotation + position jitter, ~0.4s, settles). Hides badge when `UnreadCount == 0` (chat opened).
3. **`RibbonUI`** (new, `Habitales.UI`): a slide-in at the app logo that **previews** the incoming text (the doc: "half the dopamine arrives in the peek"). **Queues during the action day-sweep; releases one at rest** so it never competes with a spotlight Event — gate on `RunManager.IsActionRunning`/`IsEventPaused` (read-only, Law 1) and/or `RunManager.OnDayResolved`. Slide in → dwell → slide out; tapping it opens the chat (`ChatAppUI.OpenThread`).

**Triggers (Alpha = simple, per the doc):** drive the ribbon off `DialogueManager.OnMessagesUpdated` (a real new message). The **first ribbon** fires when the 2nd region unlocks — subscribe `RunManager.OnRegionUnlocked` for that beat. Keep it counters + a coin flip, not a mood model.

**Out of scope:** the deferred event-vocabulary (Architecture §6.2) — wire to the existing signals only.

**Ends with:** the human wiring steps (icon prefab refs, ribbon prefab refs, where the badge/ribbon sit on the canvas).

---

## Work-Order B — Onboarding coach  *(Sonnet swarm ×4)*

The 11-beat sequence is the real new surface. Split into four independent components; **B1 (the director) defines the contract the others depend on — schedule it first, then B2–B4 in parallel.**

### B1 — `OnboardingDirector` + stall detector  *(Sonnet)*
The spine. A serialized, ordered list of **beat definitions** (hardcoded data is fine — the doc licenses it): each beat has an id, a trigger (`OnRunStart` / `OnActionCount(n)` / `OnValidTilesAvailable(n)` / `OnRegionUnlocked` …), the implicit cue to show, and the **stall fallback** (the explicit text shown after ~3s of no input). Owns an **idle timer**: resets on any player input; at ~3s with the beat unsatisfied, reveal the explicit text. Advances to the next beat when its success condition is met. Reads game state via Law-1 getters (`RunManager`, `ActionManager`, `TileManager`); fires cues by calling the **layer-1 façade** (`NarrativePopup.Say(...)` / `PlayThread`) and the **B2 coach-marks**. Priority Zero is beat 0 → a `GameEventSO` via the façade. Graduation (beat 10) retires all scaffolding.

**Contract it exposes to B2–B4:** an event/interface like `OnBeatEntered(beatId)` / `OnBeatCompleted(beatId)` and a `RequestCoachMark(kind, target)` call, so the widget layer is dumb and director-driven.

### B2 — Coach-mark widgets  *(Sonnet)*
Dumb, reusable, director-driven visuals: **blinking tile marker**, **fidget arrow** (nudges toward a target), **ghost-mouse** (LeanTween sprite that mimes LMB down/up and drags), **persistent corner reminders** ("Select / Reselect", "Select multiple"). Each is a small MonoBehaviour with `Show(target)` / `Hide()`. No game logic — they render what the director asks.

### B3 — Juice: 1.3 sequence + dusk bubbles  *(Sonnet)*
The beat-1.3 **staggered** teaching sequence (~200–300ms apart): tile shifts colour → delta tip pops at the tile → score bubble launches to the corner (during onboarding only; fire together after). Plus **dusk bubbles**: event-driven on **plant-placed / plant-evolve** (NOT per-tile-per-day), **pooled** GameObjects. Hook the meaning-events (`TileManager.OnEntitySpawned`, the tier-change/evolve hook) — Law 2. LeanTween throughout.

### B4 — Drag ghost-inset (beat 2.1)  *(Haiku or Sonnet)*
The ~20-line LeanTween coroutine the doc already specs: a ghost-mouse sprite tweens along a path, a `pressed` sprite swaps in at drag-start, 3–4 ghost tile sprites swap shape as the cursor x passes each. Fully retimeable. Fires on the first action where **3+ valid target tiles** are available (Rule of Three: replays for 3 drag-eligible actions, then fades). Self-contained → **Haiku-suitable**.

**Hardcode (per the doc & the user):** the 11-beat script strings, per-beat Azi lines, which marker blinks where. Bake as data in B1. Priority Zero ships as a `GameEventSO` asset.

---

## Deliberately NOT in scope (Alpha)
- The deferred **event vocabulary** (Architecture §6.2) — stub stays; onboarding/ribbon ride existing signals + hardcoded counters.
- The **folder/namespace migration** (Architecture §4, Phase 2.5) — independent renovation; do not entangle with onboarding.
- Sticker/reaction system, fire/crisis onboarding, losing-but-okay framing — all parked per the onboarding doc.

---

## Consolidated Inspector Wiring (all 3 layers compile — wiring is the only remaining step)

> All code compiles (`dotnet build` → 0 errors). Every component loud-fails (Law 3) naming any missing ref in the Console — so you can wire iteratively and let the errors guide you. Build leaf prefabs per each component's `[Tooltip]`s. **Do the Critical Path first** to get Priority Zero on screen, then the rest.

### Critical path — smallest testable loop (Priority Zero showing)
1. **EventManager** (existing scene object) → its `GameEventRegistry` asset → add **`PriorityZero.asset`** to the `events` list. *(Most common miss — `FireEventByID` searches this list.)*
2. **`PriorityZero.asset`** → `eventID = priority_zero`, `triggerType = Manual`, `fireOnce = true`, fill headline/body/`speakerName`/`speakerPortrait` (see content table in chat / above).
3. **`NarrativePopupManager`** GameObject → assign `headlineView` = the scene's **EventPopupUI**, `dialogueView` = a **DialoguePopupView** prefab, `sideBubble` = a **SideNarrativeBubble** prefab.
4. **`OnboardingDirector`** GameObject → assign its Priority-Zero `GameEventSO` field = `PriorityZero.asset`, `aziPortrait`, `stallSeconds = 3`. Leave `OnboardingBootstrap` OR set `skipBeat0 = true` (one owner only).
   - ▶ Play: Priority Zero should dim + show with Azi + OK.

### Layer 1 — Narrative Popup façade
- **DialoguePopupView prefab:** dim `overlayBlocker`, `card`, `portraitBox`, `portrait` (Image), `speakerLabel`, `bodyText`, `nextButton` (+ `nextLabel`).
- **SideNarrativeBubble prefab:** `root` (RectTransform), optional `canvasGroup`, `portraitBox`, `portrait`, `speakerLabel`, `bodyText`, `tapTarget` (Button over the bubble).

### Layer 3 — Onboarding coach
- **OnboardingDirector:** Priority-Zero SO, `aziPortrait`, `stallSeconds`, (optional) `actionBarUI`, `skipBeat0`, `debugStartBeat` (set >0 to skip to a beat while testing).
- **CoachMarkLayer:** `screenCamera` (or leave null → `Camera.main`) + the five widget refs: `blinkingTileMarker`, `fidgetArrow`, `ghostMouseClick`, `ghostMouseDrag`, `cornerReminder`. Each widget is its own GameObject under the HUD canvas with its sprites/refs wired per its tooltips. Unused kinds may stay null (their requests are ignored with a warning).
- **Beat1_3JuiceDirector:** `commitFlashColor`, `deltaTipPrefab`, `deltaTipOffset`, `scoreBubbleTemplate` (a DuskBubble), `scoreBubbleTarget` (the score-counter RectTransform), `staggerDelay`.
- **DuskBubblePool:** `bubblePrefab` (a DuskBubble prefab — needs a CanvasGroup), `poolSize` (caps concurrent bubbles), `poolParent`, `filterToPlants` + `plantIdSubstrings` (tune to your plant entityIds).
- **DragGhostInset:** `insetRoot`, `ghostCursor`, `cursorImage`, `cursorIdle`/`cursorPressed` sprites, `ghostTiles[]` (3–4 Images, left→right), `tileEmpty`/`tileFilled` sprites, `dragStartPos`/`dragEndPos`.

### Layer 2 — Messaging FX
- **MessagingAppIconUI** (on the phone-HUD messaging button): `badgeRoot` (pip), `badgeCountText`, `iconRect` (the image that shakes), shake tunables.
- **RibbonUI** (`MessageRibbon` GameObject, floats above world, below full-screen events): `ribbonPanel` (RectTransform, parked off-screen at `hiddenAnchoredX`), `senderNameText`, `previewBodyText`, `tapButton`, `chatAppUI` (drag the ChatAppUI), slide/dwell tunables.

### Cleanup once wired & verified
- Remove the migration fallback in `EventManager.ShowPopup` (marked `TODO`) so a missing façade loud-fails instead of silently using EventPopupUI.
- Pick one Priority-Zero owner (delete `OnboardingBootstrap` or set director `skipBeat0`).
