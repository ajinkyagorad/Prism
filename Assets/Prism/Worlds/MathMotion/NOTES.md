# Mathematics of Motion — build notes

WorldId `math-motion`. Door concept `math-motion` (System, Mathematics). Namespace
`Prism.Worlds.MathMotion`. Everything lives in this folder; nothing outside it was touched.

## What it is

A curve hangs in the air. A bead rides along it, and a second curve is traced out beneath it as
the bead goes — nothing is labelled. The learner can grab the first curve with either hand and
reshape it; the second curve answers instantly and everywhere, including where it should not
move. Once they have personally produced both a maximum (a down-crossing of the second curve's
own zero) and a steep section (a spike in it), twice each, a bead becomes draggable and a tangent
line appears — a line that only touches the first curve, whose slope IS the height of the second
curve at that point. Only then do the words arrive: derivative, slope, rate of change, f'(x).
Then the reverse: sweep a hand under the second curve and watch a third curve — the running
accumulation — grow until it lands back on the first. That is the Fundamental Theorem of
Calculus, watched happening rather than stated. Apply asks for a specific maximum and a specific
total area; Explain asks for a zero-crossing to be predicted before it is confirmed, with a wrong
guess requiring a fresh deformation before a new attempt is accepted; Create asks for a hump and a
dip of the learner's own design, held stable and untouched for a few seconds.

## Files

- `CurveField.cs` — the honest maths. Plain C#, no MonoBehaviour: a 121-sample array `Y[]` over
  `x in [-1,1]`, a central-difference `Derivative[]`, a Gaussian-bump local edit
  (`ApplyGaussianBump`), a trapezoidal `TotalArea()` and a trapezoidal `AccumulateDerivative` (the
  Fundamental Theorem, computed, not staged), and a prominence-based local-extremum finder used by
  both the Explore gate and the Explain/Create challenges.
- `MathMotionMesh.cs` — `ColouredStripMesh`, a small reusable-buffer mesh builder (round tube or
  flat area strip) that carries a genuine per-vertex `Color`, since none of PRISM's shared mesh
  helpers do. Modelled directly on `PrismMesh.Tube`'s frame-propagation algorithm.
- `MathMotionCurve.shader` — `Prism/MathMotionCurve`. Structurally `Prism/Trail` with the
  age/head-brightening terms removed and the ramp replaced by the incoming vertex colour; composes
  through `Prism_Compose` exactly like every other PRISM material, so it survives on any backdrop.
  Declared in `MathMotionWorld.Shaders` so the master's shader registration picks it up.
- `MathMotionWorld.cs` — the `PrismWorldBase` subclass: build, loop gates, per-frame simulation
  and interaction, the colour law, and the five declared concepts.
- `MathMotionChallenge.cs` — Apply/Explain/Create, mirroring `OrbitalChallenge`'s structure.

## Colour encoding

**Hue is the sign of the derivative.** Mint where the function is rising, coral where it is
falling, warm-neutral exactly at a critical point (`MathMotionWorld.ColourForSign`, scaled by
`ColourSaturationScale`). One rule, applied identically to the function curve, the derivative
curve, the tangent line, the bead, the sweep fill and the accumulation trace — the same colour
appearing in two places at the same x is not decoration, it is the same number shown twice, which
is exactly the correspondence Discover is built to make the learner notice for themselves. The
integral total shown in the Formalize label uses the same two-colour law on a separate, smaller
saturation scale (`AreaSaturationScale`), since area and slope are different quantities. Apply's
target markers deliberately do NOT use this law — they use Gold/Lavender via
`PrismMaterials.ForRelation(Relation.Constrains, ...)`, the same convention Orbital's ring
challenge uses for "this is a target, not a measurement" — so the one meaningful colour channel is
never overloaded with two different meanings.

## Honest vs approximate

**Honest, computed every time, never faked:**
- The derivative is real central differences on the *current* sampled curve, recomputed from
  scratch after every edit (`RecomputeDerivative`), not smoothed or cached separately.
- The integral is real trapezoidal accumulation. Accumulating the derivative back up reproduces
  the original function to within ordinary O(dx²) numerical error, which at 121 samples over a
  domain of width 2 is far below anything visible — the Formalize "it lands back on the first
  curve" moment is the theorem actually happening.
- Deformation is a real local basis-function edit: a single Gaussian term added at the grabbed x,
  so the derivative away from the hand is unaffected beyond the Gaussian's own rapidly vanishing
  tail. Dragging is many honest edits in a row, not one canned animation.
- Apply's two targets and Create's hump-and-dip check are read directly off the live array
  (`MaxValue()`, `TotalArea()`, `BestMaximum`/`BestMinimum`) — nothing is a scripted checkpoint.

**Approximate, and why it is still honest:**
- Central differences are one-sided (first-order) at the two domain endpoints rather than central
  (second-order) — the only place accuracy is deliberately traded away, and it is invisible in
  practice since nothing interactive sits at the extreme edge of the domain.
- The derivative curve's *display* height is soft-clamped (`DerivDisplayClamp = 6`, abstract
  units) so an extreme spike cannot fly off the built display frame. This clamp is cosmetic only —
  the real, unclamped derivative value is what drives colour, evidence gates (`SteepThreshold`)
  and the Formalize label's printed number. A learner who makes a very steep section will see the
  second curve hit a soft ceiling and the label will still report the true number underneath it;
  this is noted here because it is the one place a display choice could be mistaken for the
  physics being wrong.
- "Local maximum/minimum" uses a cheap topographic-prominence heuristic (walk outward from a
  candidate sample until the curve turns back past the candidate's own height, track the shallowest
  point seen) rather than a rigorous prominence algorithm. It is deterministic, has no false
  positives on a smooth analytic curve like this one, and is what both the Explore gate and the
  Explain/Create challenges are built on.
- The maximum-value scan and prominence walk are O(n) each over 121 samples; called a handful of
  times per second at most (on release, and inside the challenge evaluators), not per hand per
  frame, so none of this shows up as cost.

## What the master does not need to wire

Concept and shader registration are both fully automatic through the existing reflection
machinery (`WorldRegistry.WorldTypes` / `AllWorldConcepts` / `AllWorldShaders`, consumed by
`PrismConceptSeed.cs` and `PrismConfigure.cs`). `MathMotionWorld.Shaders` declares
`Prism/MathMotionCurve`; `MathMotionWorld.Concepts` declares the five concepts below. Nothing
outside this folder was edited, and nothing needs to be for either of these to take effect —
running the existing "PRISM > 2. Seed Concept Graph" and shader-registration steps is enough.

## Concepts declared

- `math-motion` (System, Mathematics) — the door. `Composes` derivative, integral, rate-of-change,
  critical-point; `Prerequisite` for `orbital-mechanics`.
- `derivative` (Equation) — `Instantiates` rate-of-change; `Prerequisite` for integral; `Analogy`
  to **`vectors`** (a velocity arrow in the orbital world IS a derivative, drawn as an arrow
  instead of a slope — this is the flagship cross-domain link the brief asked for) and to
  **`periodic-motion`** (the derivative of a periodic function is again periodic, phase-shifted);
  `Prerequisite` for `orbital-mechanics` (velocity is dx/dt).
- `integral` (Equation) — `Analogy` to **`angular-momentum`**: sweeping area under a curve with a
  hand is the same act as a moon sweeping equal areas in equal times: Kepler's second law is an
  integral wearing different clothes.
- `rate-of-change` (Process) — the felt, pre-formal sibling of derivative. `Prerequisite` for
  `periodic-motion`.
- `critical-point` (Fact) — `Measures` derivative; `Analogy` to **`energy-conservation`** (a
  critical point of a potential is where the force vanishes — equilibrium is a zero derivative).

Placement: wedge azimuth 184°–214°, elevation −0.42..0.30, distance 3.6–5.2 m, all within the
assigned band (180–216°, ±0.5, 3.2–5.5 m). `WedgeDirection()` reuses `PrismEnvironment`'s sun
az/el-to-vector convention (`x = cos(el)·sin(az)`, `y = sin(el)`, `z = cos(el)·cos(az)`), the only
azimuth precedent in the codebase, except elevation is taken directly as the y-component rather
than as a further angle, matching how the placement brief states it as a plain ±0.5 bound. If the
intended azimuth reference frame differs from `PrismEnvironment`'s, only `WedgeDirection`'s two
call sites' arguments need adjusting — the concepts and links themselves do not depend on it.

## Interaction summary

- **Deform the curve**: pinch within ~6.5 cm of the curve, drag. Both hands work independently and
  simultaneously. Available in every stage from Wonder onward — nothing about later stages removes
  earlier capability, matching Orbital's own philosophy.
- **Drag the bead**: pinch within ~5 cm of the bead. Available from Discover onward; before that it
  free-rides back and forth on its own.
- **Sweep**: pinch anywhere in the derivative curve's plane (a broad gesture, not a pinpoint grab).
  Available only during Formalize; the swept fill and accumulation trace stay exactly as left
  once the learner moves on, as a settled record rather than a live-updating one (see Known
  limitations).
- **Predict**: pinch anywhere in the derivative curve's plane during Explain to place the marker,
  release to lock it.

All interactive content sits within roughly ±0.24 m horizontally and −0.19 m to +0.23 m vertically
of the anchor, which itself sits 0.50 m in front of and 0.32 m below the eye — well inside normal
arm's reach, not the 0.85–2.85 m the contract's cautionary tale warns about. No pointing-fallback
was implemented for the curve grab (unlike Orbital's moon-reach fallback) since it is not needed
at this range; only direct proximity opens a grab.

## Known limitations / honestly unfinished

- **Sweep/trace freeze after Formalize.** The area-under-derivative fill and the accumulation
  trace stop being recomputed once the learner leaves Formalize (by design — Apply/Explain/Create
  have their own, distinct visual content in the same spatial region, and continuing to update a
  demonstration that has already made its point would compete with them). If the learner keeps
  reshaping the curve afterward (curve-dragging remains available in every stage), the frozen
  trace will visibly stop matching the live curve. This is a deliberate tradeoff, not an oversight,
  but it is the one place in the world where "what you see" can silently go stale.
- **A returning learner mid-loop.** `KnowledgeState` does not persist `_sweepEnd`, `_targetMax`,
  `_targetArea` or which zero-crossing was being predicted — only the stage reached and mastery
  numbers are saved. A learner who quits mid-Apply and comes back gets fresh random targets and an
  empty sweep/trace (handled cleanly: `UpdateVisibility` and the two `_targetsStarted` /
  `_predictionStarted` lazy-init flags in `MathMotionChallenge` re-derive everything from
  `Loop.Stage` alone, so nothing is null or broken — it simply starts that stage's challenge over
  rather than resuming a half-finished one). This mirrors a gap that exists in `OrbitalChallenge`
  for the same reason (`Mode` also only ever starts via the stage-transition event there), so it
  is a shared, known characteristic of the current architecture rather than something novel here.
- **No sound cues wired.** `Companion.PrismVoice` (Consonance/Tension/Settle) was not called
  anywhere in this world. Orbital doesn't call it directly either (the base `OnStage` handles the
  per-stage tone), so this matches that precedent, but a `Settle()` on a completed Apply target or
  a correct Explain guess would be a natural, cheap addition if the master wants it.
- **Tuning is by reasoning, not by eye.** All layout and interaction constants (`XScale`,
  `FnYScale`, `Sigma`, `SteepThreshold`, tolerances, hold durations) were derived from the maths of
  what a Gaussian bump of a plausible hand-drag amplitude actually produces, not from watching it
  run — this world has never been seen. If a maximum or steepness gate feels too easy or too hard
  in practice, `SteepThreshold` (3.0 abstract units) and the `MadeMaximum`/`MadeSteep` prominence
  floor (0.05) in `EndCurveDrag` are the first constants to revisit.
- **No custom sub-editor tooling.** Everything the learner needs is reachable through play; there
  is no debug/skip path exposed, matching the rest of the product's "no button that means I
  understand" stance.

---

## Round 2 — pocket demonstrations (`CalculusDemos.cs`)

The four supporting concepts (`derivative`, `integral`, `rate-of-change`, `critical-point`) were
falling back to `LatentDemo` in the atrium. `CalculusDemos.cs`, namespace `Prism.Worlds.MathMotion`,
adds one bespoke `ConceptDemo` for each, registered by `[ConceptDemoFor("...")]` — no shared file
touched. Each is self-contained (does not reference `CurveField`/`MathMotionWorld`/the custom
shader from Round 1) and uses only the base `ConceptDemo` helpers, matching the house style set by
`DemosMath.cs` exactly: `Curve`, `Body`, `MakeArrow`, `FlatMaterial`, `TryFreeLocal`, `FreePinch`,
`Tint`, `Voice`. All four run real numerics at a small sample count (33–41 points) rather than a
closed-form shortcut, computed fresh every tick.

- **`DerivativeDemo`** (78 lines). A 33-sample curve; the free hand pinches a point and pulls,
  applying a real Gaussian-bump edit every frame (softer and rate-limited compared to the full
  world's snap-to-hand version, for a gentler first touch). A bead paces the curve on its own and
  carries an arrow: direction and length read straight off a central-difference `_d[]` array,
  recomputed after every edit. The arrow is deliberately built to be the same *shape* as a moon's
  velocity arrow in the orbital world — a small arrow riding a moving body, tangent to where it is
  going — per the brief's request that this be the concept that rhymes with orbital. Colour is the
  full world's own law (mint rising, coral falling), not orbital's speed-ramp, since the shape is
  what rhymes and the colour is what stays consistent with the rest of this world.
  Honest: the derivative is real central differences on the *current* array, recomputed after
  every edit, not smoothed. Approximate: the edit is a rate-limited nudge (`dt * 6`) toward the
  hand's target rather than an instantaneous snap, chosen for stability and a softer feel in a
  pocket-sized version — the full world's Explore stage uses the harder snap.

- **`IntegralDemo`** (65 lines). A fixed 33-sample curve (built once, never re-deformed, so it is
  never re-tubed after `Build()` — the one demo of the four that does not call `.Set()` inside
  `OnTick`). The free hand's x-position sets a sweep endpoint; a real trapezoidal sum accumulates
  from the left edge to that point — full slices plus one honest *partial* slice sized to the
  exact sweep position (not snapped to the nearest sample), algebraically verified against the
  sample spacing. The running total grows or shrinks a crystal, signed by colour; a marker riding
  the curve at the sweep point is tinted by the sign of that single slice, so the connection
  between a slice's sign and which way the total moves is visible, not just the total itself. When
  the hand is absent the sweep keeps going on its own (a wrap, not a ping-pong, so it reads as "a
  fresh sweep starting over" rather than "the integral undoing itself").
  Honest: real trapezoidal integration including the partial boundary slice. Approximate: nothing
  meaningfully approximate here beyond the same O(dx²) trapezoidal error the full world carries.

- **`RateOfChangeDemo`** (50 lines). Deliberately has no curve or graph at all — a level rises or
  falls, and the free hand's height sets the rate directly (up = filling, down = draining, colour
  and pulse-speed both carry the magnitude). This is the pre-formal, felt sibling of Derivative:
  the point is to feel "how fast" as a lived rate before any tangent line or formula exists. A
  faint trail behind the level is a real oscilloscope-style sweep (a 40-slot circular buffer,
  written a few times a second, replotted every frame in oldest-to-newest order) rather than a
  growing or shifting list — so a learner who has already visited Derivative can notice, unprompted,
  that the trail's own steepness is the rate they just felt.
  Honest: the level is real (if simple) Euler integration of the hand-set rate, `y += rate * dt`.
  Approximate: when the hand is absent the rate free-runs as a sine rather than holding still,
  purely so the demo is never inert — noted here since it is a deliberate liveliness choice, not
  numerics standing in for something they are not.

- **`CriticalPointDemo`** (61 lines). A fixed two-frequency curve (one broad hump, one finer
  ripple, so there are several genuine critical points of differing character, not one obvious
  peak). The free hand scrubs a marker along x directly; the slope is measured by central
  difference taken fresh at whatever exact x the hand is at (via the curve's own interpolator, not
  a lookup into a precomputed table), continuously, every frame. Nothing is shown until `|slope|`
  drops under a threshold, at which point the marker brightens toward gold, grows slightly, and —
  once, on arrival, not every frame it lingers — sounds a consonance. No number is ever printed:
  the point is to recognise the moment by feel, mirroring how the full world's Explain stage asks
  for a prediction before any measurement is shown. The snap/consonance state machine is a direct
  adaptation of `SymmetryDemo`'s own snap-on-alignment pattern.
  Honest: the slope is a genuine finite-difference measurement at an arbitrary (non-grid-aligned)
  x, taken on demand. Approximate: "critical enough to snap" is a fixed slope-magnitude threshold
  (0.35) rather than a rigorous zero-crossing bisection — for a curve this smooth, at this sample
  density, it does not produce false positives, but it will not find a critical point whose slope
  crosses zero unusually steeply within a single sample interval without the hand passing through
  that interval, since nothing here scans ahead of the hand.

**Allocation note.** All per-vertex work in every demo is `Vector3`/`Color` struct math, written
into a single field-level `List<Vector3>` pre-sized to its exact per-frame element count and
`.Clear()`'d, never reallocated. The one shared cost across all four (and across every existing
demo in `DemosMath.cs`/`DemosMotion.cs`/`DemosSpace.cs` that calls `CurveView.Set()`) is that
`PrismMesh.Tube()` itself allocates four fresh `List<>`s internally on every call — that is
upstream, in `Assets/Prism/Aesthetic/PrismMesh.cs`, which this task does not touch. `IntegralDemo`
and `CriticalPointDemo` avoid paying that cost at all by building their curve once, since neither
one's curve ever changes shape.
