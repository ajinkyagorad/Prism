# Quantum Garden — NOTES

Folder: `Assets/Prism/Worlds/QuantumGarden/`  Namespace: `Prism.Worlds.QuantumGarden`
World id: `quantum-garden`  Primary concept: `quantum-garden`

## What this is

A two-slit apparatus. A source emits particles one at a time; each lands at a single, definite
point on a screen; wait long enough and the dots build a pattern the learner did not predict. The
whole design bet is that quantum mechanics is a wave theory with a measurement rule, not a bag of
paradoxes — so the module builds the wave (a real complex amplitude, summed over real paths, with
real path-length phase) and the rule (the Born rule, |amplitude|^2), and never asserts an
interpretation of what happens between emission and detection. The word "particle" is used loosely
in this document for the thing that is emitted and detected; the simulation itself never claims to
know what it does in between, and neither does any label in the world.

## Files

| File | What it is |
|---|---|
| `TwoSlitSim.cs` | The physics. No MonoBehaviour — a plain class the world and the shader both read from. |
| `QuantumMesh.cs` | Two procedural primitives PrismMesh doesn't have (a cube, a metre-sized rect), wound and verified by hand against `PrismMesh.Disc`'s known-good triangulation. |
| `QuantumScreen.cs` | The detector screen: backing plate, live field overlay, ring-buffered detection dots. |
| `QuantumField.shader` | `Prism/QuantumField` — the live amplitude field, computed per-pixel by the same sum `TwoSlitSim` uses to sample a detection. |
| `QuantumGardenWorld.cs` | The `PrismWorldBase` subclass: geometry, interaction, the eight-stage loop, the constellation concepts. |
| `QuantumDemos.cs` | Three atrium pocket demos (round 2 — see below): `SuperpositionDemo`, `MeasurementDemo`, `QuantisationDemo`. |

## The physics, and exactly how honest it is

`TwoSlitSim.Amplitude(x)` sums a **discretised Fresnel-Kirchhoff integral**: each open slit is
`SubSourcesPerSlit` (9) evenly spaced coherent line elements across its physical width, not a
single point. That discretisation is what makes the single-slit pattern *real diffraction* rather
than a flat nothing — a zero-width slit cannot diffract, so a slit has to have width to honestly
produce the "broad hump, no fine fringes" result the Explore→Discover gate asks the learner to
grow. Every path length (source→sub-source, sub-source→screen point) is the exact 3D-reduced-to-2D
distance, never a paraxial or far-field shortcut, so:

- Fringe spacing is **computed**, not art-directed: `Wavelength * (ScreenZ - SlitZ) / Separation`
  falls out of the sum rather than standing in for it (`PredictedFringeSpacing()` exists only to
  lay out the Apply-stage target ghost and grade that one challenge — the pattern the learner
  actually sees always comes from the full sum, never from that formula).
- Dragging one slit off-centre while the source stays fixed on-axis genuinely shifts the whole
  fringe pattern sideways, for free, because the source-to-sub-source leg is computed per sub-source
  rather than assumed symmetric.
- `MaxIntensity()` rescans the pattern (96 samples) only when something dirties it (a slit moved, a
  slit opened/closed, the wavelength changed), and `SampleDetectionX()` is honest rejection
  sampling against that peak — not inverse-CDF against a precomputed table, and not a Gaussian
  standing in for the real shape.

**Slits are modelled as infinite lines (cylindrical wave sources), not points** — amplitude falls
as `1/sqrt(r)`, intensity as `1/r`. This is the *physically correct* treatment for a real slit (a
gap much taller than it is wide), not a simplification: it is also why the whole problem reduces
honestly to 2D (x across the slits, z along the beam) and why detected dots are scattered uniformly
in y — the physics genuinely has no y-dependence to draw from, so faking one would be the dishonest
choice.

`Prism/QuantumField` runs the *same* sum per pixel (`TwoSlitSim.FillActiveSources` pushes the
identical sub-source list — position, source distance, weight — that `Amplitude()` uses on the
CPU), so the picture on the screen and the distribution detections are drawn from cannot drift
apart; they are one formula evaluated twice, not a look-alike shader standing in for the real
thing.

### What is a stated approximation, and why

- **Slit width is fixed** (1.2 cm, `TwoSlitSim.SlitWidth`), not learner-adjustable — only
  separation and wavelength are, matching the brief's suggested spine. I chose one apparatus
  constant rather than exposing a third knob; because slit width is always well under the minimum
  slit separation (3 cm), the qualitative single-vs-double contrast (fine fringes vs. one broad
  hump) holds for every reachable configuration by construction, which is the property that
  actually matters for the Explore/Discover gates.
- **The source is fixed on-axis.** Only the two slits move. A movable source would be a nice
  future addition (it would let the learner explore coherence/source-size effects) but wasn't
  needed for the spine and would have doubled the interaction surface for a secondary payoff.
- **Detection arrival rate (particles/second) is a chosen constant pace (5/s), not derived from
  total flux.** Real physics would make the two-slit rate roughly double the one-slit rate (a
  second open aperture really does admit more energy). I deliberately decoupled rate from
  configuration so both demonstrations are equally paced to learn from — waiting twice as long to
  see an equally clear one-slit pattern would be a pedagogical cost for a realism gain nobody would
  notice. What is never faked is the **shape** every detection is drawn from.
  This is exactly the PrismScale precedent (orbital mechanics keeps Newton exact and chooses one
  constant, mu, rather than faking gravity): the rule stays real, one pacing constant is chosen
  deliberately and said so here.
- **The field overlay's brightness is NOT renormalised between one- and two-slit configurations.**
  It reuses the same `MaxIntensity()` the CPU sampler computes, so a one-slit pattern is honestly
  dimmer at its peak than a two-slit one (real light — real anything — gets through a second open
  aperture). I flag this because it was a real design choice, not an oversight: I considered
  self-normalising per-configuration for legibility and decided the honest relative brightness was
  worth keeping, given it's still clearly legible either way.
- **The particle-in-flight animation deliberately shows nothing between the source and the
  barrier's own optical axis.** The mote travels in a straight line from the source to local
  (0,0,SlitZ) — never toward either individual slit, never toward a "midpoint between the slits"
  (which would differ from the axis once slits are dragged asymmetrically and would itself be a
  which-path hint) — then vanishes. After a short unshown interval, a detection simply appears on
  the screen. Nothing in the animation claims the particle went through one slit, both slits, or
  any particular path; only the two empirically real endpoints (emission, detection) are drawn.
  This is the most conservative honest choice I could find for the interpretation question the
  brief is explicit about.
- **No per-detection sound.** PrismVoice's palette is all soft, slow-attack tones meant to mark
  significant moments; at 5 clicks/second that would read as an alert loop, which the contract
  explicitly says nothing in PRISM should do. Sound is reserved for stage transitions and the
  Apply/Explain payoffs.

## Colour encoding (stated once, as the contract asks)

**Phase of the complex amplitude → the spectral ramp; |amplitude|^2 → brightness/height.** This is
the one meaningful encoding and it appears everywhere a complex amplitude is drawn (the world's
field overlay, and both `SuperpositionDemo` and the field itself): two contributions arriving out
of phase sit on opposite sides of the ramp, and *exactly there* the intensity also goes to zero —
so the learner can see that a dark fringe is a meeting of opposite phase, not merely "no light",
which is the whole reason `Prism_Spectral` domain-colouring was worth doing instead of a plain
brightness gradient.

Separately, and explicitly NOT the same encoding: the wavelength gauge and the world's slit
markers use `PrismPalette.Spectral(t)` as generic **parameter feedback** (this value is currently
near this end of its range), the same convention `OrbitalWorld` already uses for its velocity-ratio
arrow. That's a UI convention this product already has, not a second physics encoding — I call this
out so the two uses are never confused for one another.

Detection dots are a plain warm/gold mark, deliberately **not** phase-coloured: a single classical
click has no phase to display. Phase belongs to the field, not to an outcome.

## Interaction

- **Slits**: pinch near a slit's marker bead and drag it along the transverse axis (clamped,
  minimum separation enforced) to change separation — and, if dragged asymmetrically, to shift the
  whole pattern sideways, honestly. An open (not pinching) hand held near a slit's gap covers it;
  the marker dims and the physics genuinely excludes that slit's sub-sources from the sum.
- **Wavelength**: pinch a small bead near the source with each hand simultaneously and stretch —
  hand separation maps to wavelength. The gauge always shows the current value at rest (a real
  physical stand-in for a slider, not a proxy for one).
- **Apply**: a faint translucent comb of tick marks shows a randomly chosen target fringe spacing;
  tune separation/wavelength until the live pattern's computed spacing matches within tolerance for
  ~1.2 s.
- **Explain**: point (reach-ray, not touch — the screen sits ~0.5 m past the slits) and pinch to
  drag a marker across the screen plane; release to commit a prediction of the densest region, then
  a fast burst of 100 honestly-sampled detections runs and the marker is checked against where the
  pattern actually peaked. Wrong answers are recorded as a misconception
  (`Knowledge.FlagMisconception`) and can be retried, per the contract's "informative, not a
  failure" instruction.

## The loop (evidence, not buttons)

The Explore→Discover gate is the one the brief calls out specifically: it requires
`PatternInterference` (≥55 dots accumulated continuously with both slits open) **and**
`PatternSingle` (≥45 dots with exactly one slit open) **and** at least two real open/close toggles
— the personal straddle. Discover→Formalize goes a step further and asks the learner to reproduce
*both* patterns again with the amplitude field now visible (`PatternInterferencePostField` /
`PatternSinglePostField`), so the dot-by-dot buildup and the continuous field are connected by
something the learner did, not by a timer. Full gate list and rationale are in the doc comments on
`ConfigureLoop`.

One thing worth flagging explicitly: `LearningLoop.StageEntered` only fires on a transition the
loop performs itself — it never fires for the stage a *resumed* session simply starts at (a
returning learner whose save file already has `stage = Apply` starts there silently). Without
handling this, that learner would find a target-less Apply challenge with no way to satisfy its
gate. `QuantumGardenWorld` handles it with a small per-frame catch-up check
(`CatchUpStage`/`ApplyStageSetup`) that performs the same one-shot stage setup either way. This
looked like a real gap in the pattern `OrbitalWorld` established (its own per-frame `Loop.Stage`
polling for *continuous* state like the field reveal sidesteps the issue, but a genuinely one-shot
action like picking an Apply target would have the identical problem there) — flagging it in case
it's useful elsewhere.

## Constellation placement

Wedge: azimuth 72–108°, distance 3.2–5.5 m, elevation ±0.5, as assigned. I read "azimuth" as
degrees measured **clockwise from forward (+Z), sweeping toward +X (right)**, and "elevation" as
the raw y-component of the direction vector *before* normalisation (matching how the existing
`PrismConceptSeed` data is authored — e.g. gravity's `(-0.55, 0.10, 0.85)` — rather than as a
separate angle). `QuantumGardenWorld.WedgeDir(azimuthDegrees, elevation)` implements exactly this.
If the real convention differs, the four `Direction = WedgeDir(...)` calls in `Concepts` are the
only things that need adjusting — everything else (distance, links) is independent of that choice.

Four concepts, as suggested: `quantum-garden` (door, System, MatterAndEnergy) composing
`superposition` (Uncertainty — "has extent instead of position" is a precise description of a
superposed state), `measurement` (Equation — the Born rule really is "a rule the space obeys," not
a formula on paper), and `quantisation` (Fact — in this exhibit specifically, the one settled,
checkable fact is that every detection is a whole, indivisible click). Cross-domain links, as the
contract asks: `superposition —Analogy→ waves` (the amplitude genuinely is a wave, same shape,
different material — the product's own thesis) and `measurement —Instantiates→ probability` (the
Born rule is where probability first becomes physical). `measurement —Measures→ superposition` uses
the `Measures` relation in its precise stated sense: measurement is literally how you find out
about the superposition.

## For the master

- **`WorldRegistry`/`ConceptDemoRegistry` discovery confirmed working** — no shared file was
  touched; `QuantumGardenWorld` and the three demos below are found by reflection alone.
- `PrismSession.cs` (read, not touched — `Assets/Prism/Core/**` is off-limits) still special-cases
  `worldId != "orbital"` as its own dead end as of when I last read it. Given `WorldRegistry`
  already exists and the round-1 status check confirmed all ten worlds landed and compiled, this is
  presumably already superseded — noting only in case it isn't.
- Nothing outside `Assets/Prism/Worlds/QuantumGarden/` was written to.

## Unfinished / possible future work

- No secondary diffraction lobes are guaranteed visible on-screen at every wavelength (the single
  slit's own envelope can be broader than the screen at long wavelengths) — physically correct
  given the chosen dimensions, just not always the most visually dramatic case. Noted in
  `TwoSlitSim`'s doc comment; not a shortfall in the sampled physics, only in how often the prettiest
  version of it is on screen.
- A movable source (see above) would add a genuine third degree of freedom; deliberately deferred.

---

## Round 2 — pocket demonstrations (`QuantumDemos.cs`)

Added after the world shipped: bespoke atrium demos for the three supporting concepts, which were
falling back to `LatentDemo`. Registered via `[ConceptDemoFor("...")]`
(`Prism.Demos.ConceptDemoForAttribute`), discovered by `ConceptDemoRegistry`'s reflection scan — no
shared file touched. All three follow the grammar exactly: the holding hand carries the phenomenon,
the free hand operates it. All geometry uses `ConceptDemo`'s existing helpers
(`Ball`/`Body`/`Curve`/`FlatMaterial`); no new shaders, no additive passes.

**`SuperpositionDemo`** — two coherent point sources and a row of 13 pillars reading out their
combined amplitude along a line beneath them. Pillar height is `|amplitude|^2`; pillar colour is
the amplitude's *phase* on the spectral ramp — the same dual encoding the world itself uses, so a
learner who has stood in Quantum Garden recognises it immediately. The free hand drags the second
source around in real time and the whole pattern reshapes live. Nothing here ever resolves to one
outcome — that is deliberately left to Measurement, so the two demos stay conceptually distinct
rather than repeating each other.

**`MeasurementDemo`** — the same two-source interference (`DemoWave.AddSource`, shared by both
demos so the formula can't drift between them), but the apparatus is fixed and the pattern is never
shown directly. Each pinch performs one honest rejection-sampled draw from `|amplitude|^2` — the
free hand's *position* is not consulted for the outcome, only its *pinch* triggers a draw, which is
deliberate: this demo is about an outcome the learner does not get to choose, unlike Superposition
where the hand's position IS the physics. A marker pops to the sampled point and a histogram
accumulates below; over many pinches the histogram is the only route by which the hidden
distribution ever becomes visible, which is the actually honest situation — you get outcomes, and
their statistics are the wave function.

**`QuantisationDemo`** — a string fixed at both (visible) ends. A fixed-fixed string has no stable
half-integer standing wave; only an integer number of half-wavelengths can actually stand. The free
hand's height sets a *continuous* candidate wavenumber, but the drawn shape always uses the nearest
*integer* mode — what genuinely varies continuously is whether the string can hold full amplitude
and sit still. Near an allowed mode it stands tall and settles (a consonance marks the catch, edge
detected so it fires once per arrival, matching `SymmetryDemo`'s own snap pattern); exactly between
two modes its amplitude collapses toward a minimum and it visibly shivers (a small per-vertex jitter
term, amplitude tied to how far from any allowed mode the hand is asking for). Trying to hold it
between two states is the demonstration — you feel it refuse, every time, rather than being told it
would. This is the same boundary-condition mechanism that quantises the energy levels of a bound
particle, made tactile rather than asserted; no claim beyond that mechanism is made.

### Honest vs approximate, round 2

- All three reuse `TwoSlitSim`'s spirit (real complex amplitude, real phase, real rejection
  sampling) but **not its code** — they define their own small two-point-source sum (`DemoWave`,
  spherical `1/r` amplitude, not the world's cylindrical `1/sqrt(r)` slit treatment) sized for a
  17 cm object with its own constants. Sharing `TwoSlitSim` directly would have tied a hand-sized
  demo to the world's much larger apparatus scale; a small self-contained formula was the more
  honest fit for "point sources," which these are (not slits).
- `SuperpositionDemo`/`MeasurementDemo`'s intensity is self-normalised to the pattern's own current
  peak each frame (unlike the world, where cross-configuration brightness is preserved
  deliberately) — there is no second configuration to compare against inside either demo, so there
  was no honesty trade-off to make either way.
- `QuantisationDemo`'s curve is rebuilt every frame via the shared `Curve()`/`PrismMesh.Tube` helper,
  which allocates internally (several `List<>`s per call) — this is inherent to that shared,
  off-limits-to-edit helper and is exactly the cost `ConicDemo` and `FeedbackDemo` already accept
  for the same reason (a shape that must visibly change every frame). No new allocation pattern was
  introduced; `OnTick` itself allocates nothing (the point buffer is a single reused, pre-sized
  `List` cleared and refilled in place, never reallocated).
- Nothing in round 2 required a new shader or touched any file outside this folder.
