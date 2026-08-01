# PRISM

A spatial universe of living knowledge for Meta Quest 3 — where a learner does not watch a lesson
but enters a concept, takes hold of it, and finds out what it does.

Full product brief: [`docs/VISION.md`](docs/VISION.md).
Engineering notes and hard-won environment facts: [`CLAUDE.md`](CLAUDE.md).

---

## This milestone

**Eleven Concept Worlds, 65 concepts, 55 pocket demonstrations.** Ten of the worlds were built
simultaneously by independent agents against a shared contract; see `CLAUDE.md` for how that was
partitioned so it could not collide, and what it cost.

**The Knowledge Atrium** is the home, and it is not a menu. The learner stands on a high plateau at
dawn — level ground underfoot, a mist-filled valley beyond it, distant ranges, and a sky computed
from real Rayleigh and Mie scattering — with their own knowledge constellation floating around them.

(It began as the brief's "immense calm white space". That failed in a headset: with no horizon, no
ground and no aerial perspective, there is nothing to measure anything against, so every concept read
as a flat blob at an unknowable distance. The landscape is entirely procedural — no textures, no
models, nothing downloaded.)

Reaching toward a concept names it and summons it to the hand; holding it also shows what you should
be able to *do* with it. Nothing else is labelled, and the resting world has no text in it at all.

**Every concept is a live thing you can operate.** Holding one blooms the phenomenon it stands for,
and the grammar is the same for all seventeen: *the holding hand carries it, the free hand works it.*
Each runs real rules rather than an animation of them — dent a real gravitational potential and watch
a marble orbit the dent; pull a string in and feel the mass speed up because r·v is conserved exactly;
tilt a plane through a cone until the ellipse opens into a hyperbola; shake a string and watch the
wave equation reflect off the far end; fire Newton's cannonball faster and faster until it stops
coming down.
Bringing two concepts together proposes that they are related — and the space answers physically: a
real relationship resolves into a perfect fifth and a coloured current, a false one produces a slow
beating dissonance and a tube that shivers. Holding a concept to your chest takes you inside it.

Every concept's appearance is derived from the learner's actual understanding, across two independent
channels: **hue says which domain it belongs to**, and **structure says how well you know it**.
Mastery does not fill a progress bar; it **crystallises the geometry**. An unresolved idea is smooth,
restless, and literally missing parts of its own surface — but it is fully coloured from the first
moment, because you cannot learn from something you cannot see. A mastered one is a hard faceted
crystal whose edges have emerged on their own. A well-understood concept with no connections stays
visibly dim, because it is not finished. One node is a *question* — "why doesn't the Moon fall
down?" — and it renders as an iridescent void that bends the concepts near it out of shape.

**Orbital Mechanics** is the world. A planet floats above the table and a single moon hangs beside
it, unlabelled. Let go and it falls. That is the whole of the first stage.

Then the learner throws moons, for as long as they like, with no instructions. Trails appear, and
they are coloured **by speed** — so an eccentric orbit draws itself violet and hurried at perigee
and cyan and unhurried at apogee, and Kepler's second law is visible long before it has a name. Some
throws come back. Some leave forever.

Once the learner has produced **both** a bound and an unbound path — once they have found the
boundary themselves — the invisible arrives: the gravitational potential appears as contours, which
are real equipotentials of the actual bodies in the scene, and each moon grows a velocity arrow that
can be taken hold of by its head. Only after that does anything get named, and even then it is named
spatially: the complete conic is drawn before the moon has finished tracing it.

The numbers underneath are not decorative. `mu` is derived rather than chosen so that a circular
orbit at 20 cm takes 6 seconds, which puts circular speed at 0.209 m/s and escape speed at
0.296 m/s. **The difference between putting a moon into orbit and throwing it away forever is
8.7 cm/s of wrist.** Throws are one to one. The learner's own arm is the instrument.

---

## Build

Everything is generated from code — scenes, meshes, materials, the concept graph. There are no
prefabs and no imported models.

```bash
UNITY=~/Unity/Hub/Editor/6000.5.5f1/Editor/Unity

# configure, seed the concept graph, generate the scene, verify wiring
$UNITY -batchmode -nographics -projectPath . \
       -executeMethod Prism.EditorTools.PrismBuild.BootstrapFromCommandLine

# APK -> Build/PRISM-<version>+<code>-<codename>.apk   (override with PRISM_APK_OUT)
# versionCode increments every build and is never reused, so builds cannot overwrite
# each other and any device log identifies exactly which one is running.
$UNITY -batchmode -nographics -projectPath . \
       -executeMethod Prism.EditorTools.PrismBuild.BuildFromCommandLine
```

Or from the editor: **PRISM → 1. Configure Project / 2. Seed Concept Graph / 3. Build Scene /
4. Build APK**.

`adb` ships with the editor:

```bash
ADB=~/Unity/Hub/Editor/6000.5.5f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb
$ADB install -r "$(ls -t Build/PRISM-*.apk | head -1)"

# which build is on the headset right now:
$ADB logcat -s Unity | grep PRISM-DIAG | head -2
```

## Verify

Three independent checks, all runnable headlessly:

```bash
$UNITY ... -executeMethod Prism.EditorTools.PrismPhysicsTest.RunFromCommandLine   # exits 1 on failure
$UNITY ... -executeMethod Prism.EditorTools.PrismShaderTest.RunFromCommandLine    # exits 1 on failure
$UNITY ... -executeMethod Prism.EditorTools.PrismVerify.VerifyFromCommandLine
```

`PrismPhysicsTest` is the one that matters most. It asserts the simulation against closed-form
Kepler solutions — that a circular orbit is still circular ten periods later, that measured periods
match `T² ∝ a³`, that eccentricity matches the analytic value for a tangential throw, that identical
throws are bitwise reproducible, and that the orbit/escape boundary stays a feelable gap of wrist
speed. Every claim the world makes to a learner is downstream of these numbers.

## Controls

Controllers and hand tracking both work, simultaneously — put a controller down mid-lesson and
nothing notices.

| | |
|---|---|
| point at a concept | a bead brightens and the concept swells: that is the one you would take |
| pinch or grip while pointing | it comes to your hand, and its phenomenon blooms above it |
| move your **free** hand | operate the phenomenon — every concept responds |
| bring two held concepts together | propose a relationship — consonance if it holds, beating dissonance if it does not |
| hold a concept to your chest | enter its world |
| pinch / grip near a moon (or point at one) | pick it up; release to throw |
| pinch the head of a velocity arrow | change speed and direction directly |
| both grips held 1.4 s, or **B/Y** | return to the atrium |

A concept can be reached for at **any distance** inside an 11-degree cone — the constellation is
authored from 0.85 m to 2.85 m away, so touch alone would leave everything permanently out of reach.
Every material also responds to a hand approaching it, via `_PrismHandL` / `_PrismHandR` globals.

## Status

Compiles clean. Physics verified numerically (25/25). All 10 shaders compile for Vulkan. Scene
verification covers wiring, reachability, text resources, environment, camera far clip, and
**that every one of the 17 concepts has a live demonstration** — all passing.

Two device tests so far, both of which found things no automated check had caught. The first: nothing
was reachable, hand pinch was read from an API that always returns 0 for tracked hands, and every
concept rendered white-on-white. The second: the white void gave the eye nothing to measure against,
and there was no text at all. The third: sixteen of seventeen concepts did nothing when held, and at
2.85 m a concept subtended 1.4 degrees — a speck. All fixed; `PrismVerify` now asserts reachability,
font availability, environment presence, far clip, and demonstration coverage, so none of them can
recur silently. See the top of `CLAUDE.md` for the full account of each.

If something still looks wrong on device, get specifics cheaply:

```bash
adb logcat -s Unity | grep PRISM-DIAG
```

That reports head pose, per-hand tracked/pinch/grip/fingertips/reach, node count, distance to the
nearest concept, and the current loop stage — once per second.
