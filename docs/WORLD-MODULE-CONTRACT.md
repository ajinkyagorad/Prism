# World Module Contract

**Read this fully before writing anything.** You are building one Concept World for PRISM alongside
nine other agents working in parallel on other worlds. This document is what keeps ten independently
built modules coherent and prevents you from destroying each other's work.

---

## THE HARD RULES

### 1. NEVER RUN UNITY. Not once.
Do not run `Unity`, `-batchmode`, `-executeMethod`, any build, any test that launches the editor.

The entire project shares one `Library/` directory and one editor lock. Two Unity processes against
this project produce **an immediate core dump** — that has already happened here once. Ten would
guarantee corruption.

The master agent compiles, verifies and builds. Your C# will be compiled by them. Write carefully
enough that it compiles first time: you do not get a compiler in this loop.

### 2. Write ONLY inside your own folder.
You own exactly one directory: `Assets/Prism/Worlds/<YourWorld>/`. Create it. Everything you write
goes inside it — C#, shaders, includes, everything.

**Do not edit, and do not even open with intent to change:**

```
Assets/Prism/Editor/**          Assets/Prism/Core/**        Assets/Prism/Aesthetic/**
Assets/Prism/Atrium/**          Assets/Prism/Demos/**       Assets/Prism/Interaction/**
Assets/Prism/Scenery/**         Assets/Prism/Worlds/PrismWorldBase.cs
Assets/Prism/Worlds/OrbitalMechanics/**
ProjectSettings/**  Packages/**  Library/**  Build/**  *.md at repo root
```

Reading them to learn the conventions is not just allowed, it is expected. Writing to them is not.
If you believe a shared file must change, **say so in your final report** and the master will do it.

### 3. No git, no APK, no package installs, no network fetches of assets.
Everything in PRISM is generated procedurally. There are no textures, no imported models, nothing
downloaded. Keep it that way — it is why the APK is 40 MB and why the product has its own identity.

---

## What PRISM is

A Quest 3 educational XR product where a learner does not watch a lesson but enters a concept, takes
hold of it, and finds out what it does. Unity 6000.5.5f1, **Built-in render pipeline**, Vulkan,
single-pass instanced stereo, 72–90 Hz.

The learner stands on a high plateau at dawn with their knowledge constellation floating around them.
Holding a concept blooms a live demonstration; carrying one to their chest takes them **inside** it —
that is your world.

Read `docs/VISION.md` for the full brief and `CLAUDE.md` for hard-won engineering facts. Read
`Assets/Prism/Worlds/OrbitalMechanics/OrbitalWorld.cs` as the reference implementation: it is the one
finished world and shows the standard you are matching.

---

## The pedagogy is the product

Every world runs the same eight-stage loop, and the ORDER is the whole point:

| stage | what happens |
|---|---|
| **Wonder** | An unexplained phenomenon. No labels, no instructions, no UI. |
| **Explore** | Free manipulation. Still no instructions. |
| **Discover** | Causal structure surfaces — *after* the learner has produced the evidence for it. |
| **Formalize** | Only now: names, equations, diagrams. |
| **Apply** | Solve something with it. |
| **Explain** | Predict an outcome, or teach it. |
| **Create** | Build something of your own with it. |
| **Connect** | See how it relates to everything else. |

**Stages advance on EVIDENCE OF WHAT THE LEARNER DID — never on an answer, never on a button.**
There is no "I understand" button and no quiz anywhere in this product.

The exemplar gate, from the orbital world: Explore → Discover waits until the learner has produced
**both a bound and an unbound trajectory** — once they have personally straddled the boundary, they
are ready to be shown it. Aim for gates with that quality: the learner earns the reveal by doing
something that proves they are ready for it.

```csharp
loop.AddGate(new LoopGate(LoopStage.Explore,
    "has not yet made both a bound and an unbound path",     // why they are stuck, for the companion
    e => e.Count("release") >= 4 && e.Has("bound") && e.Has("unbound")));
```

Record evidence from your simulation: `Loop.Evidence.Record("key")`,
`Loop.Evidence.ObserveBest("key", value, lowerIsBetter)`, `Loop.Evidence.Set("key", value)`.

---

## The aesthetic law

**Post-digital living knowledge.** Luminous, translucent, spectral pastels, organic geometry mixed
with precise scientific structure. No rectangular panels, no dashboard UI, no classroom, no neon
cyberpunk.

### Colour means something
Use `PrismPalette`: `Cyan, Mint, Lavender, Coral, Gold, Violet, Warm`, and `Spectral(t)` for a
continuous ramp. **Colour encodes meaning; it never decorates.** Pick an encoding and state it in a
comment — e.g. in the orbital world, trail colour *is* speed, so Kepler's second law is visible before
it is named.

### Composition: never assume the background
`Prism_Compose()` in the shaders blends between darken-and-saturate (bright backdrop) and emit (dark
backdrop), driven by `_PrismBackdropLuma`. **Never write a plain additive pass** — it is invisible
against a bright sky. What survives on any backdrop: thin bright specular hairlines, saturation,
silhouette, motion.

### Existing materials — prefer these to writing new shaders
```csharp
PrismMaterials.CeramicBody(colour, luminance, filmNm)   // frosted luminous solid
PrismMaterials.New(PrismMaterials.Gel)                  // translucent, internal currents
PrismMaterials.New(PrismMaterials.Volumetric)           // cloud matter
PrismMaterials.New(PrismMaterials.Seed)                 // crystallising concept body
PrismMaterials.ForRelation(relation, strength, phase)   // flowing current along a tube
PrismMaterials.TrailMaterial(speedLo, speedHi)          // speed-coloured ribbon
PrismMaterials.New(PrismMaterials.Field)                // iso-contours of a real potential
```

If you DO write a shader: Built-in RP, `CGPROGRAM`, `#include "UnityCG.cginc"` then your include,
`#pragma multi_compile_instancing`, and the stereo macros (`UNITY_VERTEX_INPUT_INSTANCE_ID`,
`UNITY_VERTEX_OUTPUT_STEREO`, `UNITY_SETUP_INSTANCE_ID`, `UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO`).
Copy the structure from `Assets/Prism/Aesthetic/PrismGel.shader`. Include shared optics with
`#include "../../Aesthetic/Include/PrismOptics.hlsl"`. Always call `Prism_Deband()` before output —
pastel gradients band badly on the Quest panel. **`line` is a reserved word in HLSL.** Declare your
shaders in the `Shaders` property so the master can register them (unregistered runtime shaders are
stripped from the build and render as *nothing*).

### Geometry
`PrismMesh.Icosphere(subdiv)`, `Disc(seg, rings)`, `Tube(points, radius, sides, reuseMesh)`,
`Arrow(...)`. Everything procedural.

---

## Interaction

`PrismHands.Hand` gives you: `IsTracked`, `Position`, `Rotation`, `Grip`, `Pinch`, `IsGrasping`,
`GripDown/Up`, `PinchDown/Up`, `Velocity` (smoothed, throw-quality), `IndexTip`/`ThumbTip`/`TipsValid`,
and `Reach` (a `Ray`) with `ReachValid`. `PrismHands.PointOf(h)` gives the fingertip if available else
the grip. Haptics: `Hands.Clunk(h)` (two pulses — reads as a physical seat), `Hands.Buzz(h, amp, secs)`.

Base-class helpers: `NearestHand(point, maxDist)` and `PointedAt(candidates, positionOf, cone, range)`.

**Things must be reachable.** A previous build authored content 0.85–2.85 m away and only allowed
selection within 0.13 m — nothing in the product could be touched by anyone. Either put it within
arm's reach or support pointing. This is a hard requirement, not a nicety.

**Interaction should teach the concept.** Grab a vector by its arrowhead; stretch a wavelength with
both hands; push time along a spatial timeline. The concept itself is the interface; buttons and
panels are a last resort.

---

## Text

Almost none, and never at rest. `PrismLabel.Create(name, parent, head, size)`, then `SetText(body,
colour)`, `PlaceAbove(worldPoint, offset)`, `Show(bool)`. It handles metre sizing, billboarding and
ASCII sanitising. **Labels appear on approach or at Formalize, and fade when attention moves on.**
The bundled font has no arrows or checkmarks — ASCII only.

---

## Sound

`Companion.PrismVoice`: `Consonance(pos, gain, pitch)` for something resolving, `Tension(pos)` for a
contradiction, `Settle(pos)` for something coming to rest, `Resonance(pos)` for ambient presence.
Nothing in PRISM has a sharp attack; nothing alerts anybody.

---

## Performance budget

Quest 3, 72–90 Hz, and your world will not be alone on screen. Assume you have **roughly a third of a
frame**. Concretely:

- Reuse meshes and buffers. **Never allocate per frame** — no `new List<>()` inside `Tick`. A previous
  demo allocated 15 lists per frame and that is the kind of thing that shows up as hitching.
- Prefer one dynamic mesh over many renderers. Camera-facing quads in a single mesh beat 30 GameObjects.
- Keep total draw calls in your world under about 40.
- Fixed timestep for anything physical; accumulate real time and step at a constant dt.
- **Symplectic integrators for orbital/oscillatory motion.** Explicit Euler bleeds energy and makes
  orbits spiral, which teaches a falsehood that is harder to remove than the lesson was to give.

---

## What you deliver

Inside `Assets/Prism/Worlds/<YourWorld>/`:

1. **`<YourWorld>World.cs`** — a `PrismWorldBase` subclass. Implement `WorldId`, `PrimaryConceptId`,
   `BuildWorld()`, `ConfigureLoop(loop)`, `Tick(dt)`, and override `Shaders` and `Concepts`.
2. Any supporting C# — simulation, sub-systems — all in your folder, all in namespace
   `Prism.Worlds.<YourWorld>`.
3. Any shaders you need, in your folder, named `Prism/<YourWorld><Thing>`.
4. **`NOTES.md`** in your folder: what you built, the encoding choices you made, what is simulated
   honestly vs approximated and why, anything the master must wire, and anything you could not finish.

Your `Concepts` property should declare 3–6 concepts: one carrying `WorldId = "<your id>"` (that is
the door into your world), the rest supporting ideas, with `Links` to each other and to existing
concepts where a real relationship exists. Existing concept ids you may link to:

```
orbital-mechanics  gravity  inverse-square  conic-sections  energy-conservation
angular-momentum   kepler-laws  escape-velocity  vectors  periodic-motion
why-orbits-persist waves  rhythm  tides  symmetry  probability  feedback
```

Place your concepts in a distinct direction from the origin so the constellation does not collide —
your task prompt gives you a direction wedge and a distance band. Respect it.

---

## Definition of done

- The world runs all eight loop stages with gates driven by learner action.
- A learner who has never been told anything can reach Discover by playing.
- Every simulation computes its subject rather than miming it.
- No allocation in `Tick`. No additive passes. No text at rest. Nothing out of reach.
- `NOTES.md` is honest about what is approximate and what is unfinished.

Write it as if the person maintaining it has never met you, because they have not.
