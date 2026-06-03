# Habitales — Design Direction Synthesis (Codebase Reading)

This document is a reading of what *Habitales* has been **becoming** according to its code — not what its design docs say it is, and not what the developer intends it to be, but what the prototype, taken honestly, actually does to a player. It was assembled from a system-by-system pass across ten code areas. It is observational, not prescriptive: it names the game the code currently describes so that a separate drafting conversation can hold it up beside the developer's vision and the existing GDD. Where the code is unfinished or contradictory, that is carried forward, not smoothed over.

## 1. The Game's Center of Gravity

The code's care concentrates in one place above all others: **the ecological simulation of the land itself.** The tile and entity systems are not placeholder math. Constants like `DECOMPOSITION_DAYS = 30`, a hard health cap above 60% contamination, `ORGANIC_BOOST_ON_DEATH`, and seven named real-world degradation conditions (heavy metal contamination, drainage collapse, soil compaction) describe someone who researched ecological causality and compressed it into playable units. The same seriousness runs through the planting system — a six-element survival array that is the single source of truth for both whether a plant lives and what it gives back to the soil — and through the run loop, which knows exactly how to end and score a 60-day season.

A second, smaller concentration of care is surprising: the **run-end Level-Up animation** is the single most polished stretch of code in the project — it rewinds XP across thresholds, handles multi-level overflows, and paces its bursts deliberately. The flood-fill painting verb is also genuinely crafted: its frontier is shuffled on purpose "to keep the frontier organic."

The game's true center, then, is *a simulation of land healing* and *the act of painting care onto it* — exactly the verb the May prototype validated. The docs' systems-heavy framing is, in this sense, accurate to where the effort went.

## 2. The Felt Experience the Code Promises

**Minute 1.** The player pans a camera across a grid of tired, discolored land. They click a tile; a panel snaps open showing a health percentage and one clinical word — "Critical," "Degraded," "Thriving." They choose a category from a D-pad of four (Examine, Intervene, Emergency, Cleanup); the camera takes over, panning and hard-zooming to the tile. They pick a plant card — an audio cue fires, the only consistent sound in the build — and drag. A cyan flood-fill blob grows outward in an organic, non-rectangular shape as they sweep neighboring tiles.

**Minute 5.** They confirm. The camera pulls back out. Days advance — a directional light spins through a full rotation, the first day slow, each subsequent day twice as fast, so long work "blurs." A pale, near-white cube appears floating over each painted tile. They open a phone-style chat app; a worker has messaged them — a quiet line of field prose, "Saw a kingfisher near the bend."

**Minute 15.** More planting. The cubes saturate in daily step-jumps as the land beneath them heals — pale lerping toward full color. Clicking a tile outlines its whole zone in a red-to-green band. An event fires: the world pauses, the camera pans to a crisis, a card appears, the player clicks OK, the world resumes.

**Minute 30.** The loop holds steady — paint, advance, watch color return, read a message, absorb an interruption — until Day 60 ends the run and a Snapshot screen tallies what was built.

What the code promises is a **slow, legible recovery loop**: act, wait, watch the land answer in color.

## 3. Where the Code Diverges from "Cozy-Tense"

The friction is concentrated and consistent. **The color language is clinical, not cozy.** Zone health outlines lerp red-to-green — traffic-light alarm vocabulary — and the global health bar runs through purple-blue-pink-green, colors with no ecological grounding at all. The careful earthy tones used elsewhere make these mismatches stand out.

**The "tense" half is delivered without craft.** Village tiles spawn four to eight fires at once on a flat daily roll — a jump-scare, not a building dread. Health-threshold events re-fire every single day the world stays low, turning weight into repetitive noise. Fire Suppression on a burned-out tile always "succeeds," silently costing days and worker fatigue — punishing rather than weighty.

**The world is mute and still between actions.** Weather changes work speed, fatigue, and fire spread but has zero visual or audio expression. Cubes and tiles change only on daily ticks; nothing breathes between the player's inputs. Cozy needs a living world, and the world currently only moves when commanded.

**Modal interruption fights calm.** Examine results and events arrive as blocking overlays that stop the player entirely, where the cozy register would favor ambient disclosure. And the camera frequently takes spatial agency away at the exact moment the player is deciding how large to paint.

## 4. The Unfinished Trajectory

The code has made promises it has not yet cashed. **The verb palette is two-fifths built:** nine of fourteen actions are coded but deliberately held dormant — the entire Cleanup category (the "cozy" maintenance gameplay) and most of Examine are offline. The **Examine category has no payoff**: its tooltip output is commented out, and the fog-of-knowledge it implies is switched off (`isAnalyzed` and `issuesRevealed` are hardcoded true), so the "learn the land" fantasy has no surface.

**The procedural plant unlock — the unbounded pool the docs promise — is infrastructure without a trigger.** The generator works, the registry holds 29 culturally specific Filipino plant names, but nothing wires an unlock to a level-up. **The dialogue system is the next prototype bet and is roughly 30% of the way to its intent**: weighted worker selection, birthday tracking, and field-voice flavor lines exist, but the authored content layer (`WorkerMessageTemplateSO`) is empty and the system has only one tonal register — crisis and charm land identically.

Smaller unfinished seams: three contradictory run lengths (60 / 100 / 365), a VFX manager nobody calls, Research Points tracked but never earned or spent, the "Boogle ?" help button wired only for planting actions, and a run-end flow whose Level-Up screen only appears if the player exits to the menu rather than replays.

## 5. Load-Bearing for Feel

Ranked by what the player would *miss* — these are the team's protection list:

1. **The land simulation & tiles** — remove it and there is no world, no surface to paint, no answer to any action.
2. **The planting / cube system** — the player's only agency and the entire "watch it grow" cozy payoff.
3. **The painting verb & action selection** — the sole bridge between intent and world state.
4. **The run loop** — the 60-day season; without it nothing ends or is scored.
5. **Events** — currently the *only* delivery mechanism for tension.
6. **Dialogue** — the only sense that the world is inhabited by people who notice.
7. **Inspect/Examine** — the player's vocabulary for *why* the land matters.

These systems may be ugly in code. They cannot be removed or rushed.

## Habitales, in one breath

*Habitales is a quiet act of ecological repair — you paint care across tired land and watch pale things slowly saturate back into color — built on a simulation that understands ecology deeply, and still waiting for its world to answer back with the warmth, sound, and weight the player is meant to feel.*
