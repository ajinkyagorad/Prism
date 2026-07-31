# Living Cell — build notes

## What this is

A cell as a chemical economy, not a diagram. The learner stands in front of a translucent,
arm's-reach cell (a 10 cm-radius membrane, matching the orbital world's table-top-scale precedent
rather than a room-scale enclosure — see "Design choices" below). It has one glucose channel they can
block with a hand, a handful of glucose motes they can grab and carry in, and a membrane they can
pinch to punch a temporary leak in it. Underneath, a real coupled-ODE simulation runs continuously:
Michaelis-Menten enzyme kinetics, a genuine discretised diffusion PDE for oxygen, and an exactly
conserved ATP/ADP currency, all driven by the same three hand interactions the whole way through the
loop.

Files, all in `Assets/Prism/Worlds/LivingCell/`, namespace `Prism.Worlds.LivingCell`:

- **`LivingCellWorld.cs`** — the `PrismWorldBase` subclass. Geometry, hand interaction, the eight-stage
  loop's gates, and the five `ConceptSpec`s.
- **`CellSim.cs`** — the simulation. Plain C#, no MonoBehaviour, mirrors `OrbitalSim`'s shape.
- **`CellChallenge.cs`** — Apply/Explain/Create, mirrors `OrbitalChallenge`'s split from the world.
- No shaders. Every visual need was covered by existing `PrismMaterials` (Gel, Ceramic, Seed, Flow,
  Volumetric) — see "Colour encoding" for how each is used and why a bespoke shader was not worth the
  risk of shipping unverified HLSL with no compiler in the loop.

## The simulation: what is honest, what is approximate

**Real and exact:**
- Every state variable is an *amount* (mole-like units) in a fixed-volume compartment, and
  concentration is always amount/volume computed on demand. Every transport and reaction term moves
  an identical quantity out of one field and into another in the same statement, so mass conservation
  is not asserted, it is structurally impossible to violate (see the comment above `CellSim.Step`).
- The adenylate pool (ATP + ADP) is conserved exactly and is additionally re-derived every step as
  `Adp = ApoolTotal - Atp` after clamping — so the invariant holds even in extreme steps, not just
  "on average."
- Oxygen crosses the membrane by a genuine explicit finite-difference diffusion chain (5 shells,
  `dC_i/dt = D*(C_{i-1} - 2C_i + C_{i+1})`, the discretised Fick's second law) — a real spatial
  gradient the learner can watch move, not two lumped numbers with a gradient implied. The stability
  bound (`D*dt < 0.5`) is respected with about an order of magnitude of headroom.
- Glucose transport and catabolism are Michaelis-Menten throughout, never a bare linear rate — every
  enzyme-mediated step saturates because the enzyme is a finite, reusable resource. Catabolism
  additionally saturates in **ADP concentration**, which is real biology (respiratory control) and is
  not decorative: it is what makes "more ATP -> less ADP -> less substrate for making more ATP" an
  actual negative-feedback loop computed by the equations, not a claim layered on top of them. That
  mechanism is the literal justification for the `metabolism --Analogy--> feedback` concept link.

**Approximate, and stated where it matters most (also in code comments at point of use):**
- **Two or three well-mixed compartments, not a full 3D spatial PDE.** Concentration is uniform
  within "outside," within each oxygen shell, and within "cytoplasm" at any instant. This is a fair
  simplification for something the size of one cell (real cells rely on diffusion being fast at that
  scale) and is exactly the kind of honest model-simplification the base class's own documentation
  asks for — the alternative (a 3D finite-difference grid) would have cost far more of the frame
  budget for no pedagogical gain at this scale.
- **Catabolism stoichiometry is simplified**, not the literal 1 glucose : 6 O2 : ~30 ATP of real
  aerobic respiration. One reaction turnover consumes one unit of glucose and one unit of oxygen
  together and produces waste at a matched rate (so `Gin + O2in + Win` is exactly conserved by that
  reaction, which is a real and checkable statement) plus `AtpYield` (3, tunable) units of ATP from
  ADP. The *qualitative* behaviour — multiple required inputs, saturating rate, ADP-gated throttling —
  is real; the *ratios* are simplified for a legible timescale.
- **Distances inside the diffusion chain are dimensionless "shell units"** (spacing = 1), the same
  spirit as `PrismScale` inventing a stylised `mu` rather than using real orbital G*M — a stated scale
  choice, not a hidden shortcut.
- **Rate constants were sized by order-of-magnitude reasoning, not empirical tuning.** There is no
  compiler or runtime in this loop, so nobody has watched this simulation actually run yet. Every
  constant lives at the top of `CellSim.cs` with a one-line comment on what raising or lowering it
  does. What I *can* guarantee without running it: the system is a genuine negative-feedback loop
  (traced by hand in the `CellSim` class comment) so it settles rather than oscillating or blowing up,
  every flux is safety-clamped against overshooting its source compartment negative, and there is an
  unconditional floor-clamp at the end of every step as a last-resort numerical backstop. What I
  *cannot* guarantee by inspection alone: that the idle steady state, the shortage/surplus thresholds
  (ATP fraction 0.22 / 0.82), and the Apply-stage spike survival floor (0.10) land exactly where they
  feel best in play. **If the first person who runs this finds the cell drifts to an extreme on its
  own, or shortage/surplus feel too easy/too hard to trigger, the fix is nudging `DemandBaseline`,
  `VmaxCatabolism`, or `VmaxChannel` in `CellSim.cs` — not the gates or the interaction code.**

## Colour encoding — two laws, both stated in the `LivingCellWorld` class comment

1. **Concentration -> `PrismPalette.Spectral`**, for the two genuine continuous fields only: the
   cytoplasm's tint (`t` = ATP fraction) and each of the five oxygen beads (`t` = local
   concentration / reference). A starving region and a saturated one are visibly different colours
   before any number appears, per the brief's suggested candidate.
2. **Fixed identity colour** for discrete, non-field objects, where mapping one small bead to "the
   current concentration" would be unreadable: glucose (motes, the channel port, its flux tube, and
   ATP tokens when charged) is always **Gold** — one hue for "usable chemical energy" wherever it
   appears, including what it becomes. Spent energy (the empty side of an ATP token) is a dim
   **Violet**. Oxygen's flux tube is **Cyan**. Only brightness, scale and animation state change on
   these — never their hue.

## The eight stages

Gates are in `LivingCellWorld.ConfigureLoop`, evidence keys in `LivingCellEvidence`. The one that
matters most mirrors the orbital world's bound/unbound straddle exactly: Explore -> Discover waits
until the learner has driven the **same single quantity** (ATP fraction) to both a shortage
(< 0.22) and a surplus (> 0.82) — one by blocking the channel, the other by feeding it — before the
gradient beads, the ATP token row, and the flux currents are revealed. Discover -> Formalize then
asks for evidence of understanding rather than just of breaking: the learner must bring the cell back
into a healthy band (0.35-0.75) and hold it there for two seconds, having previously disturbed it.
Apply/Explain/Create are a demand spike, a blocked-channel prediction placed on the (now-visible) ATP
token row, and a one-dial tuning-plus-survive-a-cycle exercise — see `CellChallenge.cs`.

## A reentrancy pattern worth knowing about before extending this

`Loop.Evidence.Record(...)` can **synchronously** advance the loop and call the next stage's
`OnStageEntered`, which for this world can call `CellChallenge.BeginPrediction()` or `BeginCycle()`
— *while still on the call stack of the `Evaluate*` method that just recorded the evidence.* The
first draft of `CellChallenge` recorded `SpikeSurvived`/`PredictionGood`/`CycleSurvived` and then
unconditionally set `_mode = Mode.Idle` right after, which silently clobbered the `_mode = Mode.
Prediction` / `Mode.Cycle` the reentrant `Begin*()` call had just set — breaking the Apply->Explain
and Explain->Create handoffs with no error, nothing to catch on inspection except tracing the call
graph by hand. Fixed by guarding each reset (`if (_mode == Mode.Spike) _mode = Mode.Idle;` etc.) —
all three sites in `CellChallenge.cs` carry a comment explaining why. Anyone adding a fourth
challenge stage should keep the same guard.

## What the master should verify (not necessarily do — the infrastructure looks ready)

- `Assets/Prism/Worlds/WorldRegistry.cs` (present when I started writing, not authored by me) already
  discovers every `PrismWorldBase` subclass by reflection, and `PrismSession.cs` already has a
  `List<PrismWorldBase> Worlds` it wires `Hands/Head/Knowledge/Companion` onto generically. On the
  evidence available to me `LivingCellWorld` should be picked up **automatically** — I did not need
  to (and, per the contract, must not) touch `PrismSceneBuilder.cs` or `PrismConceptSeed.cs` myself.
  Worth a quick check after a build that `World_LivingCell` (or equivalent) actually appears in the
  built scene and that the concept seeder is pulling from `WorldRegistry.AllWorldConcepts()` — I could
  not confirm the seeder side from inside my own folder.
- No shader registration needed — `Shaders` is left at the base class's empty default since every
  material is built from existing `PrismMaterials` entries.

## Known gaps / things I would do next with a compiler

- The pinch-puncture ("pinch a membrane") is a material pulse (membrane density/glow) plus the real
  permeability spike underneath it — there is no mesh deformation or particle burst at the pinch
  point. Functionally complete, visually plainer than it could be.
- Waste is simulated and conserved (`Win`/`Wout`) but has no dedicated visual — it never blocks or
  slows anything the learner can see, so I judged the draw-call budget better spent on the oxygen
  gradient and the ATP row. It is there in `CellSim` if a future pass wants to surface it.
- The Apply spike and Create cycle each use one fixed profile (ramp-hold-ramp; a sine). Fine for a
  first pass; a repeat visit to this world plays out identically each time within a stage.
- `NearestHand()` (inherited from `PrismWorldBase`, used for mote- and slider-grab detection) builds a
  small `new[] { Left, Right }` array internally on every call — a handful of these happen per frame
  (once per idle mote, once for the slider). I removed every instance of this pattern I introduced
  myself (`ServiceChannelBlock`, `ServicePinch`, `CellChallenge.ServiceMarkerPlacement` all now take
  two explicit calls instead), but could not remove the copies inside the base class helper without
  editing a file outside my folder. The same pattern already exists in `OrbitalWorld.TryDragArrow` and
  in `PointedAt<T>` on the base class itself, so this is a pre-existing, small, fixed-size (2-element,
  reference-type) allocation already accepted elsewhere in the reference implementation — flagging it
  rather than hiding it.
- Numeric tuning is unverified end-to-end (see "rate constants" above) — this is the one item I'd
  actually want a device or Play Mode session for before calling the pacing final.

## Design choices worth defending explicitly

- **The cell sits at arm's reach in front of the learner, not around them.** The brief's Wonder stage
  says "inside a cell"; I chose the same resolution the orbital world already uses for an analogous
  problem (a learner is not literally scaled to a planet either) — a graspable, reachable, translucent
  body you can lean toward and see into, rather than a room-scale shell that would put every control
  out of arm's reach and violate the contract's hard reachability rule. Framing carries the "inside a
  living process" feeling; scale carries interactivity.
- **Only glucose has a blockable channel; oxygen only diffuses.** This is not a simplification for
  convenience — it is the actual biological asymmetry (glucose needs a transporter protein, oxygen
  crosses the bilayer unaided) and it is what makes "block a channel with a hand" a legible, singular
  action: there is exactly one door, and the learner always knows what they just did.

## Round 2 — pocket demos for the four supporting concepts

`LivingCellDemos.cs` gives `diffusion`, `enzyme-catalysis`, `membrane-transport` and `metabolism` a
bespoke `[ConceptDemoFor]` pocket demonstration each, so holding them in the constellation no longer
falls back to `LatentDemo`. All four are single self-contained ideas, ~60-100 lines, built with the
`ConceptDemo` helpers (`Ball`, `Cloud`, `Curve`, `Body`, `FlatMaterial`, `Segment`), and none allocates
on the heap inside `OnTick` — every array is sized once in `Build()`, and buffer swaps (the diffusion
demo's `_c`/`_next`) reuse the same two arrays forever, the identical technique `WaveDemo` uses. Where
a demo mirrors a mechanism from the full world, that is deliberate: a learner who plays with the pocket
version and later enters Living Cell should recognise the rule, not meet it cold.

- **`DiffusionDemo`** — a row of 14 concentration cells solving a real, explicit finite-difference
  step of Fick's second law in one dimension (`dC/dt = D*d2C/dx2 - decay*C`), the same equation the
  full world's 5-shell oxygen chain solves, just uncoiled into a line instead of a chain between two
  reservoirs. Pinching anywhere along the row injects concentration there; colour is
  `PrismPalette.Spectral` of the local value, so the world's Law 1 (concentration -> spectral ramp)
  carries over exactly. **Honest:** the PDE step, the stability clamp on `k`, the decay term (an open
  medium that also clears itself, not just a closed dish). **Approximate:** the decay term has no
  equivalent inside the full `CellSim` — it exists here only so a pocket demo run for a while returns
  to a legible starting point rather than needing to be reset by hand, and is flagged as demo-only.
- **`MembraneTransportDemo`** — two compartments, one gate, real flux `J = permeability * (C_out -
  C_in)` integrated every frame. The free hand's proximity to the gate drives permeability toward
  zero — literally "block a channel with a hand," at pocket scale. **Honest:** the flux law itself,
  and the fact that the current (a `Flow`-shader tube) visibly slows and dims exactly when either the
  gate is shut or the gradient has already flattened, never one without the other. **Approximate:**
  the outside is a fixed, non-depleting reservoir rather than its own conserved pool (see the same
  choice defended for the full world above) — correct for illustrating gating, not a claim about
  where the outside's supply comes from.
- **`EnzymeCatalysisDemo`** — the one demo here that could not be reduced to a formula and stayed
  honest, so it is not a formula: four docking sites and nine substrate motes, each an individually
  simulated agent. A free mote binds an empty site on contact; a bound mote is released only when
  that SITE's own turnover timer expires, never sooner, regardless of how many motes are waiting.
  The free hand's height sets how many motes are "present" (the local substrate concentration).
  **Honest:** this produces real Michaelis-Menten saturation as a CONSEQUENCE of the mechanism — at
  low concentration sites idle between visits and the reaction rate tracks concentration almost
  linearly; at high concentration every site is refilled the instant it opens and adding more
  substrate stops changing anything — rather than asserting `v = Vmax[S]/(Km+[S])` and animating
  something that matches it. **Approximate:** nine discrete motes is a small enough population that
  the saturation curve is visibly noisy rather than smooth; a real enzyme population is not.
- **`MetabolismDemo`** — a ten-token ATP/ADP ring running the exact same rule as the full world's
  currency: production saturates in ADP availability, consumption saturates in ATP availability
  (`Sat(x, Km) = x/(Km+x)` on each side), and the free hand sets a production multiplier rather than
  an absolute rate. **Honest:** the mechanism is the full `CellSim`'s respiratory-control idea
  narrowed to one scalar pool with no spatial structure — genuinely the same equations, not a
  lookalike. A needle above the hub shows net direction and turnover magnitude, so "empty," "full and
  idle" and "full and roaring" are three visibly different states rather than two. **Approximate:**
  a single well-mixed pool with one production term and one consumption term, where the full world has
  a whole reaction network (transport, catabolism, work) feeding into the same currency — this is the
  claim "production must track consumption" isolated from everything that produces or spends it.

All four were checked by hand against `ConceptDemo.cs`'s exact helper signatures and against every
shader property they touch (Ceramic's `_Luminance`, Volumetric's `_Density`/`_EdgeTint`, Flow's
`_Strength`/`_Speed`/`_Pulse`) — there is still no compiler in this loop, so that hand-check is what
stands in for one.
