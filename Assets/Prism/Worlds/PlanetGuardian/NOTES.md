# Planet Guardian — build notes

A hand-held planet whose temperature is governed by a real zero-dimensional energy balance
model, with real thermal inertia and a genuine ice-albedo tipping point. The learner drives it
warm and cold with three controls, discovers that it keeps moving after they let go, and
eventually learns why: energy in and energy out are two different numbers, and the planet's
temperature is just whichever way it is currently leaning.

Files, all in `Prism.Worlds.PlanetGuardian`:

- `PlanetGuardianSim.cs` — the physics. Plain C#, no MonoBehaviour, mirrors `OrbitalSim`'s shape.
- `PlanetGuardianWorld.cs` — the `PrismWorldBase` subclass: geometry, hand interaction, the loop,
  the five `ConceptSpec`s.
- `PlanetGuardianChallenge.cs` — Apply / Explain / Create, mirroring `OrbitalChallenge`'s split
  of "the living system" from "the tests put to it."
- No shaders. See "Why no shaders" below.

## The physics, and what is honest vs approximate

The model is exactly what the brief asked for:

```
absorbed = (1 - albedo) * S / 4
outgoing = epsilon * sigma * T^4        (real Stefan-Boltzmann constant)
C dT/dt  = absorbed - outgoing
```

Real, not approximated:
- `sigma = 5.670374419e-8`, the actual constant.
- The solar constant is *derived*, not picked to look right: `SolarConstantRef` is solved so the
  default configuration (288 K, manual albedo 0.30, GHG at the pre-industrial reference) is in
  **exact** steady state against Earth's real outgoing radiation, 239 W/m^2 — the same move
  `PrismScale` makes deriving `mu` from a chosen orbit rather than picking a number that merely
  looks plausible. It lands at ~1377 W/m^2, close to (not bit-exact with) the commonly cited
  1361-1366 W/m^2 — see "A bug I found and fixed" below for why it isn't the cleaner ~1366.
- CO2 forcing uses the actual IPCC AR5 coefficient, `5.35 * ln(C/C0)`, referenced to the real
  pre-industrial 280 ppm. Doubling CO2 from any starting point therefore always costs the same
  ~3.7 W/m^2, which is a real, well-known property of the logarithmic form and falls out for
  free rather than being asserted.
- Ice-albedo feedback is a genuine function of temperature (`IceFractionAt`, a smoothstep
  between two thresholds), not a scripted event. Whether the system is locally stable or
  unstable at any given temperature is computed from the actual slopes of the two curves, not
  authored — see the derivative check below.
- The integrator is a semi-implicit (linearised backward-Euler) step on the T^4 term. This
  changes nothing about the physics, only how it is stepped, and is a standard technique for
  stiff radiative relaxation. It is unconditionally stable at these numbers: `HeatCapacity/dt`
  (~1800) dwarfs both the Planck response (~2-3 W/m^2/K) and the ice-albedo slope at its
  steepest (~5-8 W/m^2/K), so the model integrates smoothly through the unstable zone across many
  real substeps rather than overshooting numerically. Fixed step at 1/30 s, accumulated from real
  time, capped catch-up — same pattern as `OrbitalSim.Advance`.

Compressed, and said so in the code: real ocean mixed-layer heat capacity is on the order of
3x10^8 J/(m^2 K), which would take years to show a learner anything. `HeatCapacity = 60`
compresses that by roughly six orders of magnitude so the *same physics* settles in tens of
seconds (tau ≈ 19 s near the reference state) instead of years — the identical move `PrismScale`
makes choosing `mu` for orbital gravity, for the identical reason.

Approximated, and said so in the code: this is a *zero-dimensional* model — one temperature for
the whole planet, no latitude. Real poles are colder than the real equator at any given global
mean; this model cannot represent that, because it has no "where" at all. The ice-line thresholds
are calibrated against the **global mean** so the default 288 K state still shows real, modest
polar ice, the way Earth is icy at the poles despite a 288 K planetary average. This is the
standard, named limitation of a 0-D energy balance model — it is exactly why Budyko, Sellers and
North built latitude-resolved (1-D) models next. A truer next step for this world, if anyone
wants to invest in it, is exactly that: a ring of latitude bands each with their own T, coupled
by a diffusion term. Also approximated: the planet's decorative spin does nothing to the model
(a 0-D EBM has no day/night or seasons either), and CO2 forcing is linearised about the reference
climate rather than re-derived from the instantaneous temperature each step — which is how real
forcing numbers are conventionally quoted in the first place, not a simplification invented here.

## A bug I found and fixed (worth reading before touching the constants)

My first pass derived the solar constant against the *manual* albedo (0.30) alone. But the
running simulation always adds an ice contribution on top, and at 288 K with the ice-band I'd
first chosen, that ice term was large enough that the "default" state was not actually balanced —
there was a residual ~9 W/m^2 of net cooling sitting at the supposedly-resting start, and, worse,
the *local slope* at 288 K was net-unstable (the ice-albedo feedback there was slightly steeper
than the restoring Stefan-Boltzmann response). A planet in that state does not hold steady — it
quietly runs away into a snowball with nobody touching anything, which would have broken the
single most important promise of Wonder ("a temperature that is holding steady") before the
learner ever arrives.

I caught this by writing a parallel Python port of the exact stepper (not a substitute for
running it in Unity, which I cannot do, but a real check of the arithmetic and the dynamics) and
simulating 120 seconds of untouched default state. It drifted. The fix has two parts, both in
`PlanetGuardianSim.cs`:

1. `SolarConstantRef` is now derived against the **total** default albedo, manual plus the ice
   fraction the model actually produces at 288 K (`DefaultIceFraction`), not manual alone. This
   guarantees exact balance at the default state regardless of exactly how the ice constants are
   tuned.
2. `TIceFree` was moved from 293 K to 290 K, and `TIceFull` sits at 258 K — so 288 K sits near
   the *warm edge* of the band (x ≈ 0.94 of the way across), where the smoothstep's slope is
   gentle, rather than near the middle, where it is steepest. Checked numerically: the local
   slope of `d(imbalance)/dT` at 288 K is **-1.43 W/m^2/K (stable)**, while the same quantity at
   the band's centre (~274 K, reachable by pushing any control) is **+5.2 W/m^2/K (unstable)** —
   confirmed with a semi-implicit stepper run for 120 s at the default: it holds at 288.0000 K to
   four decimal places. The default ice fraction that results is small (~1.1%), which through the
   sphere-intersection geometry the world uses for the ice caps still renders as a real, visible
   polar cap of several degrees of latitude, not an invisible sliver — verified by solving the
   actual sphere-sphere intersection angle, not eyeballed.

I mention this at length because it is exactly the kind of error that "looks plausible" from the
formulas alone and only shows up by actually running the dynamics forward — which is the whole
argument for writing a throwaway numerical check rather than trusting algebra done by hand when
there is no compiler in the loop.

## A second bug: the Create-stage check could reward failure

Early version: "did the temperature come back within 4 K of where it was, for 2 continuous
seconds, at any point in the 50 s after the pulse." Simulating a genuinely-tipping configuration
(one that fails, and should) showed it sitting inside that 4 K tolerance for the *first* ~30
seconds after the pulse — not because it had recovered, but because thermal inertia meant it
simply hadn't drifted far *yet*. The naive check fired "stable" on a planet that was actively
capsizing. Fixed with `PostSettleGrace = 30f`: proximity is not trusted until 30 s after the
pulse ends, by which point a genuinely-diverging configuration has clearly left tolerance and a
genuinely-stable one has settled back (checked against four representative configurations —
two that should pass, two that should fail — all four now classify correctly).

## A third finding: the Apply target range had a dead zone

Original design picked a target uniformly from 266-300 K. Testing with a simple proportional
hand-controller (not a search for the *best* strategy, just a plausible patient one) showed that
targets in roughly 215-285 K cannot be *held* by incremental control at all — that whole span
sits inside the unstable region between the two real equilibria, so any target there is not hard,
it is impossible, which is a worse thing for a challenge to be. `BeginTargetChallenge` now picks
from one of two verified-holdable zones instead: a warm hold (292-308 K) or a cold hold
(198-213 K), chosen at random. Both were confirmed reachable and holdable from a default 288 K
start with a deliberately unsophisticated controller, so a patient human should find them hard
for the honest reason (the delay makes overshoot easy) rather than impossible for a hidden one.

## Colour law

Stated once, in a comment at the top of `PlanetGuardianWorld.cs`: **the atmosphere shell is the
only place this world uses `PrismPalette.Spectral`, and it encodes the current ENERGY IMBALANCE
(absorbed minus outgoing), not temperature.** Cyan is losing energy, the ramp's warm end is
gaining it, the middle is balance. I chose imbalance over temperature deliberately — it is the
more interesting reading, per the brief's own suggestion, because imbalance is what predicts
where the temperature is *headed*, which is invisible if only the temperature itself is shown.
It is also the quantity that makes the central lesson legible at a glance: the planet can be
glowing "still warming" cyan-to-gold long after a hand has let go of every control.

Temperature itself is read a different, non-colour way: through the ice caps' latitude. Two small
spheres, radius just over the planet's own, positioned along the pole axis and pulled toward the
centre as `IceFraction(T)` rises — at zero ice they are pulled out to a tangent point (invisible),
at full ice they are concentric with the planet (a complete shell). The visible cap is the actual
sphere-sphere intersection, which traces a genuine circle of constant latitude — this is not a
decorative shrink/grow, the edge shape is the real geometric consequence of the same threshold
driving the physics.

The in/out flow tubes (revealed at Discover) carry fixed identity colours instead of the ramp —
gold for sunlight in, cyan for radiated heat out — with packet rate as magnitude. Two different
non-negative flows are a better fit for "which flow, how much" than for a single signed ramp, and
keeping them off the Spectral law avoids two different meanings competing for the same hue near
the planet.

Everything else (base planet body, GHG haze, levers, thermometer beads) uses fixed identity tints
from the palette and is not meant to encode a continuous quantity — the GHG haze's *density*
(not colour) tracks the GHG lever, and the base planet's *luminance* (not hue) tracks surface
albedo, since brighter-is-more-reflective is literally true rather than decorative.

## Interaction

Three beads on three short rails, fanned in front of the planet, each a direct 1-DOF drag
(project the hand onto the rail axis, clamp 0..1, map to the control's real range). No labels
until Formalize. A hand-drag was chosen over a more elaborate "handful of gas" mechanic for the
GHG control specifically because it is robust to write correctly without a compiler and reads
identically for controller and hand tracking, at the cost of being a slightly more abstract
interaction than "grab the substance itself" — a fair trade given the risk.

A real bug caught in review: the three lever grab-checks were independent, so a hand pinching
near the boundary between two adjacent beads could satisfy both proximity checks and drag them in
lockstep. Fixed with `IsHandBusyOnAnotherLever` — a hand already dragging one lever cannot start
dragging another in the same frame.

The thermometer rail (Formalize onward) carries up to three beads on the same axis: a live
"current" bead, a static "target" bead (Apply), and a draggable "prediction" bead (Explain) —
reusing one instrument for reading, aiming and guessing rather than inventing three UI widgets.

The Create-stage disturbance is a small pinchable mote (not a thrown object) that triggers a
raised-cosine forcing pulse — a volcanic aerosol event, the real, well-documented direction
(volcanoes cool). It can be retried on a 2-second cooldown, and nothing about which configuration
survives it is prescribed — that is what makes the design the learner's own.

Reachability: `Reach = 0.42 m`, `EyeToTable = 0.36 m` (closer than Orbital's 0.55/0.50, since this
is a globe held in two hands, not a table seen from a slight distance). Every interactive element
sits within about 0.05-0.19 m of the anchor origin — tighter than Orbital's proven 0.16 m moon
cradle. All grabbing is direct-touch (`NearestHand` / proximity to a bead); I did not add a
cone-based pointing fallback the way Orbital's moon-grab has one for a learner seated too far
away. Given how much closer everything already sits, I judged the risk low enough to accept, but
it is the first thing I would add if the master wants extra reach robustness.

## The loop

Evidence-driven throughout, in `PlanetGuardianEvidence`. The gate that matters most is
Explore -> Discover: it requires a warm excursion, a cold excursion, *and* at least one measured
episode of the temperature continuing to move by more than 2.5 K well after every hand let go
(`RunawayFelt`, computed from a snapshot-and-compare against the live sim, not asserted). This was
tested against the real stepper for five plausible play patterns (single-lever pushes in both
directions, moderate multi-lever pushes, a deliberately marginal push) and fires correctly in
all of them, typically within 5-40 seconds of idle drift depending on how hard the push was —
see the "warm excursion" threshold note in `PlanetGuardianWorld.cs` for the exact numbers this
was checked against.

Discover -> Formalize requires a newly-found balance point away from the default (`Rebalanced`) —
a deliberate act of retuning, echoing Orbital's "used the arrow handle for a near-circular orbit."
Formalize -> Apply requires having touched all three controls with the readout visible, echoing
Orbital's "produced all three kinds of path." Apply, Explain and Create are described above.
Connect is terminal (no gate), matching the Orbital pattern exactly.

## Concepts

Five, per the brief: the door concept `planet-guardian` (System, EarthAndCiv) plus `energy-balance`
(Equation, MatterAndEnergy — deliberately a different domain from the rest of the cluster, so the
constellation shows a real cross-domain edge even within this one world's own contribution),
`albedo` (Fact), `equilibrium` (Theory), `tipping-points` (Theory), all EarthAndCiv. Two links to
existing concepts, exactly the two the brief calls out as "genuinely the same ideas": `feedback`
(Analogy — the ice-albedo mechanism *is* a feedback loop, not merely similar to one) and
`energy-conservation` (Instantiates, from `energy-balance` — a planetary energy budget is a
concrete case of the general law, the same relation `escape-velocity` already has to it).

Placement uses `WedgeDir(azimuthDeg, elevationRad)`, matching `PrismEnvironment`'s own sun-vector
convention exactly (`(cos(el)*sin(az), sin(el), cos(el)*cos(az))`, az from +Z toward +X, el as an
angle above the horizontal) — I inferred azimuth-in-degrees/elevation-in-radians from the task
prompt's units not matching (293 degrees would be nonsensical stated as "+/-0.5"; PrismEnvironment
uses exactly this degrees-for-azimuth, radians-in-practice-via-Deg2Rad-symmetry pattern already),
and it is the only azimuth/elevation convention that exists anywhere else in this codebase, so
matching it seemed safer than inventing a second one. All five concepts sit inside the given
wedge (azimuth 144-180 deg, distance 3.2-5.5 m, elevation +/-0.5) with margin.

## Performance

No custom shaders — every material comes from the existing `PrismMaterials` factories (Ceramic,
Gel, Volumetric, Flow, Seed), all of which are already unconditionally registered in
`PrismMaterials.AllShaders`. This was a deliberate compile-safety choice: I have no way to
compile-check HLSL, and Unity shader errors are exactly the kind of failure that would be
invisible to me and fatal to the build. `Shaders` is left at the base class default (empty array)
since nothing here needs registering beyond what already is. If the master wants a true
continuous-latitude ice shell (rather than the sphere-intersection trick, which is honest and
cheap but coarser than a per-fragment latitude test) or a more elaborate gas-haze look, a bespoke
shader is the natural next investment — the sphere-intersection approach was chosen specifically
to get a real, honest ice edge without writing one.

No per-frame mesh rebuilding anywhere — every mesh in this world (rails, flow tubes, the planet
body) is built once in `BuildWorld` and only ever moved, scaled or has its material properties
changed afterward, which is a step further than `OrbitalWorld`'s own trails/conics (which rebuild
their mesh every frame they're visible). About 21 draw calls total, comfortably under the ~40
budget. The numeric readout label throttles its string rebuild to only fire when the rounded
displayed values actually change, rather than every frame.

## What I could not verify and would flag to the master

- No pocket demo (`ConceptDemo` subclass) for the four supporting concepts, so reaching for
  `energy-balance` / `albedo` / `equilibrium` / `tipping-points` in the atrium (before ever
  entering the world) currently falls back to `LatentDemo` — an honest gap rather than a fake
  animation, per that system's own stated design, but worth a bespoke demo if there's time. I did
  not write one because `Demos/**` is outside my folder and adding new demo types requires an
  edit to `ConceptDemoRegistry.cs`, which I cannot touch.
- I have not run this in Unity or on a headset — cannot, by the rules of this build. Every claim
  above about the *dynamics* (stability at 288 K, the runaway detector firing, the two Apply
  zones, the Create grace-period fix) was checked with a from-scratch Python port of the exact
  stepper logic, run to completion many times against the exact same formulas that are in
  `PlanetGuardianSim.cs`. That verifies the math and the state machines; it does not verify hand
  ergonomics, visual scale, or draw order, which only a headset can.
- Two low-probability, low-impact edge cases I chose not to engineer around given their cost:
  (1) `Rebalanced` and `IsSettled` are both instantaneous/derivative threshold checks, and in
  principle a trajectory passing *exactly* through the unstable equilibrium (~284.8 K at default
  settings) could momentarily satisfy either. Landing precisely there via imprecise hand
  interaction is very unlikely, and when it was forced in testing the resulting error was small
  (a couple of Kelvin, well inside the 6 K prediction tolerance) rather than a wrong pass on a
  gate. (2) The Apply/Explain/Create numeric thresholds (2.5 K, 4 K, 6 K, the various hold
  durations) are reasoned choices verified to work for the scenarios I could script, not values
  tuned against real human hands.

---

## Round 2 — pocket demonstrations (`ClimateDemos.cs`)

Four `ConceptDemo` subclasses, one per supporting concept, registered with `[ConceptDemoFor]` so
no shared file needed editing. Read `Assets/Prism/Demos/ConceptDemo.cs` and `FeedbackDemo` in
`DemosMath.cs` first, per the brief — the grammar (holding hand carries it, free hand operates it)
and the helpers (`Ball`, `Curve`, `Sheet`/`SheetApply`, `Segment`, `MakeArrow`) are all reused
exactly as the existing seventeen demos use them, nothing new added to the base class.

Same discipline as Round 1: I have no compiler and no headset, so every piece of *dynamics* below
(not just the formulas — the actual behaviour over time) was checked with a throwaway Python port
before being written into C#. Two things that check caught are noted below; both would have been
invisible from reading the code and only showed up by running it.

### AlbedoDemo (`albedo`)
A lit swatch. The free hand's height sets albedo directly (0..1); the swatch's own colour *is*
that number (dark absorbs, pale reflects — not a proxy, the literal definition), and a second
beam peels off carrying exactly that fraction of brightness back out. Genuinely computed, not
just reflected: the swatch also integrates a real temperature from the unreflected fraction,
`C dT/dt = (1-albedo)*340 - sigma*T^4` — the same equation Planet Guardian runs at planetary
scale, run here for one small body with a real Stefan-Boltzmann constant and a demo-scale heat
capacity (tau ≈ 3 s, chosen so the delay is felt within a pocket demo rather than proven only at
planetary scale). Honest: the physics. Approximate: 340 W/m^2 and the heat capacity are
illustrative round numbers, not a claim about any specific real body.

### EnergyBalanceDemo (`energy-balance`)
The atomic mechanism inside Planet Guardian, isolated to remove every other variable: one body,
one incoming flow (hand-controlled magnitude), one outgoing flow (computed, `sigma*T^4`, no
albedo or greenhouse term at all — deliberately, so the ONE relationship is legible on its own).
Colour reuses Planet Guardian's own law exactly, unchanged: the body's hue is the current
imbalance on the Spectral ramp, not temperature. Default flux (240 W/m^2) and resulting resting
temperature (255 K) are Earth's actual real airless blackbody numbers — the textbook answer to
"what would Earth's temperature be with no atmosphere," which is a genuine, correct, checkable
fact, not a rounded illustrative one. Honest: all of it, including the default numbers. This demo
does not simplify Planet Guardian's physics — it is the same physics with two of the three
controls (albedo, greenhouse) held fixed.

### EquilibriumDemo (`equilibrium`)
A ball on a real potential landscape, `U(x) = x^4 - 2x^2` — two valleys, one hilltop between
them, the identical double-well shape Planet Guardian's own climate sits on, shown here with
nothing about climate in it. The free hand picks the ball up and puts it down anywhere; on
release, real damped dynamics (`F = -dU/dx`, second-order, substepped) take over. This is the
standard textbook visualisation of a potential well — the terrain height IS potential energy, and
the ball's horizontal motion is the real equation of motion in that potential, not a rigid-body
rolling simulation (which would not be more honest, just more expensive, since "potential well" is
already the actual physics vocabulary for this). Verified against the exact ODE: released near
the hilltop it settles into the nearer valley every time; released essentially exactly on the
hilltop (tested at x=0.001 and x=-0.001) it goes to whichever side, confirming genuine sensitivity
rather than a scripted snap; released far outside the intended range it is still pulled back in by
the x^4 term rather than escaping, so an errant hand position cannot break it.

### TippingPointsDemo (`tipping-points`) — the one asked for the most care
Real model: `dx/dt = c + x - x^3`, the standard cusp/fold system (the same mathematical object
used, among other places, in actual climate tipping-element literature whenever "tipping point" is
meant technically). For `|c| < 2/(3*sqrt(3)) ≈ 0.385` two valleys and a hilltop exist; past that
threshold, on either side, one valley is mathematically gone. The free hand never touches the
ball — it drags a control bead that tilts the whole landscape (rebuilt every frame via
`Sheet`/`SheetApply`, the one mesh in any of these four demos that has to be), and the ball is
simply wherever the landscape leaves it. That indirection is deliberate, not a limitation: the
lesson is that the state is a *consequence*, not something you puppet, and making the ball
ungrabbable is what makes that legible.

This is deliberately a different mechanism from the constellation's existing `FeedbackDemo`, per
the brief — `FeedbackDemo` shows a single fixed point whose stability flips with a gain;
this shows two coexisting stable states where one can vanish out from under you, which
`FeedbackDemo`'s model cannot do at all. Related, not redundant.

The hysteresis itself was verified end to end before writing any C#: pushing `c` from 0 up to 0.6
(crossing the fold at +0.385, the ball jumps from x≈-1 to x≈+1.2) and then back down through 0
leaves the ball at x≈+1.0 — *not* back at -1 — because the branch it is now on does not fold until
c reaches -0.385, on the far side. Only pushing further, past that second threshold, brings it
home. That is the exact asymmetry the brief described, produced by the real equations, not staged.
Two numbers were tuned against that same verification rather than guessed: the jump/settle
haptic-and-colour threshold (0.5 units/s — the measured peak speed during a genuine jump is
~0.98 units/s at these constants, while the slow *approach* to a vanishing valley, real critical
slowing down near the fold, stays under 0.5, so the cue fires on the dramatic part only), and the
control range (±0.6, comfortably past ±0.385 on both sides so the fold is always reachable by
hand). Honest: the bifurcation, the threshold values, the asymmetry. Approximate: nothing
material — this is the textbook model at its textbook parameters, not a simplification of it.

### What I did not build
No pocket demo needed a custom shader — all four reuse existing `PrismMaterials`/`ConceptDemo`
geometry helpers, for the same compile-safety reason as Round 1. No text on any of the four,
matching every existing demo in `DemosMath.cs`/`DemosMotion.cs`/`DemosSpace.cs` — pocket demos in
this product appear to be textless entirely, not just text-deferred, so I followed that rather
than the world-level "labels at Formalize" convention.
