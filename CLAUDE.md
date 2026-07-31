# PRISM — project notes

Unity **6000.5.5f1**, **Built-in render pipeline**, target **Meta Quest 3 / 3S**.
Meta XR SDK core **205.0.0**, OpenXR **1.17.1**, IL2CPP / ARM64 / Vulkan / Linear, minSdk 29.

The full product brief is in `docs/VISION.md`. This file is only the things that are true about
*this repository* and were expensive to find out.

---

## What this milestone is

The first vertical slice: **the Knowledge Atrium plus one complete Concept World**, chosen over
breadth deliberately. One world finished proves both the aesthetic and the pedagogy; six stubbed
worlds would prove neither.

Everything is generated from code — scenes, meshes, materials, the concept graph. There are no
prefabs and no imported models. That is not a stylistic preference: this machine has **no
in-headset preview**, so a scene that has to be assembled by hand in a GUI is a scene that cannot
be rebuilt reliably or verified from a terminal.

---

## Build pipeline (Linux, headless)

```bash
UNITY=~/Unity/Hub/Editor/6000.5.5f1/Editor/Unity

# everything short of the APK, in one invocation
$UNITY -batchmode -nographics -projectPath . \
       -executeMethod Prism.EditorTools.PrismBuild.BootstrapFromCommandLine

# the APK
$UNITY -batchmode -nographics -projectPath . \
       -executeMethod Prism.EditorTools.PrismBuild.BuildFromCommandLine
# override the output path with PRISM_APK_OUT

# the three verifications, individually
$UNITY ... -executeMethod Prism.EditorTools.PrismPhysicsTest.RunFromCommandLine   # exits 1 on failure
$UNITY ... -executeMethod Prism.EditorTools.PrismShaderTest.RunFromCommandLine    # exits 1 on failure
$UNITY ... -executeMethod Prism.EditorTools.PrismVerify.VerifyFromCommandLine
```

Menu equivalents: **PRISM → 1. Configure Project / 2. Seed Concept Graph / 3. Build Scene /
4. Build APK**, plus **Verify Scene**, **Test Orbital Physics**, **Test Shaders**,
**Register Shaders For Build**.

`adb` is bundled with the editor and needs no separate install:
`~/Unity/Hub/Editor/6000.5.5f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb`

---

## Verification standard

Three different claims, never to be conflated:

| claim | how it is checked here | status |
|---|---|---|
| **the physics is correct** | `PrismPhysicsTest` — 25 numeric assertions against closed-form Kepler | **passing** |
| **the C# compiles** | any `-executeMethod` run; grep `error CS` | **clean** |
| **the shaders compile** | `PrismShaderTest` via `ShaderUtil.ShaderHasError` | **10/10 clean (editor platform)** |
| **the scene is sound** | `PrismVerify` — wiring, reachability, text, environment, far clip | **all passing** |
| **it links and packages** | `Build/PRISM.apk`, ~43 MB, `BUILD_ERRORS=0` | **succeeded** |
| **it renders correctly** | requires a headset | **NEVER VERIFIED** |

APK verified by inspection: `libopenxr_loader.so` present exactly once, arm64-v8a only,
`com.oculus.supportedDevices = quest3|quest3s`, `oculus.software.handtracking` and
`com.oculus.feature.PASSTHROUGH` features present, `com.oculus.intent.category.VR`, signed with the
Android debug key (a release key is needed before any store submission).

The last row is the important one, and three device tests have now proved it: every single fault
found on a headset was in a build where all the rows above it were green. "Compiles clean", "the
numbers are right" and "renders correctly" are three different claims; always say which one you mean.

Traps in checking the first three:

- **`Shader.Find` returning non-null proves nothing.** A shader with a syntax error still imports,
  still has a name, and still yields a Material — which then draws nothing. Because PRISM's
  materials are alpha-blended rather than additive, a broken shader fails to *complete
  invisibility* instead of to magenta. Use `PrismShaderTest`; it reads `ShaderUtil.ShaderHasError`
  and prints the error text with line numbers.
- **`Logs/shadercompiler-*.log` does not exist in a `-nographics` run.** No graphics device means
  no variant compilation, so grepping for shader errors that way silently finds nothing. This is
  why `PrismShaderTest` exists.
- Editor-platform shader compilation is **not** the Vulkan/Android compilation. Only the APK build
  validates the variants that ship.

---

## Environment facts

- **`com.unity.inputsystem` arrives transitively with the Meta XR SDK.** Whichever single backend
  is active, half the obvious ways of reading input throw: legacy `UnityEngine.Input` raises
  `InvalidOperationException` *every frame* under the new backend, and `Keyboard.current` is null
  under the old one. `PrismConfigure` sets `activeInputHandler = 2` (**Both**) by editing
  `ProjectSettings.asset` through a `SerializedObject`. Unity applies it at the **next editor
  launch**, which is why configure is a separate invocation from the scene build.
- Batch mode logs `Licensing::Module Error: Failed to handshake` and a wall of ALSA/FMOD failures.
  Both are **harmless noise** headlessly — the run still reports
  `Exiting batchmode successfully now!`.
- **Never run two Unity processes against this project at once.** Starting a test run while an APK
  build was going produced an immediate core dump — they contend on `Library/`. The build survived
  and the newcomer died, but it can go the other way. Wait for one to exit. (Two *different*
  projects on the machine are fine; that happens routinely here.)
- **`pgrep -f` matches the waiting script itself.** A wait loop like

  ```bash
  until ! pgrep -f "PrismBuild.BuildFromCommandLine"; do sleep 20; done   # NEVER EXITS
  ```

  matches the `bash -c` process running that very loop, because the pattern appears in its own
  command line. It spins forever and the work behind it never starts — silently, with no error.
  This cost a chained rebuild here.

  `pgrep -x Unity` is not the fix either — the editor does not run under the exact process name
  `Unity` in a `timeout`-wrapped batch invocation, so it reports idle while the build is very much
  alive. **Wait on the artifact, not on the process:**

  ```bash
  until grep -q "BUILD_RESULT=" Logs/apk.log 2>/dev/null; do sleep 20; done   # correct
  ```

  Every long step here prints a unique completion marker for exactly this reason —
  `BUILD_RESULT=`, `ALL PASS`, `ALL CHECKS PASSED`, `ALL … SHADERS OK`. Poll those.
- No device was attached while this was written, so deployment is untested here. `adb kill-server;
  adb start-server` recovers a Quest that dropped off after sleeping.

---

## The first device test: three fatal bugs that every check missed

On 2026-07-30 the first APK reached a headset. The report was "only white floating blocks, no
interaction, no hand tracking, no functional purpose" — and it was accurate. Every automated check
above was green at the time. What follows is why, because the pattern matters more than the bugs.

**1. Nothing was reachable.** Concepts are authored between 0.85 m and 2.85 m from the learner.
Summoning matched only concepts within **0.13 m of the hand**. No concept in the product could ever
be selected, by anyone, ever. The atrium was inert by construction.

The brief says *"a concept can be summoned by reaching toward it"* — I implemented "reaching toward"
as *touching*, which is a different gesture and an impossible one at these distances. Selection is
now: direct touch inside `TouchRadius`, otherwise nearest concept inside an 11-degree cone about
where the hand points (`KnowledgeAtrium.FindReachTarget`). Angular rather than screen-space, so it
behaves identically for a controller, a tracked hand, and either eye.

**2. Hand pinch was unreadable.** Pinch was read as
`OVRInput.Get(Axis1D.PrimaryIndexTrigger, Controller.LHand)`. **Hand tracking does not expose pinch
through OVRInput.Axis1D — that returns 0 forever.** With no controllers in hand, `IsGrasping` could
never become true. The only real source is `OVRHand.GetFingerPinchStrength(HandFinger.Index)`, which
requires an actual `OVRHand` component in the scene; there was none. `OVRSkeleton` now provides
fingertips too, and `PrismVerify` asserts both components exist.

`OVRHand.HandType` and `OVRSkeleton._skeletonType` are **non-public**, so they must be assigned
through `SerializedObject` (the same trap planetpool documented). Getting it wrong is silent: the
component runs and reports untracked forever.

**3. Everything was white.** `PrismSeedBody` desaturated concepts 65% toward the atrium's warm white
at low mastery, on the theory that an idea "has not earned its colour" until understood. On a fresh
profile *every* concept is at mastery 0 — so the entire first-run experience was pale white blobs on
a warm white sky. The nicest-sounding rule in the aesthetic produced the worst possible onboarding.

Fixed by separating the channels properly, which is what should have happened first:

> **Hue means WHICH DOMAIN a concept belongs to** — knowable the moment it exists.
> **Structure means HOW WELL IT IS UNDERSTOOD** — facets, edges, interference, completeness.

Two orthogonal channels, each carrying exactly one thing. Alpha floor raised 0.45 → 0.80.

**Also found:** the companion was invisible. It used `Prism/Volumetric`, which solves a chord through
a **unit sphere in object space**; the motes are quads whose object-space positions are metres from
their transform origin, so `b2` saturated, the chord came out 0, and the density came out 0. Now
`Prism/Mote`. This is the alpha-blended failure mode again — a broken material here renders as
*absence*, which looks exactly like "not implemented yet".

### What the process got wrong

`PrismVerify` checked that every reference was **wired** and concluded the scene was good. Wiring
correctness says nothing about whether a human arm can reach anything. The lesson is that for XR,
**reachability is a testable invariant and must be tested**: `PrismVerify` now asserts that the
furthest concept is inside `ReachRange`, and that if the nearest concept is beyond `TouchRadius`
there is a working reach cone — it would have failed loudly on the original build.

Two things were added purely so the *next* device report can be specific instead of "nothing
happens", which cost a whole round trip:

- **`PrismHandCursor`** — a bead at each fingertip and a short reach beam. Without any hand
  representation, "tracking failed", "aiming at nothing" and "app is inert" look identical.
- **`PrismDiagnostics`** — one line per second to logcat:
  `adb logcat -s Unity | grep PRISM-DIAG`. It reports head pose, per-hand tracked/pinch/grip/tips/
  reach, node count, distance to the nearest concept, and loop stage. Every field answers a question
  that otherwise needs a guess.

### The hand is available to every shader
`PrismHands.PublishToShaders()` sets `_PrismHandL`, `_PrismHandR` (xyz = position, w = tracked) and
`_PrismHandRange` as globals; `Prism_HandProximity(worldPos)` in `PrismOptics.hlsl` turns that into a
0..1 falloff. Materials therefore respond to an approaching hand without the interaction layer
holding a single material reference — which is what makes "the concept itself is the interface"
affordable rather than a pile of highlight objects.

## The second device test: the white void, and no text

Report: *"it grabs blobs, there is no text visible, white VR — rather add natural immersive
scenery."* The reach fix had worked; two things it exposed did not.

### The white void was a design error, not a faithful reading
The brief asks for "an immense, calm, white space with no obvious walls or floor boundary", and that
is what shipped. In a headset it fails, for a reason that has nothing to do with taste: **with no
horizon, no ground and no aerial perspective there is nothing to measure anything against**, so
every floating concept reads as a flat blob at an unknowable distance and the space feels like a
loading screen rather than a place.

`PrismScenery`/`PrismEnvironment` replaces it with a high plateau at dawn: level ground underfoot, a
mist-filled valley beyond, distant ranges, and a sky computed from real Rayleigh and Mie scattering
(`PrismAtmosphere.hlsl`). The intent of the brief survives — immense, calm, no walls — and the three
things that make a space legible are now present: **horizon** (scale), **ground** (where you are),
**aerial perspective** (how far).

Still fully procedural. No textures, no models, nothing downloaded, even though downloading was
offered. Two reasons: a 4K–8K skybox costs Quest memory and bandwidth that analytic scattering costs
almost nothing to beat, and an asset-store landscape would take PRISM's identity with it. Generating
the sky from the physics is also of a piece with everything else here — the field contours are real
equipotentials, the trails are real speed, the sky is blue for the actual reason.

### Changing the backdrop invalidated the composition law
This is the knock-on that mattered most. `Prism_OnWhite` made elements read by **darkening and
saturating** against a white void — and something that reads by being darker than its surroundings
*disappears the instant the surroundings are dark*. Adding a landscape would have made every concept
invisible against the ground.

It is now `Prism_Compose`, blending two behaviours by a `_PrismBackdropLuma` global:

| backdrop | behaviour |
|---|---|
| bright (sky, white) | core saturates and darkens — stained glass |
| dark (ground, night) | core emits |

What survives on *any* backdrop, and therefore carries the important detail: specular hairlines
(the only term above 1.0), saturation, silhouette and motion. **Never assume the background is light
or dark** — that assumption has now been wrong in both directions.

### Text
There is text now, and it is still minimal. Labels appear only on approach — reaching toward a
concept *is* the request for its name — and fade when attention moves on. Holding one also shows its
`capability` line, the "what you should be able to DO", which is the closest thing the product has to
a statement of purpose. The orbital world names the conic and its eccentricity, and only at
Formalize.

**TMP essential resources cannot be imported the obvious way on a build machine.**
`AssetDatabase.ImportPackage` is *asynchronous*: in a `-batchmode` run that exits when the method
returns, the import silently never happens and the font is simply absent afterwards (verified — it
was tried first). `PrismTextResources` unpacks the `.unitypackage` directly instead — it is a gzipped
tar of `<guid>/asset`, `<guid>/asset.meta`, `<guid>/pathname`, and writing the `.meta` files back out
preserves the GUIDs that keep TMP's settings → font → material references intact.

The three TMP traps from holoshowcase are handled in `PrismLabel` and documented there: metre sizing
via `fontSize = size * 1000` with `localScale 0.01`; pivot must follow alignment; ASCII only, enforced
by `PrismLabel.Sanitise` rather than by everyone remembering.

**Do not verify text by counting `PrismLabel`s in the saved scene.** They are created in `Awake`,
which does not run at author time, so the count is legitimately zero. The real dependency is
`TMP_Settings.defaultFontAsset` resolving — that is what `PrismVerify` checks.

### Never name a namespace `Environment`
`namespace Prism.Environment` shadows `System.Environment` for **every file in the assembly**, and
broke `Environment.GetEnvironmentVariable` in `PrismBuild` from across the project. Renamed to
`Prism.Scenery`. The same hazard applies to `Object`, `Debug`, `Random` and `Console`.

### Passthrough now defaults OFF
`PassthroughInWorlds = false`. Passthrough needs a runtime permission, and a denied permission leaves
the learner staring at nothing with no way to tell why — which is exactly the failure mode this
project has already produced twice. The landscape is visible in the world too; passthrough is a
toggle, not a default.

## Things that would have cost a day each

### `line` is a reserved word in HLSL
It is a geometry-shader primitive type. `float line = ...` in `PrismField.shader` produced three
`syntax error: unexpected token 'line'` messages and nothing else. Caught by `PrismShaderTest`; the
variable is now `contour`.

### `MetaXRFeature` must be explicitly enabled, or the Android build dies at link time
Symptom: IL2CPP finishes, then Gradle fails at `:launcher:mergeReleaseNativeLibs` with

```
2 files found with path 'lib/arm64-v8a/libopenxr_loader.so' from inputs:
  - .../jetified-openxr_loader/jni/arm64-v8a/libopenxr_loader.so
  - .../jetified-OVRPlugin/jni/arm64-v8a/libopenxr_loader.so
```

Unity's OpenXR package and Meta's OVRPlugin each ship an OpenXR loader. Meta's
`Editor/OVRGradleGeneration.cs` deduplicates them — but only under this condition:

```csharp
if (metaXRFeature != null && metaXRFeature.enabled)
    importer.SetIncludeInBuildDelegate(path => false);   // for openxr_loader.aar
```

**Enabling the feature set `com.meta.openxr.featureset.metaxr` and calling
`SetFeaturesFromEnabledFeatureSets` is not sufficient in a batch-mode run.** `MetaXRFeature` came
back off, both loaders shipped, and the build failed. `PrismConfigure.EnableMetaXRFeature()` now
sets it explicitly (matched on type name, so a package reshuffle warns instead of failing to
compile) and logs every enabled OpenXR feature — when XR configuration goes wrong it is nearly
always "a feature I assumed was on is off", and that list is the cheapest way to see it.

Diagnosing this from the log is unpleasant: Unity prints the entire process environment inside the
`CommandInvokationFailure` message, so the actual Gradle reason is hundreds of lines away. Go
straight to `grep -n "FAILED\|Execution failed"` and read forward from there.

### Additive glow is invisible on white — this shapes the whole aesthetic
PRISM's environments are luminous white, and additive blending has no effect on a light background.
The usual sci-fi glow vocabulary is therefore **unavailable**. Luminosity on white is built from
three things instead. (Superseded — see "Changing the backdrop invalidated the composition law"
above. `Prism_Compose()` in `PrismOptics.hlsl` is now the only place composition happens, and it
handles both bright and dark backdrops.) The white-backdrop behaviour it blends toward is:

1. the core **saturates and slightly darkens** — stained glass, not neon
2. a wide, very low alpha chromatic **halo**
3. thin, bright specular **hairlines** — the only term allowed to exceed 1.0

Any material that tries to glow by *adding* light to white will simply disappear.

### Debanding is mandatory, not a polish item
Large smooth pastel gradients — which is most of this product — band visibly on the Quest 3 panel.
Every PRISM shader calls `Prism_Deband()` before output. `Prism/Sky` in particular would band into
visible steps across the whole dome without it — a smooth gradient over that many pixels is the worst
case there is.

### Hand anchors, not controller anchors
`OVRCameraRig` drives `LeftHandAnchor` / `RightHandAnchor` at runtime. `LeftControllerAnchor` /
`RightControllerAnchor` exist, look equivalent, and are **never written to**. `PrismVerify` asserts
the binding by name so this cannot regress silently. (Inherited from planetpool, where it made the
controllers appear dead.)

### Simultaneous hands + controllers needs BOTH settings
`OVRProjectConfig.handTrackingSupport = ControllersAndHands` only writes manifest entries. The
scene's `OVRManager` also needs `launchSimultaneousHandsControllersOnStartup`, or the runtime
commits to one modality at startup and the other never tracks for the rest of the session. It is
set through a `SerializedObject` and asserted by `PrismVerify`.

### The base Oculus Touch interaction profile is required
Unity's OpenXR plugin only binds poses, sticks and buttons for explicitly enabled interaction
profiles. Without `OculusTouchControllerProfile`, `OVRInput` reports no pose and no buttons at all.
`MetaQuestTouchPlusControllerProfile` is additive and is not a substitute.

### Measure height down from the head, never up from a floor
There may not be a floor. Under XR Simulation the eye can start at 2.3 m, and content placed at an
assumed table height then sits a metre and a half below the eyeline where nobody finds it.
`OrbitalWorld.EyeToTable` is a distance *below the eye*.

### `Camera.main` is not reliably the camera that draws
Use `OrbitalWorld.FindHeadCamera()` — the enabled, last-drawn camera. The failure mode is every
viewport calculation insisting the geometry is centred while nothing is visible.

### Flatten head pitch and roll when placing world content
Inheriting them tilts the whole simulation with the learner's neck and reads as seasickness. Yaw
only.

---

## Design decisions that are load-bearing

### mu is derived, not chosen — and it is why the lesson works
`PrismScale` picks the table-top planet's standard gravitational parameter so that a circular orbit
at 20 cm has a 6 second period: `mu = 4*pi^2*r^3/T^2 = 8.773e-3 m^3/s^2`. Everything else is real
Newtonian mechanics with no further fudging, and the consequence is the whole reason this world
exists:

```
circular speed at 20 cm   0.209 m/s
escape speed at 20 cm     0.296 m/s
```

The difference between putting a moon into orbit and throwing it away forever is **8.7 cm/s of
wrist**. The learner feels the boundary between bound and unbound motion in their own arm, long
before anyone writes down an energy equation. `PrismPhysicsTest` asserts this gap stays in the
5–15 cm/s band so it cannot quietly stop being true if `mu` is ever retuned.

Throws are **1:1**. Scaling them down would make orbits easier to hit and would destroy the point.

### The integrator choice is pedagogical, not technical
`OrbitalSim` uses kick-drift-kick leapfrog, which is symplectic: energy error is bounded and
oscillates instead of accumulating, so an ellipse closes on itself indefinitely. Explicit Euler —
and Unity's rigidbody solver, for this purpose — bleeds energy, and a visibly decaying orbit would
teach a learner that orbits decay. That is false, and it is a much harder misconception to remove
than the one being taught. **Do not replace this with `Rigidbody`.**

Measured: radius drift 0.0041% over ten periods, worst energy error 0.02% on an e≈0.48 orbit,
measured period within 0.4% of Kepler, identical throws bitwise reproducible.

Softening is 4 mm and deliberately small — large enough to stop a near-radial throw exploding in
one step, small enough that Kepler's third law stays measurably intact (period error 0.000%).

### Understanding stabilises geometry, in one line
`Prism_Crystallise()` quantises the surface normal. Every normal in a neighbourhood snaps to the
same direction, which produces genuinely flat facets from one smooth sphere. Facet count rises with
mastery, so an unresolved concept is smooth and indistinct and a mastered one is a hard crystal —
no mesh swapping, no extra cost. Facet **edges come free** from `fwidth()` of the quantised normal,
so the crystal's wireframe *emerges* as the concept sharpens.

`Prism_Completeness()` makes an unmastered concept literally missing parts of itself. It modulates
alpha rather than calling `clip()` — discard breaks early-z and a soft-edged hole reads better.

### The field contours are real equipotentials
`PrismField` contours the actual gravitational potential of the bodies in the scene at equal
intervals of phi. Because phi goes as 1/r, the lines bunch toward each mass automatically — dense
where the field is steep, for the same reason the real thing is. Contour width comes from the
screen-space derivative, so lines stay one pixel wide and never moire where they bunch.

Note for a future URP move: `_Bodies` must stay **outside** any `UnityPerMaterial` cbuffer.
`Material.SetVectorArray` cannot reach the SRP Batcher's per-material buffer, and tidying the array
into it would silently zero the entire field.

### Trail colour is speed
`PrismTrail` maps speed onto the spectral ramp, so an eccentric orbit draws itself violet and
hurried at perigee and cyan and unhurried at apogee. The learner sees Kepler's second law before
they have a name for it. This is the most pedagogically loaded material in the project.

### The loop advances on evidence, never on an answer
`LearningLoop` + `Evidence`: worlds record what the learner *did*, and gates are predicates over
that. There is no button meaning "I understand" and no quiz. The gate that matters most is
Explore → Discover, which waits for the learner to have produced **both a bound and an unbound
path** — once they have personally straddled the boundary, they are ready to be shown it.

Stages never regress. A learner who does something naive after reaching Formalize has produced a
*misconception*, recorded separately, which renders as an unstable shivering structure rather than
as lost progress.

### Materials carry their own studio lighting
`PrismCeramic` has a two-light world-space rig baked in and reads no scene lights; the scene has no
lights at all. Every scene here is generated from code and cannot be inspected before it ships, and
a material whose look depends on correctly configured scene lighting is a material that will one day
render flat grey on a headset with nobody around to notice.

### Formalize is spatial, not textual
There is **no text anywhere in the build**. Formalize draws the complete analytic conic *before* the
moon has traversed it, marks the apsides, and shows vis-viva as lengths that must balance. This is
closer to the brief's "equations as spatial constraints" than a panel of glyphs would be, and it
sidesteps three known TextMeshPro traps (world-space sizing `fontSize = size*1000` with
`localScale = 0.01`; pivot must follow alignment; LiberationSans SDF has no arrows, checkmarks or
box-drawing glyphs — ASCII only). When text is eventually added, those three apply.

### Atrium ↔ world is a scale transition in one scene
No scene loading. The concept the learner holds expands around them while the constellation rushes
past. Both places exist at all times, so there is no loading screen and **no way for a build to
become a one-way door** into a world with no route home. Leaving is both grips held 1.4 s *and* the
B/Y button — a learner who wants out and cannot find the way out is the worst failure available.

Passthrough follows the same logic: the atrium is white VR, a table-top simulation belongs in the
learner's real room, so entering a world dissolves the white space into passthrough.

---

## Layout

```
Assets/Prism/
  Aesthetic/     8 shaders + Include/PrismOptics.hlsl (the optical and colour law),
                 PrismMesh (icosphere/disc/tube/arrow), PrismMaterials (semantic factory),
                 PrismTrailRibbon
  Core/          PrismPalette, KnowledgeKind (the ten states of matter), ConceptDefinition,
                 ConceptGraph, KnowledgeState (learner profile), LearningLoop + Evidence,
                 PrismScale (the mu derivation), PrismSession (owns the transition)
  Atrium/        KnowledgeAtrium, ConstellationNode
  Worlds/OrbitalMechanics/
                 OrbitalSim (leapfrog), OrbitElements (conics), OrbitalWorld, OrbitalChallenge
  Interaction/   PrismHands
  Companion/     PrismCompanion (particle attention, one draw call), PrismVoice (procedural audio)
  Content/       17 generated ConceptDefinition assets + ConceptGraph
  Editor/        PrismConfigure, PrismConceptSeed, PrismSceneBuilder + PrismVerify, PrismBuild,
                 PrismPhysicsTest, PrismShaderTest, PrismLogTap
  Scenes/Prism.unity   generated; never hand-edit, it is rebuilt from scratch
```

`Logs/Prism.log` mirrors errors, warnings and anything tagged `[PRISM`. Read it instead of hunting
the console.

---

## The third device test: "no content"

Report: *"opens fine, but no content, not catchy, just simple."* Correct on all counts, and the
cause was structural rather than cosmetic.

**Sixteen of the seventeen concepts did nothing.** Only `orbital-mechanics` had a world behind it.
Every other concept could be pointed at, named, held — and then the interaction ended. A
constellation of labels is not content, however good the labels are.

**And they were specks.** Concept radii (3-9 cm) were chosen for something held at arm's length, but
the constellation is authored out to 2.85 m, where a 4 cm sphere subtends **1.4 degrees**. Nodes now
scale with distance to hold a legible apparent size (`ConstellationNode`, clamped 1x to 3.2x).

### Pocket demonstrations
Every concept now blooms a live, manipulable phenomenon when held. Not a whole world — the smallest
honest version of the brief's claim that a concept is an inhabitable phenomenon.

**The grammar, obeyed by all seventeen:** the HOLDING hand carries the phenomenon, the FREE hand
operates it. Learn one and you have learned all of them, and it uses the body rather than a menu.

Every demonstration runs **real rules**, never an animation of the rules:

| concept | what the learner does |
|---|---|
| gravity | dents a real potential well with their hand; a marble orbits the dent |
| inverse-square | moves a patch out and watches the ray count fall as 1/r² |
| conic-sections | tilts a plane through a cone; the section opens past the half-angle |
| energy-conservation | drops a bead; the total-energy bar does not move |
| angular-momentum | pulls the string in and it speeds up, because r·v is held exactly |
| kepler-laws | watches wedges of wildly different shape have identical area |
| escape-velocity | raises a hand until the path stops coming back |
| vectors | drags arrowheads; tip-to-tail sum redraws |
| periodic-motion | a 15-bob pendulum wave, each bob solving its own equation |
| why-orbits-persist | Newton's cannonball — it IS falling, it keeps missing |
| waves | shakes a string; the real wave equation propagates and reflects |
| rhythm | taps a tempo and the ring locks to it |
| tides | drags the moon; both bulges follow, from the differential field |
| symmetry | rotates until it clicks, discovering the group order by feel |
| probability | pinches to sample; a histogram converges on a hidden distribution |
| feedback | sets the loop gain and finds the boundary at exactly 1 |
| orbital-mechanics | a miniature preview of the world it opens |

A concept with no entry in `ConceptDemoRegistry` gets `LatentDemo`, which responds to the hand but
does not pretend to teach. **An honest gap beats a decorative animation implying a lesson that is not
there** — and `PrismVerify` now fails if any concept is inert, which is the check that would have
caught this whole class of emptiness.

### The constellation had no lines in it
Twenty-three relationships were authored and **zero were drawn**, because links were hidden until
both ends reached 0.15 mastery — which on a fresh profile is none of them. A new learner saw
seventeen unconnected dots, which throws away the product's entire visual thesis.

The brief says the constellation "becomes brighter and more interconnected" with progress. That is a
statement about BRIGHTNESS, not about existence. All links are now drawn from the first second, faint,
and brighten as both ends are learned. A relationship you cannot yet use is still a relationship.

### Ambient sound
`PrismAmbience` synthesises wind — filtered noise with two incommensurate swells so the loop never
audibly repeats. Added because a silent landscape reads as a picture and the same landscape with
moving air reads as somewhere you are standing. Cheapest large gain in presence available.

### Allocation discipline in the demonstrations
Per-frame `new List<Vector3>` for a two-point string looks harmless and is not: the pendulum wave
alone would have allocated fifteen lists per frame, about a thousand a second, which shows up as
periodic hitching rather than as a low frame rate. `ConceptDemo.Segment()` reuses one buffer, and the
wave demo rotates three float arrays instead of allocating a new one per step.

### Gradle daemons are shared across projects on this machine
A build failed with `Gradle build daemon has been stopped: stop command received` and nine stopped
daemons. Nothing was wrong with the code — another project's Unity build on the same machine had
killed the shared daemons in `~/.gradle`. Retrying is the fix. Worth recognising, because the message
looks like a project fault and is not.

## Versioning

`PrismVersion.Semantic` in `Assets/Prism/Core/PrismVersion.cs` is the single declared source of
truth. Bump it by hand — it is a statement about the product, not a counter.

- **Configure** applies it to `PlayerSettings.bundleVersion`, so the project always declares the
  version its code believes it is. `PrismVerify` fails if the two ever disagree.
- **Build** increments `PlayerSettings.Android.bundleVersionCode` (never reused — the store rejects
  a code that does not strictly increase, and a reused code makes two builds indistinguishable on a
  device), writes the stamp, and names the APK `PRISM-<semantic>+<code>-<codename>.apk` so builds
  cannot silently overwrite each other.
- **The device can identify itself.** First diagnostic line at boot:

  ```
  [PRISM-DIAG] PRISM 0.4.0+13 eleven-worlds (dev) built 2026-07-30 20:14:02Z
               | 11 worlds, 65 concepts, 50 demonstrations
  ```

This exists because of a real failure: every APK before it declared `versionCode 1 / versionName 1.0`
and overwrote `PRISM.apk`, so a report of "it still does the white thing" could have meant any of six
builds and there was no way to tell which.

**The stamp is a Resources TEXT ASSET, not generated C#.** A generated `.cs` cannot be compiled and
then built in the same batch-mode invocation — the domain does not reload mid-run — so a code stamp
would always describe the PREVIOUS build, which is worse than none.

The channel is honest: `dev` while debug-signed, `release` only with a real keystore configured.

---

## Building worlds in parallel

Eleven worlds exist; ten were built simultaneously by independent agents. The arrangement that made
that safe is worth keeping, because the naive version of it destroys the project.

**One process may run Unity. Ever.** Two Unity processes against this project core-dump. Module
authors never build, never test, never run the editor — they write code, and the coordinator
compiles. This is non-negotiable and must be stated in every module brief.

**Registration is by existence, not by list.** If ten authors each had to add a line to a shared
registry, that file is the one guaranteed merge conflict in the whole arrangement. Instead:

| thing | how it registers |
|---|---|
| a world | subclass `PrismWorldBase` — `WorldRegistry` finds it by reflection |
| its concepts | `override Concepts` — the seeder merges them |
| its shaders | `override Shaders` — unioned into Always Included |
| a pocket demo | `[ConceptDemoFor("concept-id")]` — the registry finds it by reflection |

Result across ten parallel modules: **zero shared-file edits, zero merge conflicts, and one compile
error in ~13,000 lines** (a `const` reached through an instance).

**Boundaries are folders.** Each module owns `Assets/Prism/Worlds/<Name>/` and may read anything but
write nothing else. Verified after the fact with a `find -newermt` sweep; nothing escaped.

**Give each module a distinct constellation wedge**, and assert what actually matters rather than
policing the convention — `PrismVerify` fails if any two concepts sit within 22 cm, because
"azimuth" has no code-enforced meaning and two authors can disagree by a reflection.

### What agents without a compiler are good at
Two independently invented the same technique: **port the numerical core to Python, validate there,
then write the C#.** It caught a climate model whose default state sat in an unstable band and would
have run away untouched, and an explicit Euler integrator that went NaN under learner input. Neither
was a compile error; both would have shipped looking like physics. Ask for this explicitly.

### Bugs a coordinator catches that isolated authors cannot
Each of these was reported by one module and was live in the shared reference all ten were copying:

- **Evidence re-entrancy** — `Evidence.Record()` re-entered the loop mid-call-stack, so a stage's
  setup ran and was then clobbered by the caller's next line. The Explain stage silently never ran.
  Fixed structurally: gates are now evaluated only in `LearningLoop.Tick()`.
- **Stale reveals** — stage is persisted in the profile, but reveals fired only on the `StageEntered`
  edge. **A learner who saved at Apply and came back got a dead world.** Reveals now self-derive
  from `Loop.Stage` every frame.
- **Hot-path allocation** — `PrismMesh.Tube` allocated four Lists per call and is called every frame
  by every curve and all 23 constellation links: ~8,300 allocations/second. Now uses static scratch
  buffers.

The pattern: **a module can see a problem it cannot fix, because the fix lives outside its
boundary.** Collecting those reports is most of the coordinator's value.

## Rendering offscreen — this machine CAN produce frames

For most of this project's life every claim was "it compiles" or "the numbers are right", because
`-nographics` cannot draw. That was a false limit: the box has **Xvfb and a GPU at /dev/dri/card0**.

```bash
xvfb-run -a -s "-screen 0 1600x900x24" \
  Unity -batchmode -projectPath . \
        -executeMethod Prism.EditorTools.PrismShots.CaptureFromCommandLine
```

**Omit `-nographics`** — that flag is the thing that blocks rendering; Xvfb supplies the display.
`PrismShots` writes PNGs plus `Shots/_scene-dump.txt`, a factual dump of what is actually in the
scene. Read the dump before theorising about a bad frame; it answers in one line what an hour of
reasoning gets wrong.

Two traps it works around: entering play mode reloads the domain (wiping the capture state machine —
disable domain reload), and scene content does not exist until it RUNS (the atrium builds in Start,
worlds build lazily on Enter, so nothing can be shot from edit mode).

### Producing one image found three bugs every check had passed

**1. `GetComponent<T>() ?? AddComponent<T>()` NEVER ADDS THE COMPONENT.**
UnityEngine.Object overloads `==` so a missing component compares equal to null — but `??` uses
*reference* equality and bypasses that overload entirely. The `??` never fires, the fake-null is
returned, and the next line throws `MissingComponentException`. Every constellation node threw on its
first line, `BuildConstellation` aborted, and **the atrium built ZERO concepts**. Five sites.
Use `TryGetComponent` — it has no such ambiguity. Never `??` with a Unity object.

**2. An array cleared to the wrong bound.** Evolution Engine cleared its 14-entry histogram using the
96-entry population capacity: 82 elements past the end, every frame, 18,401 exceptions in one run.

**3. Placement silently used the wrong camera.** Worlds and the atrium hold a `Head` reference and
only fall back to `FindHeadCamera()` when it is null. It was not null — it was the real head camera,
disabled at the origin — so every world placed itself at (0,0,0) and never appeared in any framing.

None were compile errors. All three passed every check in the project.

### `pkill -f` kills the shell that runs it
Same self-matching trap as `pgrep -f`, and it cost two silent render runs: `pkill -f "PrismShots"`
matched the bash process executing that very command. **Kill by PID**, never by a pattern the command
line contains:

```bash
for pid in $(ps -eo pid,cmd --no-headers | grep "[E]ditor/Unity" | grep -v bin/bash | awk '{print $1}');
do kill -9 "$pid"; done
```

Also: do not `rm -rf` the output directory while a previous run may still be writing into it — that
destroyed a completed capture and made it look like the render had failed.

## RESUME HERE

Three device tests have happened. Each one found something no automated check had caught, and each
one is now covered by a check. That pattern is the most useful thing in this file: **the failures
here are not bugs in the code so much as invariants nobody had written down.**

| device report | actual cause | invariant now asserted |
|---|---|---|
| "white blocks, no interaction" | selection radius 0.13 m vs concepts at 0.85–2.85 m | reach range covers the furthest concept |
| (same) | pinch read from an API that returns 0 for tracked hands | OVRHand + OVRSkeleton sources exist |
| (same) | every concept desaturated to white at mastery 0 | — (design fixed: hue and structure are separate channels) |
| "no text, white VR" | TMP font never imported; void has no depth cues | TMP default font resolves; environment present |
| (found by review) | camera clipped at 200 m, terrain reaches 900 m | far clip >= terrain extent |

**First action next session: look at it, and read the diagnostics rather than guessing.**

```bash
adb logcat -s Unity | grep -E "PRISM-DIAG|PRISM-SHADER"
```

Most likely to be wrong on this build, in order:

1. **Frame rate.** This is now the big unknown. `Prism/Gel`'s 6-step interior march, the atmospheric
   scattering in both sky and ground (the ground evaluates `Prism_SkyColour` three times per
   fragment), 17 two-pass seeds and the companion are all on screen at once. If it is slow, the
   ground's aerial-perspective calls are the first thing to hoist into a per-vertex term, and
   `PRISM_GEL_STEPS` is the second.
2. **Terrain readability.** Heights, plateau radius and mist density were all reasoned about, never
   seen. The valley may be too deep, the ranges too far to read, or the mist too thick.
3. **Whether the constellation still reads against a landscape.** `_PrismBackdropLuma = 0.62` is a
   single global compromise between bright sky and dark ground. If concepts wash out against the sky
   or vanish against the hills, the honest fix is a per-fragment backdrop estimate rather than
   retuning the constant.
4. **Label legibility.** Size, the underlay contrast, and the distance-scaling clamp are guesses.
5. **Whether the crystallisation reads as crystal.** Still never observed, and still the single most
   important effect in the product.

Known gaps, deliberately not built:
- **No spoken companion.** `PrismCompanion.Intent` is the seam — it already decides what should be
  communicated and when, and logs it.
- **No MR room understanding.** Passthrough is a toggle (default off); there is no plane detection
  and no room mesh. MRUK is the seam.
- **No water.** The valley would carry a lake well and it is cheap (analytic sky reflection, no
  render texture), but it was cut to keep this round reviewable.
- **No Addressables**, no world downloads, no second world, no multiplayer, no teacher mode.
- 16 of the 17 concepts have no world behind them; reaching for one logs an honest "not in this
  build yet".
- The APK still requests `android.permission.INTERNET` — see the open issue above.
