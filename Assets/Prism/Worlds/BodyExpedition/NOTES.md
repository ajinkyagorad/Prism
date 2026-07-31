# Body Expedition — NOTES

Built by an independent agent under the World Module Contract. No Unity, no compiler, no headset
was available while writing this — the physics was validated by hand-porting the exact same
equations into a throwaway Python script and running them there before translating to C#. That
script is not part of the deliverable; the numbers it produced are recorded below so the master
(or anyone tuning this later) knows they are not guesses.

## What this world is

The circulatory system as a pressure-and-flow **control system**, not a labelled diagram. A learner
squeezes vessels, grips the heart, and tilts the body; a real lumped-parameter circulation model
(eight compliant nodes, ten resistive vessels, one pulsatile pump, one hydrostatic term, one
baroreflex) computes what happens everywhere else, and the learner discovers non-locality,
Poiseuille's fourth power, and homeostasis by playing before any of those words appear.

Files:
- `CirculationSim.cs` — the physics. Plain C#, no MonoBehaviour, mirrors OrbitalSim's shape.
- `BodyExpeditionWorld.cs` — the `PrismWorldBase` subclass: geometry, interaction, the loop.
- `CirculationChallenge.cs` — Apply / Explain / Create, mirrors `OrbitalChallenge`.
- No new shaders. Everything is built from `Prism/Flow`, `Prism/Ceramic` and `Prism/Gel`, all
  already in the shader family — declared again in `Shaders` per the contract, harmlessly redundant
  with what OrbitalWorld already needs.

## The topology

```
Aorta -> Upper -> Arm  -\
               -> Head -+-> Venous -> (heart, prescribed flow) -> Aorta
      -> Lower -> LegL -+
               -> LegR -/
```

Eight pressure nodes (`Aorta, Upper, Lower, Arm, Head, LegL, LegR, Venous`), ten resistive vessels,
all ten learner-adjustable by a two-hand squeeze. The heart is deliberately **not** a pressure node:
it is a prescribed flow source that moves one stroke volume from the venous reservoir into the
aorta each beat. See "What is approximate" for why.

## Colour and encoding — stated once, as required

- **Vessel hue = flow speed** (m/s), cyan (slow) through violet (fast) — exactly OrbitalWorld's
  trail-by-speed convention, so a learner who has been in the orbital world already knows how to
  read it.
- **Pressure is a separate, orthogonal channel**: the size and brightness of the bead at every
  junction, revealed at Discover. Never hue, so the two fields can never be misread as the same
  thing.
- **Resistance is the vessel's own rendered width.** Not a derived visualisation — literally the
  same radius the learner is squeezing, scaled once by a uniform display factor for graspability
  (`DisplayRadiusBoost = 1.8`, in `BodyExpeditionWorld.cs`).
- Vessels brighten together (`_Pulse`) during systole — a genuine readout of the simulated cardiac
  phase, not decoration.

## What is honestly simulated

- **Poiseuille's law exactly**: R = viscosityK · L / r⁴, applied to all ten vessels every fixed
  step. Verified in the offline prototype: halving a peripheral vessel's radius cut its flow to
  about 6–8% of its previous value (ideal 1/16 = 6.25%); a quarter radius cut it to about 0.5%
  (ideal 1/256 ≈ 0.39%). The deviation from the ideal ratio is itself honest, not error — a
  vessel's own resistance is only *part* of its series path, so the visible effect on flow is
  slightly diluted from the pure r⁴ prediction. See "peripheral vessels" below for why this still
  reads as dramatic.
- **Real conservation of flow (Kirchhoff's current law)** at every node, every fixed step, solved
  **implicitly** (backward Euler via Gauss-Seidel relaxation, 14 iterations/step), not with
  explicit Euler. This was not a style choice: a learner is explicitly invited to crank a vessel
  wide open, which can make its local RC time constant far shorter than the fixed step, and
  explicit Euler measurably went to NaN under that in the prototype. The network's system matrix is
  always strictly diagonally dominant (proof sketch is in `CirculationSim`'s header comment), which
  makes Gauss-Seidel an unconditionally stable contraction regardless of what the learner does to a
  radius — verified against dilation to 5× and near-occlusion at 0.03× base radius: bounded, no
  NaN, in both directions.
- **Real hydrostatic physics for the tilt control.** rho·g·h with real blood density (1060 kg/m^3)
  and **real anatomical height differences** (head +0.35 m, legs −0.90 m relative to the heart —
  actual human proportions, independent of the small graspable render scale, exactly the trick
  `PrismScale` plays keeping `Mu` real while `DisplayPlanetRadius` is small). Standing the model
  fully upright produced a leg-vs-head pressure swing of about 13 kPa in the prototype — matching
  real orthostatic physiology (ankle pressure roughly double heart-level pressure when standing)
  almost exactly, without that number ever being tuned toward it.
  Flow is driven by the difference in **total hydraulic head** (pressure + rho·g·h), not raw
  pressure — `CirculationSim.TotalHead()` — which is what lets tilting move blood honestly with the
  heart doing nothing different at all.
- **Real cardiac output relationship.** The heart's ejection pulse is an exact half-sine timed to
  the systolic fraction of the beat, constructed so its time-integral over one beat always equals
  the current stroke volume regardless of heart rate — so CO = HR × SV falls out, it is not asserted.
  Stroke volume itself responds to venous filling pressure (a simplified Frank-Starling
  relationship), so a change in venous return genuinely changes cardiac output.
- **The baroreflex is a real, always-on proportional controller** (`CirculationSim.UpdateReflex`),
  not a scripted response. It watches a 4-second low-pass of aortic pressure and leans on
  peripheral resistance and heart rate, bounded to ±35% authority. In the prototype, narrowing all
  four peripheral vessels to 0.65× produced roughly a third of the open-loop pressure rise with the
  reflex on versus off (+8.1 kPa closed-loop against +22.9 kPa open-loop, from the same 11.7 kPa
  baseline) — visibly pushing back, never hiding the lesson, because a learner's own r⁴ manipulation
  (16× resistance for a mere halving of radius) still dwarfs ±35% authority. This is the
  "homeostasis"/"feedback" concept, literally running.
- **Windkessel behaviour is a real emergent result, not scripted**: because the aorta has
  compliance, a pulsatile ejection produces continuous forward flow with a non-zero diastolic
  pressure floor, rather than flow stopping between beats. Nobody told it to do that.

## What is approximated, and why — read this before trusting a specific number

- **The heart has no valves and no ventricular chamber.** It is a prescribed flow source (move one
  stroke volume from venous to aortic compliance each beat, shaped as a half-sine gated to the
  systolic fraction) rather than a full time-varying-elastance pressure model. This sidesteps valve
  logic entirely (no risk of a backflow-detection bug) at the cost of not simulating *why* the
  valves make ejection one-directional — a defensible simplification for a lesson about the
  vascular network, not about cardiac chamber mechanics, but stated plainly so nobody mistakes the
  heart's own waveform for something derived rather than asserted.
- **No pulse-wave transit time or reflection.** The model is quasi-static within a beat: pressure
  changes are treated as propagating instantly along a vessel. At this network's physical scale
  (tens of centimetres) that is a reasonable approximation — a real pulse wave (~5 m/s) crosses that
  distance in ~50 ms, much faster than the ~300 ms systolic ejection it would need to keep up with —
  but it is explicitly why this model does **not** show the real phenomenon of pulse pressure
  amplifying toward the periphery in a full-scale adult vasculature. Said directly: this would be
  the wrong model at 2-metre anatomical scale; it is a reasonable one at table-top scale.
- **Vessel radii and lengths are a chosen "physics scale," not measured from the rendered
  geometry.** `CirculationSim`'s `BaseRadius`/`Length` per vessel were chosen (and the effective
  viscosity constant `ViscosityK` solved for) so the network's resting total peripheral resistance
  matches the real clinical value (~0.16 kPa·s/mL) — the same "keep the real target, invent a
  constant to reach it at this scale" move `PrismScale.Mu` makes for orbital mechanics. The
  *rendered* vessel length is separately just the distance between the hand-placed node positions
  in `BodyExpeditionWorld.NodeLocalPos`, chosen for reach and legibility. The two are not claimed to
  be the same number, and the display radius carries a further uniform ×1.8 boost for graspability.
  What is preserved exactly is the *relationship* — r⁴, not the absolute millimetres.
- **"Peripheral" vessels represent arterioles, not literal capillaries — a deliberate choice, made
  after it went wrong the other way.** The first version of this model made the four small
  leaf-adjacent vessels a *fixed*, non-adjustable "capillary resistance," and let the learner
  squeeze the bigger named-artery segments instead. Numerically, that made the r⁴ law nearly
  invisible: those proximal vessels are not the dominant resistance in their own series path, so
  halving one only changed total flow by a few percent — technically correct, pedagogically inert.
  Real physiology's own answer is that arterioles, not the big arteries, are where resistance and
  its regulation actually live — so the fix was to make the small peripheral vessels themselves
  learner-adjustable (all ten vessels are, in fact) and let their own dominance in their local path
  do the work. This is *more* physiologically honest than the first attempt, not less, and it is
  why squeezing a thin vessel near a tissue bed feels different from squeezing the aorta trunk —
  that asymmetry is real, not a bug.
- **Mean arterial pressure and cardiac output land close to real values (~12 kPa vs real ~13.3 kPa;
  ~4–6 L/min vs real ~5 L/min) but were not forced there.** They are a consequence of the K solve
  above, not independently tuned, and are close mostly because the K solve target (0.16 kPa·s/mL)
  was itself the real clinical total peripheral resistance.
- **The venous system is one lumped reservoir with one "average" anatomical height (−0.30 m)**, not
  height-tiered venous compartments. Real venous pooling in the legs on standing is a genuinely
  important part of orthostatic physiology that this simplification does not capture — the tilt
  effect you *do* see is entirely on the arterial/tissue side of the network.
- **No literal wonder-stage "you are floating inside one vessel" camera moment.** The whole tree is
  visible from Wonder onward — the aorta is simply the largest, nearest object, so attention
  naturally starts there — rather than a progressively-revealed close-up. This was a deliberate
  scope decision: staging a camera-relative reveal correctly, with no compiler to catch a mistake
  in it, felt like a bad trade against a simpler layout that is unambiguously correct.

## Interaction grammar

- **Squeeze any vessel with both hands** (pinch, both hands, near the same vessel's centreline —
  closest-point-on-segment, not surface contact, so thin vessels are exactly as easy to catch as
  the aorta) and move hands together or apart to narrow or widen it. Relative to the hand distance
  at grab-start, so it behaves like a two-hand pinch-resize gesture.
- **Grip the heart** to speed it up, proportional to grip strength; release and it eases back to
  resting rate. A haptic pulse fires on every simulated beat while held, so the pulse is felt, not
  just seen.
- **Drag the small bead** on the short track beside the tree, up to stand the body up and down to
  lay it flat; a thin indicator rotates to show current posture. Kept as a physically separate
  object from the vessel tree on purpose, so this gesture is never ambiguous with squeezing a
  vessel.

All three gestures are available from Wonder onward — nothing is locked behind a stage — because
stage advancement in this product is a *consequence* of what evidence exists, never a gatekeeper on
what the hands can reach.

## Evidence and gates

Named in `BodyExpeditionEvidence`. The one that matters most, straight from the brief: Explore does
not release into Discover until the learner has produced **both** a pressure rise (>15% above the
model's own resting mean, filtered over 4 s so a single systolic peak cannot trigger it by accident)
**and** a pressure collapse (<25% below rest). In the prototype, a single moderately-squeezed
peripheral vessel (0.5–0.6× radius) reliably produced +17–19%, and a single moderately-widened one
(1.8–2.0×) reliably produced −52–60% — both comfortably inside the interactive range
(`CirculationSim.MinRadiusMult`/`MaxRadiusMult` = 0.3–2.6), so the gate is reachable by playing with
one vessel, without needing to discover the more dramatic all-four-at-once cases.

## Apply / Explain / Create

- **Apply**: one of the four peripheral vessels is silently set pathological (0.30× radius); the
  learner restores mean pressure to within 1 kPa of the resting reference and holds it 4 seconds.
  Checked against the outcome (pressure in band), not against which vessel was touched — verified
  in the prototype that fixing the *one* affected vessel is sufficient and that the reflex alone,
  already fighting the pathology throughout, cannot fully correct it without the learner's help.
- **Explain**: one of the four single-leaf arteries (Upper→Arm, Upper→Head, Lower→LegL,
  Lower→LegR — chosen because each has an unambiguous single downstream leaf) is picked; the
  learner places a marker in space, then it narrows to 0.35× and the network is given 3 seconds to
  settle before the marker's world position is checked against the affected leaf's position (5.5 cm
  tolerance). Wrong guesses are recorded as a misconception, not a failure, and the challenge
  re-rolls — same shape as `OrbitalChallenge`'s prediction stage.
- **Create**: the four peripheral vessels are set to a deliberately uneven starting state
  (0.78/1.05/1.20/0.85×); success is all four flows within 14% of their mean, held 5 seconds. This
  reuses the exact same squeeze gesture as Explore rather than introducing a separate construction
  sandbox — a scope decision made for robustness with no compiler to verify a more elaborate
  branch-growing mechanic, and arguably more honest anyway: balancing resistance across a
  delivery network *is* what building one means, physiologically and in any engineered pipe network.

## Concepts declared

`body-expedition` (door, System) plus `pressure-and-flow` (Theory), `resistance` (Equation),
`homeostasis` (Theory), `cardiac-cycle` (Process). Cross-domain links, both genuine shape-matches
per the contract's own examples:

- `homeostasis` —Analogy(0.85)→ `feedback`: the baroreflex really is a sensed-error-driven
  correction loop, the same shape as any other feedback system.
- `cardiac-cycle` —Analogy(0.85)→ `periodic-motion`: systole/diastole really is a periodic process
  with a rate, the same shape as an orbit or a pendulum.

Placed at azimuth 40–70°, distance 3.6–5.2 m, elevation (direction.y before normalising) −0.10 to
0.20 — inside the assigned wedge (36–72°, 3.2–5.5 m, ±0.5).

## What the master must wire

1. **`Assets/Prism/Demos/ConceptDemoRegistry.cs`** needs entries for the four non-door concepts or
   `PrismVerify`'s demonstration-coverage check will flag them (they will still show the generic
   `LatentDemo` at runtime, not nothing, but not bespoke either). Quick suggestions, all cheap:
   - `pressure-and-flow`: a small U-tube/manometer the free hand tilts or squeezes, liquid level
     answering on both sides at once.
   - `resistance`: a single short tube the free hand pinches, with a speed-coloured current that
     visibly collapses on a small squeeze — the r⁴ law in miniature.
   - `homeostasis`: a small weighted arm that the free hand displaces and watches spring back
     partway, proportional to the push — a tiny proportional controller.
   - `cardiac-cycle`: a small pulsing bead with a two-phase (fast/slow) rhythm the free hand can
     speed up by touching it, echoing the heart-grip gesture from inside the world itself.
2. No new shaders to register — everything renders through `Prism/Flow`, `Prism/Ceramic`,
   `Prism/Gel`, already part of the shared family.
3. Checked late in this build: `PrismSession` already carries a general `List<PrismWorldBase>
   Worlds`, populated by `PrismSceneBuilder.BuildDiscoveredWorlds()` and routed by matching
   `WorldId` in `OnWorldEntered` — so `BodyExpeditionWorld` should be discovered, wired, seeded and
   **enterable** with no further routing changes. Worth a final confirmation once every world module
   has landed, since this file was still being generalised while this module was being written.

## What is unfinished / would benefit from a real headset pass

- All interaction thresholds (pinch/grip cutoffs, grab radii, evidence counts, the exact healthy
  band and balance tolerance) were chosen from the physics prototype's numbers and from the scale
  of the reference world, not from anyone's hand in a headset. They are very likely in the right
  range but are the first thing to retune if something feels too twitchy or too forgiving.
- The two Formalize-stage "bars" (pressure gap, flow) are oriented with
  `Quaternion.Euler(-90, 0, 0)` to stand a vessel-shaped unit cylinder upright. This is a reasoned
  derivation, not a tested one — if they turn out to grow sideways or downward instead of upward,
  the fix is flipping that sign, nothing structural.
- Nothing plays with more than one hand pointing/reach-cone fallback the way `OrbitalWorld`'s moon
  grab does (proximity fallback to a reach cone for a learner seated slightly out of range). Given
  the tree's modest spatial footprint (roughly 0.2 m × 0.35 m around the anchor) this seemed like
  lower risk to omit than to add untested reach-cone math on top of the two-hand gesture, but a
  learner with unusually short reach may find the topmost (Head) or bottommost (LegL/LegR) vessels
  a stretch.
