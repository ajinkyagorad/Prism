# Trade Networks — NOTES.md

## What this is

A network of five generic settlements, each holding a real stock of four generic goods
(grain, ore, cloth, salt). Nothing here is a person, a population, a nation or a conflict —
every settlement is just a store with a production rate in, a consumption rate out, and a
price that its own stock implies. The learner draws routes between settlements with a pinch
gesture; goods then flow along those routes, driven by nothing but the real price gap left
over once the cost of the route itself is paid.

Files:
- `TradeSim.cs` — the economics. Plain C#, no `MonoBehaviour`, same discipline as `OrbitalSim`:
  it can be reasoned about (and eventually tested) apart from any rendering.
- `CivilizationWorld.cs` — the `PrismWorldBase` subclass: geometry, hand interaction (drawing
  and cutting routes), the eight-stage loop, and the constellation concepts.
- `CivilizationChallenge.cs` — Apply / Explain / Create, checked against the live simulation,
  the same way `OrbitalChallenge` checks the ring/prediction/construction stages.
- `CivilizationRoute.shader` — `Prism/CivilizationRoute`, the one shader this world needed.

## The eight stages, briefly

Wonder: five differently-coloured settlements, small neutral piles of goods that do not move,
nothing labelled. Explore: pinch near a settlement and drag to another to draw a route; some
routes glow and flow, some sit dim and barely move; pinch-hold an existing route to cut it.
Discover gates on having made **both** a route that flourished and one that failed — the same
shape as Orbital's bound/unbound straddle — then reveals real numbers on approach. Formalize
names specialisation, comparative advantage, supply and demand and the cost of distance, and
plays a single one-time reveal the moment the two specifically-chosen dissimilar producers are
connected. Apply asks for the whole network to be reached by working routes. Explain highlights
two unconnected settlements and asks for a marked prediction before the route is even drawn.
Create asks for redundancy (every settlement on two routes) and then a real cut, watched to see
whether the network actually holds.

## Colour law (the one thing colour means in this world)

Stated in a comment above `CivilizationWorld.UpdateSettlementViews`:

> Each settlement's body is tinted by `Spectral(0.5 + 0.5*SurplusIndex)`. Shortage sits low on
> the ramp (cyan/mint — thin, cold), balance sits in the middle (lavender), surplus sits high
> (coral/gold/violet — warm, full). It is visible from Wonder onward, before anything is named
> — the same way Orbital's trail colour shows speed before Kepler's second law is named.

`SurplusIndex` is the average, over all four goods, of `(stock - ReferenceStock) / ReferenceStock`
clamped to [-1, 1] — a real, computed quantity, not an authored colour. The same colour reappears
as the two-ended gradient along a route in `Prism/CivilizationRoute`, so the learner watching a
route's ends drift toward the same colour is watching trade actually level an imbalance, not a
metaphor for it.

Everything else in this world is deliberately colour-neutral so that one law stays legible:
- The four goods "beads" sitting near each settlement are a single shared neutral ceramic
  material. Identity comes from a fixed compass position (grain/ore/cloth/salt, always in that
  order) and quantity from size (`sqrt(stock/ReferenceStock)`), not from hue.
- Prosperity — the other emergent quantity in this world — is shown through **shape**, not
  colour: `Prism/Seed`'s own crystallisation (`_Growth`, driven by `Prosperity` normalised into
  0..1) and a small change in the settlement's actual size. A thriving settlement is visibly
  bigger and more resolved; a declining one stays smooth and small.

## Simulate honestly: what's real, what's approximate

**Real, computed every tick (20 Hz fixed step, accumulated from real time, capped catch-up —
see `TradeSim.Advance`):**
- Stocks rise from a fixed production rate and fall from a fixed consumption rate, with
  consumption honestly capped at whatever stock actually exists (`actual = min(desired, stock)`)
  — a shortage is a real unmet need, not a debt.
- Price is `BasePrice * (ReferenceStock / stock) ^ Elasticity`, clamped — a standard
  constant-elasticity demand curve. Elasticity = 1 (halving stock doubles price) is a
  simplification of a real demand curve, chosen for legibility rather than fit to any data;
  said so here rather than dressed up as measured.
- Trade moves each good independently along each route, from whichever settlement is cheaper
  toward whichever is dearer, at a rate proportional to *(price gap − transport cost)* once that
  is positive, capped so a route can never move more than 40% of a settlement's stock in one
  step (a route cannot drain a warehouse in an instant). This is arbitrage, not a scripted rule:
  nothing tells goods which way to go, the price difference does.
- **Transport cost uses the actual length of the polyline the learner's hand drew**, not the
  straight-line settlement distance. A direct route is cheaper to run than a wandering one,
  literally, because the learner's own gesture IS the route's geometry. This is the most
  load-bearing honesty decision in the file: "the cost of distance" is not a slider anywhere,
  it is `TransportCostPerMetre * route.Length` where `Length` is measured from the recorded
  drag points.
- Prosperity integrates from sustained satisfaction alone (`dProsperity/dt = rate *
  (AvgSatisfaction − 0.5)`, clamped to [0.3, 2.5]) — it is never set directly and nothing
  scripts a settlement's growth. It only ever moves because stocks, prices and flows moved it.

**Approximated, and said so:**
- Production and consumption are fixed constants per settlement — "geography" and "size" that
  never change. There is no labour-allocation submodel where a settlement could shift capacity
  between goods; specialisation in this world is entirely a consequence of which goods a
  settlement's stock runs short or long on, not of a reallocation decision anyone makes. This
  is a deliberate simplification: it keeps the mechanism the learner can actually manipulate
  (routes) doing all the pedagogical work, and it keeps the comparative-advantage arithmetic
  below tractable to hand-check.
- `TransportCostPerMetre = 4.0` and `TradeRateConstant = 1.2` are **tuned for legibility at
  table scale, not derived** the way `PrismScale.Mu` is derived from a target orbital period.
  I picked them so that a direct ~0.15-0.25 m route between a strongly mismatched pair
  flourishes and a deliberately wandering path (or a pair that is nearly balanced already)
  starves — see the worked numbers below — but I have not been able to run the simulation to
  confirm the feel in-headset. If routes flourish too easily or too rarely, these two constants
  and `FlourishThreshold`/`StarvedThreshold` (0.35 / 0.05 combined units/s) are the ones to
  retune first.
- The gate thresholds in `CivilizationChallenge` (`SufficiencyFloor = 0.55`,
  `ResilienceFloor = 0.45`, the 4–6 second sustain windows) are estimated by hand from the
  production/consumption totals below, not verified by playtesting. See "Apply gate is
  achievable" below for the arithmetic behind 0.55.
- Initial stock is seeded as `ReferenceStock + 15*(production − consumption)`, clamped to
  [1, 30] — i.e. "as if the imbalance had already been accumulating for a while" — so Wonder
  shows real colour variation on the very first frame instead of forty seconds of a flat grey
  table. The integration from that point on is completely honest; only the starting condition
  is a chosen constant.

## Comparative advantage — verified by hand

This is the claim the world is built around, so here is the check, against the actual numbers
in `CivilizationWorld.BuildSettlements` (units: goods per second; order grain, ore, cloth, salt).

```
Northpoint (index 0):  Production [3.0, 2.0, 0.2, 0.2]   Consumption [3.4, 0.9, 1.0, 1.0]
Southcross (index 2):  Production [1.2, 0.3, 0.2, 0.2]   Consumption [0.9, 0.9, 1.0, 1.0]
```

**Absolute advantage.** Northpoint out-produces Southcross at both goods that matter here:
3.0 > 1.2 grain, 2.0 > 0.3 ore. By raw output, Northpoint is simply better at everything.

**Opportunity cost** (ore given up per unit grain, and vice versa, treating each settlement's
production rates as what it could make if it leaned entirely one way — the standard textbook
test, even though the live simulation never computes this number itself):

```
Northpoint: cost of grain = 2.0/3.0 = 0.67 ore   cost of ore = 3.0/2.0 = 1.50 grain
Southcross: cost of grain = 0.3/1.2 = 0.25 ore   cost of ore = 1.2/0.3 = 4.00 grain
```

Southcross gives up less ore to make a unit of grain (0.25 < 0.67) — Southcross has the
comparative advantage in grain. Northpoint gives up less grain to make a unit of ore
(1.50 < 4.00) — Northpoint has the comparative advantage in ore. **This holds even though
Northpoint is absolutely better at both goods** — exactly the counter-intuitive claim the brief
asked the world to be built around.

**Does the live mechanism actually reproduce it?** The simulation never touches opportunity
cost; it only ever moves goods toward the higher local price. Check the *net* position
(production − consumption), which is what really drives price and therefore trade:

```
Northpoint: grain 3.0 - 3.4 = -0.4 (short)   ore 2.0 - 0.9 = +1.1 (surplus)
Southcross: grain 1.2 - 0.9 = +0.3 (surplus) ore 0.3 - 0.9 = -0.6 (short)
```

Northpoint is short of exactly the good Southcross has spare, and long on exactly the good
Southcross lacks. So a route between them will show grain flowing Southcross → Northpoint and
ore flowing Northpoint → Southcross, on the same tube, simultaneously — the market discovering
comparative advantage through price alone, which is the honest and standard way it actually
works (nobody in a real market computes an opportunity-cost table either).

**Do both settlements actually gain?** Under autarky (no trade, so `actual = min(desired,
own production)`):

```
Northpoint: grain 3.0/3.4 = 0.88 satisfied, ore fully satisfied (produces more than it needs)
Southcross: grain fully satisfied, ore 0.3/0.9 = 0.33 satisfied
```

If a route closes both gaps (upper bound; a real route only closes what the price gap minus
transport cost allows): Northpoint's grain satisfaction rises 0.88 → 1.0; Southcross's ore
satisfaction rises 0.33 → 1.0. Both improve, on exactly the goods the opportunity-cost argument
predicted. `CivilizationWorld.BuildLabelText` surfaces this same pair by name once the learner
has actually connected them and reached Formalize (see the one-time reveal, gated on that real
action, in `UpdateComparativeAdvantageReveal`).

## Apply gate is achievable — sanity check

World totals (sum of all five settlements): production [6.4, 3.8, 3.3, 3.0] vs. desired
consumption [7.0, 4.5, 4.7, 4.6]. The network is short of every good even in principle — global
coverage is 91% grain, 84% ore, 70% cloth, 65% salt, averaging ~78%. So even a perfectly
connected, zero-cost network could not push every settlement's satisfaction to 1.0, which is
intentional (unbounded growth would be a worse lesson than bounded, real scarcity). The
`SufficiencyFloor = 0.55` gate sits comfortably below that ~0.78 ceiling and comfortably above
an isolated settlement's own baseline (Northpoint alone: ~0.57; a single-good specialist alone
with only trace secondary production: ~0.25–0.4), so it should be reachable with genuine
multi-route connection and not before. I was not able to confirm this by running the sim.

## Draw calls

Worst case, all five settlements fully interconnected (10 possible pairs — unlikely but
possible): 5 bodies + 20 beads (25) + 1 ground + up to 10 routes + 1 live drag preview while
actively drawing + up to ~2 labels realistically visible at once (a label costs nothing while
`Show(false)`, and only settlements within `SettlementInspectRadius` of a tracked hand show one)
≈ 39. That is at the edge of the "roughly 40" budget in the contract, in the rare case a
learner connects literally every pair. I chose not to hard-cap route count — refusing a route
the learner just spent effort drawing, with no feedback as to why, seemed like the worse failure
mode — but flagging it here: if profiling shows this world is too heavy alongside others on
screen, capping simultaneous routes (e.g. at 8) or merging the four beads into one combined
per-settlement mesh (cutting 20 draw calls to 5) are the two cheapest fixes, in that order.

No per-frame heap allocation: `TradeSim.Step` only writes into arrays/lists sized at
construction; the one deliberate exception is `CivilizationWorld.BuildLabelText`, which builds a
`StringBuilder` — but only while a label is actually visible (a hand within ~9 cm of a
settlement, post-Discover) and throttled to at most 4 times a second per label
(`LabelRefresh`), not every frame. Commented in place.

## Concept links — what I did and did not claim

Declared: `civilization` (door, kind System, domain Society), `specialisation`,
`comparative-advantage`, `supply-and-demand`, `networks` — all domain Society so they cluster
in the assigned wedge (azimuth 328-358 degrees, distance 3.6-5.0 m, elevation ±0.13 m at most,
inside the given 324-360 / 3.2-5.5 / ±0.5 band).

`supply-and-demand` links to the existing `feedback` concept via `Analogy` (0.8): a settlement's
price is a genuine self-correcting loop — scarcity raises price, which pulls in imports, which
lowers price — the same shape as any other negative feedback loop in the product, in new
material. I considered `inverse-square` and `waves` and did **not** link to either: nothing in
this simulation falls off with the square of distance (transport cost is linear in path length)
and nothing oscillates or propagates as a wave. Forcing either link would have been exactly the
kind of claim the brief warned against, so I left them out.

## What the master must wire

Nothing beyond the standard pattern `PrismWorldBase`/`WorldRegistry` already establish — this
world declares `WorldId = "civilization"`, and `Shaders => { "Prism/CivilizationRoute" }` for
the one shader it owns. I noticed `PrismSession.cs` currently wires a single hardcoded
`OrbitalWorld World` field; if that has not already been generalised to iterate multiple worlds
by the time this lands, this world (and the other nine) will need that generalisation to be
enterable at all. Not something I could fix without touching a shared file outside my folder.

## What I could not verify

I have not run Unity against this code — no compiler, per the contract. I read every API this
world calls against its actual source (`PrismWorldBase`, `PrismHands`, `PrismMaterials`,
`PrismMesh`, `PrismLabel`, `LearningLoop`, `PrismSeed.shader`, `PrismFlow.shader`,
`PrismOptics.hlsl`) rather than from memory, and `Prism/CivilizationRoute` is a close structural
copy of the known-working `Prism/Flow` (same includes, same pragmas, same UV convention from
`PrismMesh.Tube`). The numeric constants — transport cost, trade rate, gate thresholds — are my
best hand estimate, flagged above wherever I could not be more certain, and are the first place
to look if a playtest says a stage is too easy, too hard, or never resolves.

---

## Round 2 — pocket demonstrations (`TradeConceptDemos.cs`)

Four `ConceptDemo` subclasses, registered by `[ConceptDemoFor]` rather than by editing the shared
registry, for the four supporting concepts that were falling back to `LatentDemo`. Each is real
computation on real numbers, hand-operated, no text, ~90 seconds. All four read `ConceptDemo.cs`
and `DemosMath.cs` (`FeedbackDemo` especially) before being written, and reuse that file's
`Curve`/`Body`/`FlatMaterial`/`Segment` grammar exactly rather than inventing a parallel one.

### SupplyDemandDemo (`supply-and-demand`)

Two straight lines — supply rising, demand falling — and their crossing, **solved by one line of
real algebra every frame** (`q = (c-a)/(b+e)`), not looked up or animated toward. The free hand
slides the demand line vertically; the crossing point is wherever the two lines actually meet
this frame. Coral instead of gold, and a `Voice.Tension` on release, when the hand has pushed
demand so far the lines no longer cross inside the drawn range — a market that fails to clear is
a real thing that happens to real markets, not an error state to hide.

Rhymes with `FeedbackDemo` deliberately: both are one real number the hand pushes on, answered by
the system rather than declared, and both mark a genuine state change with a sound rather than a
readout — here, on release, `Voice.Settle` if a market exists where the hand left it, `Voice.Tension`
if it does not.

**Honest**: the crossing is genuinely solved from the two live line equations, not a canned
lookup. **Approximate**: the supply and demand curves are linear with fixed slopes (`A,B,C,E`)
chosen for a legible crossing point inside the drawn ~1.7×1.5 board, not fit to anything; a real
supply curve need not be linear. Unlike the full world, there is no stock here that trade could
draw down over time — this is the textbook static-equilibrium picture, not the dynamic one my
world's `TradeSim` runs. That is a deliberate scope choice for a 17 cm demo, not an oversight.

### ComparativeAdvantageDemo (`comparative-advantage`) — the one this round was really for

Two producers, each with a straight production-possibility line between all-grain and all-cloth.
North: 6 grain/day or 3 cloth/day (1 cloth costs 2 grain). South starts at 2 grain/day or 2
cloth/day (1 cloth costs 1 grain) — **absolutely worse than North at both goods**, and still the
cheaper cloth-maker. The demo draws a third line: the real **joint frontier**, built by using
whichever producer is currently cheaper at cloth first, which is genuinely kinked rather than
straight. A gold marker sits on the kink and swells with the perpendicular distance from that
kink to the straight ("naive, split proportionally") line between the two extreme corners — a
real, computed length, not an authored glow. That distance **is** the gain from specialising.

The free hand changes South's cloth ability up and down (`_southCloth`, hand-driven range
0.7–3.3). I checked the crossover by hand: opportunity costs match exactly (2 grain per cloth,
both sides) at `_southCloth = 1`, and at that value the kink point I compute (`P(SouthGrain,
NorthCloth) = P(2,3)`) lies **exactly** on the naive line between `(8,0)` and `(0,4)` — the naive
line's cloth value at grain=2 is `4*(1-2/8) = 3`, matching the kink's cloth value exactly.
Verified in the code's own terms, not just the world's. Push the hand past that point and
`costSouth < costNorth` flips, the kink swaps to route cloth through North instead, and the
bulge grows again with South's advantage now in grain — nothing about the swap is scripted, it
falls out of the one live comparison every frame.

I caught and fixed a real defect while reviewing this file: the doc comment (written before this
check) claimed sweeping the hand **up** to 3 cloth/day converges the two costs. That is
backwards — the actual crossover is at 1, reached by sweeping **down** — and I confirmed it
mathematically before fixing the comment. The runtime code itself was already correct; only the
English describing it was wrong, which is exactly the kind of error that survives silently in a
product this deliberate about honesty, so I want it on record that it was caught, not just fixed.

**Honest**: the joint-frontier construction (specialise the cheaper producer first, kink where it
maxes out) is the standard, correct Ricardian construction, and the bulge is a real perpendicular
distance recomputed every frame from whichever producer is currently cheaper — I did not find a
value of `_southCloth` where the geometry and the claim disagree. **Approximate**: North's numbers
are fixed; only South's cloth ability moves, so the demo explores a 1-parameter slice of the
full space rather than the general case — sufficient to prove the point, not to prove it in full
generality.

### SpecialisationDemo (`specialisation`)

One producer, one straight production-possibility line, one slider (the free hand's X position).
Two small bars show grain and cloth output at the current split, moving in a genuine trade-off —
every unit of one is a real unit less of the other, read off the same line the marker sits on.
Deliberately the simplest of the four: the frontier `ComparativeAdvantageDemo` needs two of, felt
once, alone, first.

**Honest**: the output at any split is computed directly from the slider fraction against the
fixed capacities; nothing is pre-rendered. **Approximate**: a single producer with a linear
frontier and no target/need shown — this demo is about the shape of the trade-off alone, not
about scarcity or trade, which the other three demos and the full world carry.

### NetworksDemo (`networks`)

Six nodes, two triangles of three, each triangle already fully connected, nothing joining the
two triangles. The free hand, reached into the gap between them, builds the one bridging edge;
withdrawn, it cuts it. Reachability from a fixed source (node 0) is recomputed **by a real
breadth-first flood fill every frame** — a fixed-size adjacency matrix and an array-backed queue,
no allocation, no `List<>` — never asserted or pre-coloured. Unreached nodes dim; a bar on the
side reads out the reached fraction and its colour rides the same spectral scarcity ramp used
throughout my world.

I checked the doc comment's specific claim ("adding that one link roughly doubles how many places
each settlement can reach") against the actual topology: without the bridge, node 0 reaches
itself plus its two triangle-mates, 3 of 6; with it, all 6. 3 → 6 is exactly a doubling. Confirmed
correct as written.

**Honest**: the flood fill is a real graph algorithm over a real (if tiny) adjacency structure —
this is the same mechanism, at pocket scale, as the resilience test in the full world (build
redundancy, cut a route, watch what stays reachable). **Approximate**: only one edge is
ever toggle-able and the source is fixed at node 0 — a deliberate reduction to the single cleanest
case (one bridge, one clear before/after) rather than a general graph editor, which a 17 cm demo
has no room for.

### What I verified without a compiler

Same discipline as round 1: no `Func<float,Vector3>` lambda is ever passed to `CurveView.Set`
inside any `OnTick` (the allocation risk the master specifically flagged) — every curve in this
file is rebuilt from a pre-sized, reused `List<Vector3>` field, `.Clear()`-then-`.Add()`-ed, the
same pattern `ConicDemo` and `VectorsDemo` already use. The few local functions that DO close over
outer state (`P(...)` in `ComparativeAdvantageDemo`, capturing `sx`/`sy`) are called directly by
name and never converted to a delegate value, which is the specific case C# compiles as a
stack-passed struct rather than a heap closure — I did not use that fact to justify anything I
was not confident about; where I was unsure I kept the local function capturing nothing, which is
unconditionally free (`SpecialisationDemo`, `SupplyDemandDemo`). Cross-checked every
`[ConceptDemoFor(...)]` string against this world's own `Concepts` declarations — all four match
exactly. Cross-checked every `Material.Set*` call against the actual shader `Properties` block it
targets (`Prism/Ceramic`, `Prism/Flow`, via `FlatMaterial`/`Curve`) — no property name typos.
