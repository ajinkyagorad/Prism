# Evolution Engine — build notes

"Change the world and watch life answer." A population of small organisms, varying visibly in
one continuous heritable trait (rendered as size and colour together — see Colour encoding
below), lives in a shallow terrarium. They wander, mature, reproduce and die on a real
birth-death process. The learner can never touch an organism. They can move four things in the
environment — a temperature control, a predator, a food source, and a token that speeds up
simulated time — and the population answers, over generations, never instantly.

## Files

| file | what it is |
|---|---|
| `EvolutionSim.cs` | The population. Plain C#, no MonoBehaviour — a fixed-capacity array of `Individual` structs, a free-list, and one `Step()` that is the entire biology. No rendering, no hands, no evidence. |
| `EvolutionOrganismField.cs` | Renders the whole living population as one dynamic mesh of camera-facing quads (à la `PrismCompanion`'s motes), vertex-coloured per organism. |
| `EvolutionHistogram.cs` | The trait histogram: `EvolutionSim.BinCount` small bars, coloured like the population, showing relative frequency. Hidden until Discover. |
| `EvolutionChallenge.cs` | Apply (hold a target), Explain (predict, then verify), Create (produce two stable forms) — mirrors `OrbitalChallenge`'s shape. |
| `EvolutionEngineWorld.cs` | The `PrismWorldBase` subclass: builds the terrarium and the four environment objects, reads hands, owns the loop gates, declares concepts and the shader. |
| `EvolutionEngineOrganism.shader` | `Prism/EvolutionEngineOrganism` — `Prism/Mote` with the uniform tint replaced by a per-vertex colour. |

## The one rule the whole design serves

Organisms have **no grab target anywhere in the code**. Every hand-proximity check in
`EvolutionEngineWorld.cs` (`HandleDial`, `HandlePredator`, `HandleResource`, `HandleTimeStone`)
tests against one of the four environment objects; none of them, and nothing in
`EvolutionChallenge.cs`, ever tests a hand against `Sim.Pop`. A learner physically cannot pick up
an organism, from Wonder onward — the constraint isn't taught, it's structural. The only things a
hand can ever change are `Sim.Temperature`, `Sim.PredatorActive` / `PredatorLocalPos`, and
`Sim.ResourceRichness01`.

## Simulation honesty

`EvolutionSim.MeanTrait` and the histogram bins are **read out of the living population every
step** (`RecomputeStats`); nothing ever assigns them and nothing ever touches a living
individual's `Trait` after birth. The distribution moves only because `Step()` decides, per
individual, whether it dies this step and whether it reproduces this step — real per-second rates
converted to real per-step probabilities via `1 - exp(-rate * dt)`, not a lerp toward a target.

What each environment control actually changes, and why the channels are split the way they are:

- **Temperature** sets `OptimalTraitAt(Temperature)` and multiplies **reproduction rate** via a
  Gaussian match between an individual's fixed trait and that optimum. It never touches mortality.
  This is deliberate: it keeps "fitness = expected offspring" literal rather than metaphorical —
  the FORMALIZE-stage definition is exactly the number this channel computes.
- **Predator** multiplies **mortality** only, and only for organisms within its hunt radius. Its
  target is not a fixed trait value — it's the *local mean trait among nearby living organisms*,
  recomputed every step, so it preys on whoever is currently common nearby (apostatic /
  search-image predation, a real phenomenon). This is what makes it a genuine disruptive-selection
  tool for Create: it suppresses the current mode, not one arbitrary size class.
- **Resource** sets carrying capacity `K` by the token's distance from the terrarium centre, which
  throttles reproduction (`crowd = 1 - N/K`) and adds a mild extra hazard above capacity. This is
  the feedback loop the `feedback` concept link is honest about: population size regulates itself
  through density-dependent reproduction and mortality, a real coupled loop, not a cap enforced by
  fiat.
- **Time token** only scales `EvolutionSim.TimeScale`, which scales how much real time is fed into
  the fixed-step accumulator. It cannot change an outcome, only how long the learner waits for one.

## Colour encoding (stated once, as the contract asks)

**Hue is trait.** Every organism's quad, and every histogram bar, is tinted by
`PrismPalette.Spectral(trait01)`. This is computed on the CPU in `EvolutionOrganismField.Rebuild`
and `EvolutionHistogram`'s constructor and simply carried through the shader — there is no second
colour law anywhere for organisms. Size is a second, *redundant* encoding of the same number: the
trait this world uses is literally body size, so drawing a large-trait organism larger is the
trait's own meaning, not decoration layered on top of it.

The four environment objects use fixed, non-spectral identity colours on purpose, so they are
never mistaken for population members: predator = coral (Seed), resource = mint/gold (Gel, a
living thing), time token = violet/cyan (Volumetric, an abstraction). The one deliberate exception
is the temperature bead, whose tint is set to `Spectral(OptimalTraitAt(Temperature))` — it
previews which trait value is *currently favoured*, in the same colour language as everything
else, so the dial's own colour is a non-verbal hint at the causal link without adding a second
meaning to learn.

## The loop

| stage | gate (`ConfigureLoop`) |
|---|---|
| Wonder → Explore | witnessed >= 2 births and >= 1 death (pure observation; nothing to grab yet but the environment) |
| Explore → Discover | `shift.warm` AND `shift.cold` both recorded — see below |
| Discover → Formalize | one more confirmed shift with the histogram already visible |
| Formalize → Apply | introduced the predator at least once |
| Apply → Explain | held the population inside a target trait band for 9s |
| Explain → Create | a correct prediction, verified against a real subsequent change |
| Create → Connect | two separated, sustained peaks in the living distribution for 7s |

**The Explore → Discover gate is the one that matters most**, mirroring the orbital world's
bound/unbound straddle. `TrackShiftEvidence` in `EvolutionEngineWorld.cs` only records
`shift.warm` after the learner has held the dial above 0.70 for a sustained 10 simulated seconds
*and* the population's own reported mean trait has moved down by a real threshold (0.09 of the
normalised trait range) from where it stood when the dwell began — same for `shift.cold` in the
other direction. It cannot be satisfied by wiggling the dial, and it cannot be satisfied by
passive drift alone (10s of sustained directional pressure moving the mean by 9% of its range is
not a plausible neutral fluctuation at these population sizes). Only once **both** flags are set —
which requires the learner to have caused the population to grow and shrink in trait, at different
times, by their own action — does the histogram appear.

## Concepts declared

`evolution-engine` (System, door concept) plus `variation`, `heritability`, `selection`, `fitness`
(all Fact/Process, domain `LifeAndEvolution`). Placed via a `Dir(azimuthDeg, elevationDeg)` helper
that builds an already-unit vector, so the assigned wedge (azimuth 216-252°, elevation within
±0.5, distance 3.2-5.5 m) is satisfied by construction rather than by hand-checked arithmetic.
Links: `evolution-engine` Composes `variation`/`selection`/`heritability` and Instantiates
`feedback`; `variation` is Prerequisite to `heritability`; `heritability` Constrains `selection`
(the Breeder's-equation point — heritability limits how much of a selection differential actually
carries into the next generation); `selection` Composes `probability` (differential
survival/reproduction is built from many individual probabilistic events — the predator's own
local-mean kernel is literally this); `fitness` Measures `selection`. I checked the other nine
worlds' declared concept ids before choosing these (none collide as of this writing) and grepped
`Assets/Prism/Editor/PrismConceptSeed.cs` to confirm `probability` and `feedback` exist as targets.

Language check: I read back every `Capability`/`Formalisation` string and the doc-comments in
`EvolutionSim.cs` specifically hunting for "wants to", "tries to", "learns to", "in order to", or
any framing that gives an individual organism agency. I did not find one. The recurring phrasing is
deliberately passive-population-level: "individuals... leave more offspring," "the population's
... distribution shifts," "become more common" — never "the organism adapts."

## What is approximated, and why

- **Reproduction is asexual/clonal** — one parent, offspring trait = parent trait + mutation
  noise. This is the single biggest simplification. It was chosen specifically so that Create's
  disruptive-selection task is *reliable*: under sexual reproduction with random mating, offspring
  of two different peaks would be intermediate, and a real population usually needs assortative
  mating on top of disruptive selection to hold a stable bimodal split. Clonal reproduction removes
  that confound at the cost of not modelling recombination at all. Worth flagging if a future pass
  wants to add real sexual reproduction — it would need mate-finding and, likely, some assortative
  bias for Create to stay achievable.
- **Predation and reproduction pressure are numeric constants, reasoned about, never measured.**
  `MaturityAge`, `BaseFecundity`, `ThermalSigma`, `MutationSigma`, the carrying-capacity range, the
  10-second dwell requirement, and every hold/settle duration in `EvolutionChallenge.cs` were
  chosen by working through the arithmetic of what should produce a visible shift in something
  like ten to twenty real seconds, not by running the simulation and watching it — I have no
  compiler and never saw a frame of this render. They are all plain public fields (not consts)
  specifically so they are easy to retune from the inspector without touching logic, because I
  expect at least some of them to need it.
- **Create's bimodality test is a heuristic peak-finder** over 14 histogram bins
  (`EvolutionChallenge.HasTwoSeparatedPeaks`): local maxima above a minimum share of the
  population, separated by a valley at most half the smaller peak's height. It reads real bin
  counts honestly; the thresholds themselves are a judgement call.
- **The resource's effect on carrying capacity is a fixed function of its distance from a static
  terrarium centre**, not of local population density. A richer, fully coupled model (food
  effectiveness depends on how many organisms are already drawing on it near where it sits) was
  the first design but was cut for legibility and lower bug risk without a compiler to check the
  coupled dynamics.
- **Fitness is never stored as a number anywhere.** This is deliberate, not a gap: fitness is
  exactly the birth rate and death rate the simulation already computes from trait and
  environment, and storing it as a separate stamped-on property would have invited exactly the
  "fitness as merit" framing the brief warns against.

## What the master needs to know

- **New concepts will read as inert without pocket demonstrations.** `Assets/Prism/Demos/` is
  off-limits to me (contract, rule 2), but `CLAUDE.md`'s own history ("Sixteen of the seventeen
  concepts did nothing... `PrismVerify` now fails if any concept is inert") means `variation`,
  `heritability`, `selection` and `fitness` need entries in `ConceptDemoRegistry` before a full
  build will pass verification, unless `evolution-engine`'s own in-world experience is judged
  sufficient for the door concept and the four supporting ones are accepted as`LatentDemo` for now.
- **The shader name is declared, not registered.** `Shaders => new[] { "Prism/EvolutionEngineOrganism" }`
  on `EvolutionEngineWorld`, matching `Shader "Prism/EvolutionEngineOrganism"` in the `.shader`
  file exactly (grepped to confirm). Per `PrismWorldBase`'s own contract this should be picked up
  automatically by whatever now unions `WorldRegistry.AllWorldShaders()` into Always Included
  Shaders — I did not edit `PrismConfigure.cs` (out of bounds) and could not verify that side is
  wired for the new per-world path yet.
- **No `.meta` files were created.** I have no Unity process to generate correct GUIDs with, and
  hand-writing them risked worse breakage than leaving them for Unity's next import to generate.
- **Reach envelope**: everything grabbable sits within about 0.75 m of the head (`Reach = 0.50`,
  objects at up to ~0.25 m local radius from the anchor), chosen by direct comparison against
  `OrbitalWorld`'s proven ~0.71 m cradle-ring distance. Never seen on a headset.
- **Nothing in this world has been run.** No Unity, no compiler, no playtest, per the hard rule.
  Every claim above about pacing, tolerances and reachability is reasoned, not observed. I
  cross-checked every symbol used across the five files against its declaration by grep (see the
  status message sent to the coordinator) as the only verification available to me.

## What is unfinished

Nothing in the eight-stage loop is stubbed — Apply, Explain and Create are all implemented against
the real simulation, not left as TODOs. The two things I'd flag as most likely to need a second
pass after a real playtest are the pacing constants above, and whether 14 histogram bins over a
population capped at 96 gives the Create gate enough resolution to detect bimodality reliably
without false positives from ordinary variance — I reasoned through both but could not watch either
happen.

## Round 2 — pocket demonstrations (`EvolutionDemos.cs`)

Added after the coordinator flagged that `variation`, `heritability`, `selection` and `fitness`
fell back to `LatentDemo` in the atrium. One new file, `EvolutionDemos.cs`, namespace
`Prism.Worlds.EvolutionEngine`, four classes each carrying `[ConceptDemoFor("...")]`, plus a small
shared `EvoDemoMath` (Box-Muller draw + Gaussian kernel — the same two functions `EvolutionSim`
uses, so the demos are a hand-held miniature of the same rules, not a separate simplified
retelling). I read `ConceptDemo.cs` and `ProbabilityDemo`/`FeedbackDemo` in `DemosMath.cs` in full
before writing these, specifically to match the established idiom (pre-built object pools, the
holding-hand-carries/free-hand-operates grammar, `Track()`-only-once material handling) rather than
inventing a parallel one.

**The "never touch an organism" constraint, re-derived at demo scale.** None of the four demos let
the free hand cause an individual's OWN trait to change by proximity or by touching it. I checked
this deliberately for each:

- **Variation** — the free hand pinches near an individual to log its value into a histogram bin.
  This reads a pre-existing value; it is the demo equivalent of reaching toward a concept to see
  its name, not of editing it. Nothing about the individual changes, and re-measuring the same one
  twice logs the same value both times — which is itself part of the point.
- **Heritability** — I specifically redesigned this away from an earlier draft where pinching near
  a parent would trigger that parent to reproduce on demand, because that is a hand DIRECTLY
  causing a specific organism to act, which is exactly the shape of interaction the world's own
  hard rule forbids. In the shipped version, both parents produce offspring automatically and
  continuously on a timer; the free hand never approaches a parent at all. It controls scatter
  (`sigma`) — an abstract property of the transmission process, the demo's equivalent of "the
  filter" — via hand height.
- **Selection** and **Fitness** — the free hand only ever sets an environment token's position
  (hand height -> a favoured trait value), read the same way the temperature dial is read in the
  full world. It never approaches an individual.

**What each demo does, and what is honest vs approximate:**

- **VariationDemo** — 14 individuals, trait fixed once at Build from a Gaussian, colour and size
  both `Spectral(trait)` exactly as in the main world. Fully honest: the histogram it fills is a
  real tally of real, fixed values: nothing is invented or resampled. The only approximation is
  scale — 14 individuals is enough to see a spread, not enough to see a smooth distribution, which
  is a legibility trade-off for a 17 cm object, not a misrepresentation.
- **HeritabilityDemo** — two fixed parents at well-separated trait values; offspring spawn on a
  0.5 s timer, alternating parent, trait = parent's trait + Gaussian noise at a sigma the free hand
  sets live. Honest: the offspring really are drawn from the parent's value with real scatter, and
  the sigma the hand sets is used at the moment of THAT birth (older offspring, born under a
  different sigma, are not retroactively altered — the pool is a rolling window of the last 22
  births, so the display always reflects recent settings without lying about history it can't
  show). Approximate in the same way the main world is: one parent, not two, so this is asexual
  inheritance, not a demonstration of blending or recombination.
- **SelectionDemo** — 12 individuals in a ring, replaced one at a time every 0.5 s by a
  two-draw weighted-random process (who is replaced favours a poor match to the current
  environment; who the replacement is born from favours a good match; both are real roulette-wheel
  draws over a real Gaussian match kernel, not a guaranteed pick of best/worst). Honest in the same
  sense `EvolutionSim` is: the colour drift is a consequence of many small probabilistic events,
  never a direct recolour of the population toward a target. Approximate: at N=12 replaced one at a
  time, this is a much coarser, faster-mixing process than the main world's birth-death population,
  chosen so a visible lean appears within a few seconds of holding a hand position rather than
  requiring the longer dwell the full world asks for.
- **FitnessDemo** — 9 individuals, evenly spread across the trait range and NEVER changed after
  Build. Each carries a bar whose height is `GaussianKernel(trait - optimum, sigma)`, recomputed
  every frame from the live hand-set environment. Honest and exact: the bar height IS the
  fecundity-match term `EvolutionSim.ThermalMatch` would compute for that trait at that
  temperature, not a stylised stand-in. This is the demo the coordinator specifically asked to get
  right on language, and it is built so the "fittest" label (the pulsing individual with the
  tallest bar) visibly moves from one end of the population to the other as the hand sweeps top to
  bottom, while no individual's own trait ever moves at all.

**Not done**: no sound distinguishes a demo-specific event beyond the existing `Voice.Resonance` /
`Voice.Settle` calls already used elsewhere in this world; I did not add a `Tension` cue anywhere in
the demos because nothing in any of the four is a "wrong answer" moment the way Explain's
prediction is. I have not seen any of these four render — same caveat as the rest of this file.
