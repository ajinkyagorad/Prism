# Algorithm City — build notes

Walk through a running computation and redirect it with your hands. Spine: **sorting**, two
structures side by side, always fed the same shuffled batch at once.

- **Insertion** — a flat row. A new element walks backward through what is already settled,
  swapping one adjacent pair at a time.
- **Merge** — a rising cascade, bottom-up (no recursion in the *implementation*; see honesty notes
  below). Runs of size 1 merge into runs of 2, then 4, then 8; a finished window rises one row.

Chosen over a BFS/maze spine because sorting is where "the same data, a different structure, a
very different cost" is easiest to make the learner personally produce twice, back to back, which
is what the Discover gate needs.

## Files

| File | What it is |
|---|---|
| `SortingSim.cs` | The honest simulation. `ISortProcess`, `InsertionSortProcess`, `MergeSortProcess`. Plain C#, no MonoBehaviour. `Step()` performs exactly one comparison (or the drain/publish bookkeeping either side of one) and returns a `SortStep` describing what happened. |
| `BeadField.cs` | Batches up to `MaxN` data beads into one dynamic mesh (one draw call) — camera isn't needed since beads are solid icosphere copies, not billboards. |
| `SortVisuals.cs` | Turns a `SortStep` into bead motion. `ApplyFlatStep` (Insertion, and both halves of the Create-stage bench) vs `ApplyCascadeStep` (Merge, the only one where a step also changes a bead's row). |
| `SortStructure.cs` | Bundles one running sort: the process, its `BeadField`, its comparison spire, the pause/speed controls. Two live for the whole session — Insertion and Merge. |
| `Controls.cs` | `SliderControl` (a bead on a rail — reused for both clock speeds, the input-size rail, and the pipeline divider) and `GateToken` (the "block a path" switch). |
| `AlgorithmCityChallenge.cs` | Apply (comparison budget), Explain (predict-then-race), Create (the pipeline bench), and the growth-curve panel. Split out of the main world file exactly as `OrbitalChallenge` is split out of `OrbitalWorld`. |
| `AlgorithmCityWorld.cs` | The `PrismWorldBase` subclass: layout, loop gates, per-frame dispatch, hand interaction, concept declarations. |
| `AlgorithmCityBead.shader` | `Prism/AlgorithmCityBead` — Ceramic's lighting rig, with colour read per-vertex from UV1 instead of a uniform `_Tint`, because up to a few dozen beads share one mesh and one material. |

## Colour law (stated in code, restated here)

Three channels, never mixed:

1. **A datum's hue is its value**, mapped onto the spectral ramp (`Prism_Spectral` in the new
   shader). This is true on both structures, at rest and mid-run. A sorted run reads as a smooth
   gradient appearing out of noise — the "genuinely lovely moment" the contract asks for.
2. **A structure's identity colour is fixed**: Coral for Insertion, Cyan for Merge. Used only on
   the spire, the growth-curve line and the Formalize label for that structure. Never on a datum.
3. **Gold marks a handle** — anything a learner takes hold of to steer the world rather than to
   inspect data: the three slider beads, the pipeline divider, the prediction marker. This mirrors
   Orbital's own use of Gold for the velocity-arrow handle.

## The loop

Wonder shows only the Insertion row, already mid-demo on a small ambient batch, looping on its
own, before the learner touches anything — deliberately as reduced as Orbital's single still moon.
The gate is simply having grabbed a bead once.

Explore reveals Merge, the spawner, both gates, both clock sliders and the input-size rail. Every
spawn from here feeds an **identical** shuffled batch to both structures at once — that pairing is
what makes the Discover gate answerable at all. The gate itself is not "run something" but
`ObserveBest` on the ratio `insertion.Comparisons / merge.Comparisons` across every completed pair,
required to reach 1.4x at least once. Because the input-size rail is available from Explore (not
gated behind Discover), a learner who only ever runs small batches will find insertion and merge
cost about the *same* — insertion is genuinely competitive at small n — and has to enlarge the
batch themselves to find the gap. That is the "straddle" for this world: not bound-vs-unbound, but
small-n-vs-large-n, and it is why Formalize's gate additionally requires a completed small (n<=8)
*and* large (n>=28) run before it opens.

Discover reveals the growth-curve panel, already populated with every sample recorded since
Explore. Formalize adds the two structures' names, their Big-O ("Insertion sort - O(n^2)" / "Merge
sort - O(n log n)") and their live comparison counts as text — the first and only text in the world
before this point, and it appears beside curves the learner already measured, so the notation is
attached to evidence rather than asserted on its own. Apply is a comparison budget drawn as a ring at the
exact spire height that many comparisons reaches (not a number to read, a place to stay under).
Explain arms a prediction on the next run and forces both clock sliders to 1x for exactly that run,
so the race is decided by the algorithm and not by whichever slider was left higher. Create reveals
a third track: split a batch at a hand-placed divider, Insertion left of it, Merge right, one more
real merge to honestly combine the two sorted halves.

## What is simulated honestly

- **Every comparison is real.** `InsertionSortProcess`/`MergeSortProcess` are literal
  implementations that mutate a live `int[]`; `Comparisons`/`Moves` are incremented exactly where a
  real comparison/write happens. The world only ever reads `Comparisons` after the fact — it never
  computes n^2/4 or n*log2(n) and displays that instead.
- **The growth curve is measured, not drawn.** `AlgorithmCityChallenge.OnPairComplete` appends the
  real `(n, Comparisons)` pair from the run that just finished; the polyline is rebuilt from the
  accumulated sample list. If a learner runs the same n twice, both points are plotted, even if
  that makes the line zig slightly — the alternative (silently keeping only one) would be a curated
  curve, and the brief's honesty condition ("if a learner ever finds the curve disagrees with the
  count, the world has lied") reads as license to plot everything, not to smooth it.
- **A hand can genuinely change the outcome.** `Values` is the single source of truth for both
  algorithms; grabbing a bead pauses its structure (no `Step()` calls while `HeldBeadIndex >= 0`),
  and dropping it near another bead performs a real `(vals[a], vals[b]) = (vals[b], vals[a])` swap.
  The very next `Step()` call reads whatever is now actually in the array. This is not a special
  case anywhere — it falls out of the array being live.
- **"Block a path" really stops the algorithm.** `GateToken.Blocked` is copied into
  `SortStructure.GateBlocked` every frame; while true, `Tick` never calls `Process.Step()`. Nothing
  fakes a pause — the structure's `Comparisons` counter simply does not move.
- **"Slow the clock" changes the real rate.** `SpeedMultiplier` scales how many `Step()` calls
  happen per second, not an animation playback speed. At 0.15x a learner watching Insertion can see
  every individual comparison; the count is exactly the same as running it at 4x.

## What is approximated, and why

- **Merge sort is implemented bottom-up (iterative), not top-down (recursive).** Same algorithm
  family, same O(n log n) comparison count. I hand-traced both processes against small arrays
  (e.g. insertion sort over `[3,1,4,2]`: compares (3,1) swap, (3,4) no swap, (4,2) swap, (3,2) swap,
  (1,2) no swap — 5 comparisons, 3 swaps, correctly sorted) while writing them, but that trace lives
  only in my own working notes, not in the source — I do not have a compiler in this loop to run
  either one for real, so this is a hand-check, not a verified result. Bottom-up merge was chosen
  specifically because a flat, resumable state machine can be driven one honest comparison at a
  time and safely resumed after a learner's hands have changed the array mid-merge, with no call
  stack to unwind or reconcile. **This is the one place a sharp learner or reviewer could catch a
  gap between what is taught and what is coded**: the `recursion` concept and the Merge structure's
  cascading visual both teach the IDEA of recursion honestly (a problem solved by combining answers
  to smaller copies of itself — the cascade's self-similar levels are real, not decorative), but
  the C# implementing Merge does not call itself. I judged this an acceptable, standard substitution
  (any algorithms text presents bottom-up merge sort as "the same algorithm," not a different one),
  but it is worth the master's attention if `recursion` is ever cross-checked against the code that
  is supposed to instantiate it.
- **Spire height is square-root compressed**, not linear: `height = 0.011 * sqrt(comparisons)`,
  clamped to 24 cm. A linear scale would either make small-n spires invisible or send a 32-element
  worst-case insertion sort (up to 496 comparisons) off the top of reachable space. The compression
  is monotonic and always computed from the real count — never inverted, never faked — but it is a
  ruler with a curve in it, not a straight one. The exact integer is what the Formalize label shows,
  and the growth-curve panel plots true magnitudes (dynamically rescaled to whatever the largest
  measured value so far is), so precision is never lost, only the spire's own spatial scale is
  non-linear.
- **Manual swaps mid-merge can leave a stale row for one step.** `MergeSortProcess.RowOf` is
  per-index bookkeeping updated only when a window publishes. If a learner swaps a bead already
  settled into a high row with one still in an unmerged low window, the two values move instantly
  (honestly — the array itself is correct) but the swapped-in value keeps displaying at its old
  height until the next time a merge window containing its new index publishes. Comparison counts,
  colours and the final sorted result are always exact; only this one row indicator can lag by a
  step in this specific edge case. Documented in `SortStructure`'s class comment too.
- **The Apply budget constant (`1.3 * n * log2(n)`, enforced only for n>=24) is reasoned by hand,
  not verified by running the code.** I traced the statistics by hand: Insertion's comparison count
  for a random permutation is approximately Normal(mean = n(n-1)/4, sd ~ sqrt(n^3/18)); at n=28 that
  puts the budget (~175) about 1.1 standard deviations below the mean, so the great majority of
  random shuffles fail it, while Merge's near-input-independent cost (~110-135 at n=28) clears it
  comfortably. I have no compiler in this loop to confirm this by actually running a batch of
  shuffles — **the master should sanity-check this once the build compiles**, and retune
  `AlgorithmCityChallenge.BudgetFor`'s `1.3f` multiplier or `ApplyMinN` if Apply feels too easy, too
  hard, or luck-dependent in practice.
- **Gate thresholds (`CostRatio >= 1.4`, small-n <= 8, large-n >= 28, run counts) are hand-tuned
  from the same statistical reasoning**, not from a playtest. If Explore feels like it never
  releases, or releases immediately, these are the numbers to move — they live as named constants
  at the top of `AlgorithmCityWorld.cs` and inside the gate lambdas in `ConfigureLoop`.
- **Grabbing a specific bead gets physically harder as n grows** (slot spacing shrinks from ~7 cm
  at n=4 down to under 1 cm at n=32, and bead radius shrinks with it). This is a real, intended
  consequence of packing more data into the same reach, not a bug — but it is not softened by any
  fallback (no pointing-at-a-distance selection the way `OrbitalWorld.TryGrabWith` supports for
  moons). At high n, precise mid-flight grabs are meant to feel harder; if that reads as "broken"
  rather than "honest" in practice, a reach-cone fallback modelled on Orbital's would be the fix.
- **Apply narrows "route data through a structure to hit a cost budget" to "scale the input until
  the budget separates the two structures."** I did not build a literal carry-and-drop-into-an-
  entrance routing gesture — both structures always run the same batch together (that pairing is
  load-bearing for the Discover gate, and I chose not to special-case Apply's data flow away from
  it). Instead the learner's agency is in the input-size rail they already have from Explore: at
  the enforced n>=24, Merge reliably clears the budget and Insertion reliably does not, so hitting
  the budget is a real, earned consequence of recognising which regime you are in and choosing n
  accordingly — but it is not the "carry a batch through a doorway" gesture the brief's wording
  suggests, and a future pass could add real routing if that gesture is judged worth the extra
  mechanism.

## Reachability

Every interactive element sits within a shallow local-space footprint (`Assets/Prism/Worlds/
AlgorithmCity/AlgorithmCityWorld.cs`, the block of `static readonly Vector3 ... Origin` constants).
`Reach`/`EyeToTable` are overridden to 0.52/0.46 m (base defaults are 0.60/0.50) specifically to
keep the diagonal eye-to-anchor distance close to `OrbitalWorld`'s own baseline (~0.74 m for its
cradle moons). The farthest content — the Create-stage pipeline bench — lands at roughly 0.76 m
from the eye, comparable to but not beyond the reference world's own reach. This was reasoned by
hand (see the comment above the Origin constants); it has never been checked on a headset or even
in the editor, because I am not permitted to run Unity. If a device test finds the bench or the
divider genuinely out of reach for a real arm, pull `BenchOrigin.z`/`DividerRailOrigin.z` in
further (currently 0.15), or add a pointing fallback to the bead/slider grab checks.

## Concepts declared

`algorithm-city` (door, System, Machines), `algorithmic-cost` (Equation, Mathematics), `recursion`
(Process, Machines), `data-structures` (System, Machines), `comparison` (Fact, Machines). Placed at
azimuth 108-144 deg, distance 3.3-5.3 m, elevation -0.40 to +0.35, inside the assigned wedge.

Links to existing concepts are exactly the two the brief suggested and no others were forced:
`recursion` -> `symmetry` (Analogy — a recursive process's self-similarity across scale is
structurally the same shape as a spatial symmetry) and `recursion` -> `feedback` (Analogy — a
recursive process is a loop that either stabilises at a base case or, lacking one, runs away
exactly the way an unstable feedback loop does; a stack overflow *is* "a loop that runs away" in
this precise sense). I considered and rejected a link from `algorithmic-cost` to the existing
`inverse-square` concept — both are power-law shapes, but one is decay and the other is growth, in
different domains, and the link felt like a pun rather than a real relationship, so it is not
there.

## What the master must wire

- **Nothing structural.** `WorldRegistry` discovers `AlgorithmCityWorld` by reflection; `Concepts`
  and `Shaders` are safe to read on a freshly-`Awake`d, never-`Enter`ed instance (no dependency on
  `Hands`/`Head`/`Knowledge`/`Loop`), which is what `WorldRegistry.InspectAll` requires.
- **`Prism/AlgorithmCityBead` must be in the Always Included Shaders list** — it will be, via
  whatever build step unions `WorldRegistry.AllWorldShaders()`, same as every other world's
  runtime shaders. Nothing extra needed from me or called out beyond confirming that step runs.
  Shader depends on `PrismOptics.hlsl` via a relative include (`../../Aesthetic/Include/
  PrismOptics.hlsl`), exactly as the contract's own guidance specifies; I did not modify any shared
  include.
- **Aggregate `KnowledgeAtrium.ReachRange` must cover at least 5.3 m** (my furthest concept). With
  ten worlds now contributing concepts, this is a shared number the master sets once for whichever
  world reaches furthest overall — not something I can set myself since I do not own the atrium.
- **The shared `Assets/Prism/Demos/**` files themselves (`ConceptDemo.cs`, `ConceptDemoRegistry.cs`,
  `ConceptDemoAttribute.cs`) are still untouched by me** — read only, per the same off-limits rule.
  Round 2 closed the gap this note used to describe: the four supporting concepts now have bespoke
  pocket demonstrations registered via `[ConceptDemoFor]` from inside my own folder (see below), so
  nothing here should still be falling back to `LatentDemo`. `algorithm-city` itself (the door
  concept) has no pocket demo — holding it in the constellation still only offers the fallback,
  since entering the full world is that concept's actual demonstration and a 17 cm pocket version
  of "walk through a computation" would undersell it. If the master wants every concept including
  doors to have a pocket preview for consistency, that is a further, deliberately unmade call.

## What I could not finish / did not attempt

- No sound cues beyond the base class's default per-stage companion consonance. A wrong Explain
  prediction correctly calls `Knowledge.FlagMisconception` (the data-level signal the atrium reads)
  but I did not additionally wire `PrismVoice.Tension` for an immediate audible cue the way it might
  deserve — same gap for a correct prediction and `PrismVoice.Consonance`.
- No "ratio beam" connecting the two spires' tops at Discover. Considered, scoped out for time —
  the spires' own visible height difference plus the growth-curve panel already carry the signal.
- No pointing-at-a-distance fallback for bead/slider/gate grabbing (see Reachability above) — direct
  proximity only.
- Draw calls: by my count, roughly 20 mesh renderers total across both always-present structures,
  the bench, two spires, the budget ring, two curve lines, the prediction marker, the spawner, two
  gates, and three sliders' rail+bead pairs (plus two `PrismLabel` TextMeshPro objects, visible only
  from Formalize on). Comfortably under the ~40 budget, but never actually profiled on a device or
  in the editor — I have no compiler in this loop to confirm it either.
- Every number and every piece of reasoning above that says "hand-traced" or "reasoned by hand" is
  exactly that: worked through on paper against known statistical properties of insertion and merge
  sort, not observed by running this code, because running it is the master's job, not mine, in
  this workflow. Please treat the tuning constants at the top of `AlgorithmCityWorld.cs` and
  `AlgorithmCityChallenge.BudgetFor` as a first, honest guess rather than a measured result.

## Round 2 — pocket demonstrations

`AlgorithmDemos.cs` (new file, same folder, namespace `Prism.Worlds.AlgorithmCity`). Four
`ConceptDemo` subclasses, each registered by `[ConceptDemoFor("...")]` rather than by editing
`ConceptDemoRegistry.cs`, which I did not open with intent to change and have not touched. Each
follows the house grammar exactly — holding hand carries it, free hand operates it via
`TryFreeLocal`/`FreePinch` — and none allocates inside `OnTick` (every `new` in the file is either
inside `Build()`, called once, or a `Vector3`/`Quaternion` struct construction, which never
heap-allocates in C# regardless of where it appears — verified by reading every `new` in the file
by hand and classifying it, since I have no profiler in this loop either).

- **`RecursionDemo`** — five genuinely concentric shells (real GameObjects at real, different
  radii, not one mesh faking depth). Free-hand distance from the centre selects which shell is
  "current," computed live from real hand position every frame; reaching inward literally descends.
  The innermost shell is a different material (Seed, not Gel) and a different colour (Gold), because
  a base case has to be qualitatively different from a recursive case or the idea is not being
  shown. Honesty note: there is no continuous physical quantity here the way gravity or a wave gives
  one — recursion's honesty is structural, not numeric. The "recursion" being demonstrated is real
  (an actual nested structure, a real depth index, a real distinct terminal case) but there is
  nothing dishonest to check it against the way a growth curve can be checked against a comparison
  count. Worth stating plainly rather than implying a numeric rigor that would not apply.
- **`AlgorithmicCostDemo`** — free-hand x-position sets n (2 to 12) over up to twelve motes; two
  real loops (one single pass, one nested over all pairs) run every frame and drive two bars to
  heights `count * 0.0115`, on the SAME scale so they are directly comparable by eye. Fully honest:
  the loops are the real thing being counted, not `n` and `n*(n-1)/2` computed as closed forms and
  displayed as if measured — I specifically avoided the shortcut here because it is exactly the kind
  of thing the brief warns against ("never a formula drawn as a curve"), even though at n<=12 the
  closed form and the counted loop necessarily agree.
- **`DataStructuresDemo`** — a fixed shuffled row of 8 values above a fixed sorted row of the same
  8 values (`{5,2,7,1,8,3,6,4}` and `{1..8}`, hardcoded rather than sorted at runtime with
  `Array.Sort`, so the relationship between the two arrays is visible on inspection rather than
  trusted to a call I cannot execute to check). Pointing at a slot in the sorted row names a target
  value; both rows then genuinely search for it on a real 0.24 s tick — linear scan on top, real
  binary search (real midpoint, real range-halving) on the bottom — and two bars accumulate real
  comparison counts. This is the closest pocket demo to the full world's own thesis, deliberately:
  data-structures is "the same lesson, zoomed to a single fixed example," not a different lesson.
- **`ComparisonDemo`** — a beam balance. Free-hand height sets the right pan's value continuously;
  a pinch triggers exactly one real `>` comparison against the left pan's value, which then
  re-randomises so the next comparison is against something new. The beam's tilt is decided by that
  boolean, not animated toward a guess. Each pinch also raises a small column by a fixed unit —
  literally counting decisions made, the smallest and most honest version of the cost-as-a-physical-
  quantity idea the whole world is built around.

Approximations, stated once more in one place: `RecursionDemo`'s five-level depth is a fixed,
arbitrary choice (not derived from anything); `AlgorithmicCostDemo`'s n range (2-12) and bar scale
(0.0115) were chosen so the gap is dramatic by n=12 without the pairs bar leaving the demo's own
±1 local space, reasoned by hand rather than tuned against a running build; `DataStructuresDemo`'s
eight fixed values were chosen so the binary search's three worst-case steps and the linear scan's
worst-case eight are both comfortably visible within a few seconds at the 0.24 s tick rate, again by
hand, not by playtest. None of these four were run, previewed, or profiled — same caveat as the rest
of this document, restated because it is still true.
