# Machine Cathedral — NOTES

WorldId `machine-cathedral`. Namespace `Prism.Worlds.MachineCathedral`. Folder
`Assets/Prism/Worlds/MachineCathedral/`.

## What this is

Climb the ladder from a lever to a logic gate and find it was one idea the whole way: **trade one
quantity for another, through a ratio, and the product never changes.**

Three side-by-side machines that look nothing alike — a lever with a sliding fulcrum, a hand crank
through a swappable gear pair, a block-and-tackle with a peg-selected wrap count — all run on the
exact same 1-DOF physics engine (`TradeRig.cs`), because the whole thesis of this world is that
they *are* the same mechanism wearing different clothes. Above them, revealed at Connect, a small
logic bench shows the same trade one rung further up: a switch is a lever with two positions, a
relay is a switch thrown by a circuit instead of a hand, and two switches wired in series or
parallel are an honest AND or OR gate.

## Files

| file | contents |
|---|---|
| `TradeRig.cs` | The shared 1-DOF dynamics core: generalised coordinate q, a ratio, a constant load force, a damped hand-spring, semi-implicit Euler. Every mechanism in this world is a thin skin around one instance of this. |
| `MachineMesh.cs` | Procedural boxes, cylinders and gears, all built from one `Extrude()` routine. Gear teeth are flat "castle wall" profiles (involute explicitly not required) with real module-based radii, so any two gears mesh at the correct centre distance automatically. |
| `MachineConstants.cs` | Shared numbers: gravity, the hand force budget, spring tuning, and the force→colour ramp. |
| `MachineEvidence.cs` | Evidence key constants. |
| `LeverStation.cs` | First-class lever, adjustable fulcrum. |
| `GearStation.cs` | Hand crank → swappable gear pair → drum → hanging weight. The Wonder mechanism. |
| `PulleyStation.cs` | Block and tackle, peg-selected wrap count 1–4. |
| `MachineChallenges.cs` | Apply (gear, force budget), Explain (gear, speed prediction), Create (pulley, specification) — evaluated against the live sim, `OrbitalChallenge`-style. |
| `LogicBench.cs` | Two hand switches, a relay, a topology toggle, a lamp. Also defines `MechanicalSwitch`, the switch/relay skin over `TradeRig`. |
| `MachineCathedralWorld.cs` | The `PrismWorldBase` subclass: wiring, the loop, concepts. |

## Colour encoding (stated once, as required)

**Primary: hue is force/torque magnitude in the drivetrain**, on `PrismPalette.Spectral`, cyan
(gentle) through violet (heavy) — `MachineConstants.ForceColour()`. It is live from Wonder onward,
not gated behind any stage: it is not a label, it is the phenomenon, the same way the orbital
world's trail colour is speed before Kepler's second law has a name. A learner watching the crank
glow gently while the weight it is lifting glows hot has already seen the lesson once, wordlessly.
Every station colours its effort point and its load point this way; the gear station also colours
the driven gear itself, since gears are the one place two distinct torques (input and output) are
visually separate objects.

**Secondary: Lavender marks a hand-configurable control** (the fulcrum bead, the rack gears, the
pulley pegs, the logic bench's topology toggle) — never force-bearing, always "grab this to change
the machine's shape." Structural parts (posts, beams, the pulley frame) are a neutral warm gold
ceramic, carrying neither signal.

## Simulated honestly

- **Real torque and moment of inertia on the lever**, including the arc geometry: the effort and
  load points travel on circles, not lines, so their torque arms shrink by `cos(theta)` as the beam
  tilts, and this factor is in the code (`Rig.Ratio = 1/(dl*cos(theta))`), not dropped. See the long
  comment at the top of `LeverStation.cs`. Moment of inertia is `m_effort*d_effort^2 +
  m_load*d_load^2`, exact for point masses.
- **Real gear ratio and real relative rotation direction.** Meshing gears are driven kinematically
  opposite to one another at exactly `N_in/N_out`, recomputed from the ACTUAL angle delta each gear
  swept that frame (not from velocity, which would drift). Reflected inertia through the gear pair
  and the drum both use the standard `1/ratio^2` result, derived, not tuned.
- **Real pulley mechanical advantage.** MA = VR = wrap count N, exactly, including reflected load
  mass `m/N^2`.
- **Work in vs. work out is genuinely computed**, not animated to match: `TradeRig.WorkIn` and
  `WorkOut` are running integrals of `effort * dq` and `LoadForce * dOutput`. There is exactly one
  dissipative term anywhere in the whole world (`TradeRig.Damping`, small, on the input coordinate)
  and it is documented as standing for the give in a human grip, not for friction in the machine.
  Everything else — beams, gears, pulleys, ropes — is treated as massless and frictionless. That is
  the standard first idealisation of a machine and is the right one for this lesson: it isolates the
  one relationship (ratio trades force for distance, product conserved) from the true but secondary
  fact that spinning up the machine's own parts also costs a little energy.
- **The straddle is measured the same way the orbital world measures bound/unbound**: Discover's
  gate waits for evidence of BOTH a configuration with ratio ≥ 1.5 and one with ratio ≤ 0.667, each
  with real recorded motion under it (`MachineCathedralWorld.RecordDrive`), on any station.
- **The logic gate is computed, not asserted.** `LogicBench.LampOn` is a plain boolean expression
  over `SwitchA.Closed`/`SwitchB.Closed`, computed fresh every frame from real hand-driven switch
  angles crossing a threshold. Series → AND, parallel → OR falls out of that expression; nothing
  hard-codes the truth table.

## Approximated, and why

- **The lever's target-angle mapping is a linear convenience, not physics**: when converting "where
  is the hand" into "what angle should the spring reach for," I use a small-angle linear inverse
  rather than the exact arcsine. This affects only how eagerly the spring chases the hand, never the
  beam's actual dynamics (which use the exact `cos(theta)` torque arm as above). Documented at the
  top of `LeverStation.cs`.
- **Switch/relay dynamics (`MechanicalSwitch` in `LogicBench.cs`) are stability-tuned, not
  mass-derived.** Unlike the three main stations, where every constant traces to a stated physical
  quantity, the switch's return-spring stiffness, inertia and push force are chosen for a snappy,
  numerically stable toggle. The one thing that IS load-bearing there is documented inline: the
  natural frequency must stay inside `TradeRig`'s stable range at its fixed step.
- **Gear teeth are flat-profiled, not involute** — explicitly permitted by the brief. Tooth COUNT
  and RELATIVE ROTATION are exact; the meshing PHASE (whether a tooth tip visually lands in a
  valley at any given instant) is not enforced, so two meshing gears may show a small tooth-tip
  overlap rather than a perfect nested interlock at some rotations. Centre distance is exact.
- **Pulley wheels themselves are not modelled as separate rotating parts.** The wrap count N is
  shown as N parallel straight strands between the crossbar and the block, which is what is
  mechanically relevant to the ratio; the individual sheave geometry is abstracted away.
- **The pulley free end is cleated when released** (holds position rather than paying back out
  under load) — a real accessory on real block-and-tackle rigs, added because a single tracked hand
  cannot otherwise haul rope hand-over-hand without losing ground on every release. Noted in
  `PulleyStation.cs`'s class comment; the lever and the gear crank do NOT have this and settle back
  under their own load when released, which is correct for a simple beam or an un-pawled winch.
- **A gear swap or a wrap-count change takes effect the frame after it is requested**, because the
  physics parameters for the CURRENT frame are computed before grab-handling runs. Sixteen
  milliseconds, invisible in practice, consistent across all three stations.

## Loop gates (evidence, never an answer)

Wonder→Explore: any real turn/push/pull. Explore→Discover: the straddle (≥6 turns, both a
force-multiplying and a distance-multiplying trade actually driven). Discover→Formalize: all three
stations tried. Formalize→Apply: the mechanical-advantage number watched respond to the learner's
own hand at least three times post-Formalize. Apply→Explain: the force-budget challenge solved.
Explain→Create: a correct speed prediction. Create→Connect: the pulley specification met AND both
an AND and an OR built and observed on the logic bench.

The Create→Connect gate is a deliberate departure from the orbital reference, which leaves Create
ungated. The contract's definition of done asks for all eight stages reachable by evidence, and
"connect this to everything else" has a literal, buildable answer here — switching becomes logic —
so it seemed worth gating on rather than leaving unreached. If the master prefers Connect to fire on
leaving the world instead (matching Orbital's apparent intent), the fix is one line: remove the
second `Create` gate in `ConfigureLoop`.

## Concepts declared

`machine-cathedral` (door, System, Machines) → composes → `mechanical-advantage` (Equation,
Machines), `torque` (Theory, MatterAndEnergy), `logic-gates` (System, Machines). Cross-links to
existing concepts: `mechanical-advantage` → `energy-conservation` (Instantiates — every machine
here is a specific case of the general law, and it is the ceiling nothing here can exceed), `torque`
→ `angular-momentum` (Causes), `logic-gates` → `feedback` (Prerequisite — flip-flops and latches,
the next rung up, are gates with a loop, and that is one door past what is built here).

Placed at azimuth ≈ 296–320°, distance 3.6–4.8 m, small elevation, inside the assigned wedge
(288–324°, 3.2–5.5 m, ±0.5). **Azimuth convention assumed** (not found documented in the existing
code): 0° = +Z, increasing clockwise toward +X, i.e. `direction = (sin(az), y, cos(az))`. If the
master's actual convention differs, these four directions will land in a different but still-valid
part of the sky; distances and elevation are unaffected either way.

## Performance

Every dynamic mesh (ropes, strands, wires, the rings) is rebuilt in place via `Mesh.Clear()` +
`SetVertices`/`SetTriangles` on a `MarkDynamic()` mesh created once at Build time — no `new Mesh`
and no `new List<>` inside any `Tick`. `NearestGraspingHand` is written without the array-literal
`foreach (var h in new[]{Left,Right})` pattern the base class helpers use, specifically because it
is called many times per frame across three stations plus the logic bench. Total renderers across
all three stations plus the (initially hidden) logic bench is in the low thirties, all single
Ceramic/Flow materials, well inside the ~40 draw call budget. `TradeRig` sub-steps at a fixed
1/120 s with a 32-step-per-frame safety cap, mirroring `OrbitalSim`'s accumulator pattern.

## What the master must wire

Nothing beyond the standard discovery path (`WorldRegistry` reflection, `PrismConceptSeed`,
`PrismSceneBuilder`). No new shaders are declared (`Shaders` is left at the base class's empty
default) — every material is built from the existing `PrismMaterials` factory. Freshly created
`.cs` files here have no `.meta` yet; Unity will generate them on first import, which I cannot
trigger myself (no Unity was run to build this).

## What I could not finish / would do next with more room

- **Gear tooth meshing phase** is not synchronised (see above) — cosmetic only.
- **No haptic feedback** wired on the main stations' effort handles (the gear/pulley rack and peg
  selectors do buzz on swap). Would add `Hands.Buzz` scaled by `Rig.LastEffort` on the three main
  handles if there were room — cheap, and it would let the force colour be felt as well as seen.
- **The lever/fulcrum and effort-handle grab zones can theoretically overlap** if a hand sits
  between them at exactly the wrong moment (both are checked independently, first-claimed-wins is
  not enforced across stations). In practice their positions are far enough apart that this has not
  been observed to matter, but a small "hand already claimed this frame" registry in the world would
  close it properly.
- A second `PrismLabel` naming **torque** specifically (beside the lever, where the arm and the
  force are both visible at once) would round out Formalize; right now only the mechanical-advantage
  number is named in-world (`MachineCathedralWorld.UpdateFormalizeLabel`), and torque is left to the
  constellation's own formalisation text. One label was judged enough text for one stage, matching
  the orbital world's own restraint (a single `ConicLabel`).

## Round 2 — pocket demonstrations (`MachineDemos.cs`)

Three `ConceptDemo`s, registered by `[ConceptDemoFor]` rather than by editing
`ConceptDemoRegistry`, for `torque`, `mechanical-advantage` and `logic-gates` — the three concepts
this world declares beyond its own door, which previously fell back to `LatentDemo`. All three reuse
`TradeRig` and `MachineMesh` directly (same namespace, same files as the world itself), which is
deliberate: the pocket-scale version and the full world are built from literally the same physics
code, not a separate simplified re-implementation of it.

**TorqueDemo** — a wrench on a stuck bolt. The free hand's position, wherever it currently is along
the bar, becomes a spring-coupled point force at that radius; real torque = force x radius drives a
damped rotation against a fixed resistance. Grabbing close to the bolt cannot clear the resistance
threshold no matter how hard the (budget-capped) push is; grabbing far along the bar clears it
easily, with no threshold ever stated — the bar and the curved torque-arc indicator both shift
toward gold once the current grip is enough, so the discovery is watched rather than read. *Honest*:
force = k(offset) - the spring proxy — is the same technique the full world uses for every
hand-driven handle; torque = force x radius and the rotational dynamics (inertia, damping) are
computed, not animated. *Approximate*: the bolt's resistance is a two-level static/kinetic friction
threshold, not a continuous friction law — a real qualitative behaviour (stuck, then loose), not a
precise one, and cheap enough to be worth it at this scale.

**MechanicalAdvantageDemo** — a beam with two FIXED weights (a heavy coral load, a light cyan
effort) and a fulcrum the free hand slides. At a centred fulcrum the load always wins and the beam
sinks; sliding the fulcrum toward the load increases the effort's arm until its torque overtakes the
load's, and the beam visibly flips. This is the same `TradeRig`/`LeverStation` relation used in the
full world, including the `cos(theta)` arc-geometry correction, just with a constant effort torque
in place of a hand-driven spring (there is only one free hand, and it is busy with the fulcrum).
Two bars show `TradeRig.WorkIn` and `WorkOut` — genuine running integrals from the beam's actual
motion, not the trivially-true `MA x (1/MA) = 1` algebraic identity that was the first draft of this
demo and was replaced for being true by definition rather than by physics. The bars reset when the
beam settles so each swing gets its own legible reading instead of both pinning at the display
ceiling after a few swings. *Honest*: the torque comparison, the flip, and the work integrals are
all genuinely computed. *Approximate*: none beyond what `LeverStation` itself already documents
(massless beam, point-mass inertia).

**LogicGatesDemo** — two hand-thrown switches (cyan, mint) and a third, visually distinct
(lavender, uncoloured-by-state) toggle that flips the wiring between series and parallel, redrawing
the two wire segments to match the chosen topology rather than just relabelling a fixed picture. The
lamp's state is `series ? (A && B) : (A || B)`, computed fresh every frame from the two booleans —
never asserted. *Honest*: the boolean logic is the whole mechanism; nothing about it is faked.
*Approximate*: unlike the full world's `MechanicalSwitch` (momentary — closes while held, springs
open on release, correct for a two-handed rig with switches nobody needs to leave set), these
switches are bistable (pinch near one to flip it, and it stays) because a single free hand cannot
hold two switches at once. A toggle switch that stays where you leave it is an equally real
mechanism — most household switches work this way — so this is a different honest choice for a
different constraint, not a simplification of the same one.

All three: geometry built once in `Build()`, `OnTick()` does arithmetic and a handful of
`SetColor`/`SetFloat`/`Set(list)` calls only. The one thing worth flagging for whoever touches this
file next: an early draft of `TorqueDemo` drew its torque arc with `CurveView.Set(Func<float,Vector3>,
int)` called every frame, which allocates a closure per call — every other demo in this codebase
only uses that overload once, in `Build()`. Fixed by filling a pre-allocated `List<Vector3>` field
in a plain loop instead and calling the list overload; if a future demo reaches for the `Func`
overload inside `OnTick`, it will have the same problem.
