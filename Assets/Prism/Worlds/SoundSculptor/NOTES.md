# Sound Sculptor — build notes

## What this is

Two vibrating strings the learner shapes with their hands. One sounds from the moment the world
is entered; a second can be pulled out of a cradle and moved independently. Length sets pitch
(`f = v / 2L`, mode 1); when the two lengths land on a simple ratio, the tones genuinely lock —
because they are genuinely summed, harmonic by harmonic, in the audio mixer — and a light between
the strings shows the real beat frequency slowing to a stop as that happens. The whole arc:

- **Wonder** — one string, one tone, a glowing spindle. Nothing labelled, nothing else on screen.
- **Explore** — grab the two cyan end-handles and stretch; pull the gold seed out of its cradle to
  summon a second string. With only the ear to go on, find a pair that locks and a pair that beats.
- **Discover** — gated on having found BOTH (the exemplar straddle). A pulsing light appears
  between the strings, beating at the real coincidence frequency; go find the octave and the fifth
  specifically, now that it can be seen as well as heard.
- **Formalize** — a label names the ratio and shows both frequencies and the beat in Hz.
- **Apply** — a ghost pair of markers shows a target length; tune a string onto it by ear and hand.
- **Explain** — a token carried into a "calm" or "beating" zone commits a prediction *before* the
  world moves a string to the trial length and lets it sound.
- **Create** — a crystal sampled against a sounding string captures its frequency into a slot; two
  or more slots let a play-mote stagger the notes in and hold them ringing together.
- **Connect** — mastery confirmed; the constellation gains five new, linked stars.

## Files

- `SoundScale.cs` — the physics and arithmetic: wave speed (derived, not chosen, same method as
  `PrismScale.Mu`), length/frequency conversion, pitch class, the simple-ratio table, the
  coincidence/beat calculation, and the tuning-assist magnet.
- `SoundSynth.cs` — procedural audio: one shared looping AudioClip built once, and `ToneVoice`, a
  thin wrapper that retunes it continuously via `AudioSource.pitch` and eases its volume.
- `SoundString.cs` — one string's state (two endpoints, mode), its length-constraint/magnetism
  step, and its dynamic tube mesh (the standing-wave envelope).
- `SoundSculptorWorld.cs` — the `PrismWorldBase` subclass: evidence keys, the loop, hand/handle
  interaction, the confluence light, the label, and the five `ConceptSpec`s.
- `SoundSculptorChallenge.cs` — Apply (tune-to-ghost), Explain (predict-then-reveal), and Create
  (capture-and-play), mirroring `OrbitalChallenge`'s separation from the main world file.
- No shaders. Every material is `Prism/Trail`, `Prism/Ceramic`, `Prism/Seed` or `Prism/Volumetric`
  via `PrismMaterials`, so `Shaders` is declared empty on purpose.

## Colour encoding (stated once, here, as required)

**Hue = pitch class.** `SoundScale.PitchClass(f) = frac(log2(f / 220 Hz))`, mapped through
`PrismPalette.Spectral`. This wraps every octave onto the same point of the ramp — a frequency and
its double, quadruple, or half all render identically. It was chosen over hue = raw frequency
specifically because pitch class, not frequency, is the thing that is perceptually invariant under
"same note, different register" — an octave doubling is inaudible as a change of *kind*, and the
colour says so before anyone is told why. The fifth (3:2 above any root) is therefore always the
same hue too, wherever it occurs. `Prism/Trail`'s speed channel is repurposed to carry this value
(`_SpeedLo=0, _SpeedHi=1`, `uv1.x = pitch class`) rather than writing a new shader.

The confluence light's hue is the pitch class of the *average* of the two frequencies, so it reads
as "between" the two strings; the chord crystals in Create each take the pitch class of whatever
they captured.

## What is simulated honestly

- **Additive synthesis, real partials, no baked audio.** `SoundSynth.LoopingTone` sums three real
  sine partials (1x/2x/3x of a base frequency) into one `AudioClip.Create` buffer, exactly
  `PrismVoice.Tone`'s method. The one difference is that this clip **loops** rather than being a
  one-shot with a baked envelope, because a string's pitch must move continuously while the
  learner's hand moves it. Retuning is real resampling — `AudioSource.pitch` — the same physical
  operation as a record played at the wrong speed, not an approximated pitch shift, and because
  every partial is an exact integer multiple of the base frequency, changing the playback rate
  keeps them in that exact ratio at any pitch. Attack/release live on `AudioSource.volume`,
  eased every frame (`1 - e^-5dt`), which is the loop-compatible equivalent of PrismVoice's
  raised-cosine — nothing here has a sharp attack, including a string only just summoned.
- **Beating is real summation, never faked.** Two `ToneVoice`s are two real `AudioSource`s. Unity's
  mixer sums their waveforms before the DAC; if the ratio is 3:2, the partials genuinely coincide
  and the pair genuinely locks, because harmonic coincidence is genuinely what is playing. Nothing
  computes "these should beat" and layers a tremolo on top.
- **The beat number itself is a real physical quantity.** `SoundScale.NearestCoincidence` finds the
  candidate ratio p:q whose coinciding harmonics (`q * fHigh` against `p * fLow`) are closest, and
  returns their real difference in Hz. At p=q=1 this is exactly the textbook "beat frequency is the
  difference of the two frequencies" (the Formalize-stage claim the brief asks for); the general
  case is the same idea applied to whichever pair of harmonics is actually nearest. The confluence
  light's pulse rate **is** this number (capped at 12 Hz for legibility) — not an animation
  standing in for it. When a ratio is exact, the beat is exactly zero and the light stops, honestly.
- **Standing wave modes are real.** The tube's radius at each point along its axis is
  `A * |sin(mode * pi * x / L)|` — the actual envelope a fixed-fixed string sweeps, not a
  decoration. n lobes for mode n, nodes (near-zero radius) at both fixed ends and at every internal
  node in between; counting the lobes is reading off the mode number, and the SAME (length, mode)
  pair drives both this shape and `SoundScale.FrequencyOf`, so they cannot disagree.
- **Length and frequency are exactly, not approximately, inverse.** `SoundString.EndA`/`EndB` are
  the single source of truth; both the mesh and the audio pitch are derived from them fresh every
  frame. The tuning magnet (below) moves the endpoints, never the frequency directly, so whatever
  is drawn is always exactly what is heard.
- **No numerical integrator, so no energy-drift question applies.** Everything here is closed-form:
  the envelope is an analytic sine, hand-following is direct 1:1 positioning (like Orbital's
  carried moon), and the length constraint is an algebraic clamp/lerp, not an ODE being stepped.
  There is nothing to integrate and so nothing that can secretly bleed or gain energy — the
  contract's symplectic-integrator concern doesn't arise here by construction, not by omission.

## What is approximated, and why

- **Visual amplitude is stylised.** A real string's displacement at these lengths and tensions is
  sub-millimetre — invisible. `SoundString.Amplitude` (1.7 cm) scales the envelope for legibility,
  exactly the way `OrbitalWorld` draws a 4 cm planet rather than a to-scale one. The *rule*
  (radius proportional to `|sin(n*pi*x/L)|`) is exact; only the constant multiplying it is a
  display choice.
- **The envelope is shown, not an instant of motion.** A real standing wave at 100-800 Hz cycles
  far faster than any 72-90 Hz headset can honestly redraw — animating "one instant" at a
  human-visible rate would either alias into nonsense or require slowing time down and silently
  implying a false frequency. The envelope (both extremes the string sweeps over one full cycle) is
  a real, well-defined mathematical object, and it is also what the eye actually perceives from a
  real plucked string — persistence of vision integrates the fast motion into exactly this blur.
  Showing it directly is the honest choice, not a simplification of a "real" animated version.
- **Every string shares one wave speed.** Two strings of different tension or material would
  legitimately have different wave speeds, and I did not model that. Sharing `SoundScale.WaveSpeed`
  is what makes a length ratio and a frequency ratio exact inverses of each other with nothing else
  in play — a learner who eyeballs "these two lengths look like 2:1" has already predicted the
  pitch relationship correctly, which is the whole teaching mechanism. Documented rather than
  hidden: see the comment on `WaveSpeed`.
- **Mode is general in the formula, fixed at 1 in the interaction.** `SoundScale.FrequencyOf` and
  `SoundString.RebuildMesh` both take a mode number and are correct for any positive integer, but
  nothing in the interaction currently lets the learner excite mode 2 or 3 on a single string (e.g.
  by damping its centre, the real technique for a natural harmonic). This was a scope cut, not a
  shortcut in the physics that IS exposed — everything the learner can actually do remains exactly
  governed by n=1. A natural follow-on feature; see below.
- **The tuning magnet moves geometry, not the sound.** Hand-tracking jitter of a few millimetres
  translates to several Hz of frequency jitter near a target interval (worked through in the
  comment on `SoundScale.MagnetLength`) — well outside the ~1.5 Hz lock window, which would make
  finding an exact lock by raw freehand positioning nearly impossible. Rather than loosen the lock
  window until "locked" stops meaning anything, `MagnetLength` gives a soft pull (eased, not
  snapped) toward the nearest simple-ratio length while a string is actively being stretched. It
  only ever adjusts the endpoints — the same ones the mesh and the audio both read — so it never
  makes the demonstration less honest, only less frustrating to actually perform.

## Safety / volume

Both strings loop continuously for as long as the learner is in the world, which is a different
risk profile from PrismVoice's brief stingers, so gains were set for sustained rather than peak
exposure: harmonic gains sum to 0.65 (down from an initial 0.98 draft), `VoiceGain` is 0.13, and
chord notes during Create (up to five ringing at once) are individually quieter still at 0.08.
Worst-case simultaneous-peak across everything running at once stays under ~0.45, comfortably below
clipping. Every voice fades in and out over a fraction of a second; nothing starts or stops
abruptly. See the comment on `SoundSculptorWorld.VoiceGain` for the arithmetic.

## Concepts declared

`sound-sculptor` (door, System/ArtAndMusic) plus `standing-waves` (System/MatterAndEnergy),
`harmonic-ratio` (Equation/Mathematics), `timbre` (Fact/ArtAndMusic), `octave-equivalence`
(Fact/ArtAndMusic). Cross-domain links to existing concepts, each a genuine shared structure rather
than a name-alike: `standing-waves` → `waves` (Composes) and → `periodic-motion` (Analogy);
`harmonic-ratio` → `rhythm` (Analogy — ratio arithmetic in time is the same mathematics as ratio
arithmetic in pitch) and → `periodic-motion` (Constrains); `octave-equivalence` → `symmetry`
(Analogy — a repeating pattern under a transformation that leaves pitch class invariant is
structurally the same idea as any other symmetry). Placed at azimuth 258-281°, distance 3.3-5.1 m,
elevation -0.30..+0.35, inside the assigned wedge (252-288°, 3.2-5.5 m, ±0.5).

**Assumption flagged for the master:** I found no code-enforced azimuth convention (`ConceptSpec.Direction`
is consumed as a free `Vector3.normalized * distance` by `KnowledgeAtrium`, with nothing dictating
what "azimuth" means). I used the standard compass convention — 0° = world +Z, increasing clockwise
toward +X, matching Unity's own yaw rotation — on the assumption every world was assigned a wedge
in the same frame. Worth a quick constellation-wide visual check that the ten wedges actually tile
without collision, since I could not see the other nine agents' interpretations.

## Performance

No per-frame allocation: `SoundString.RebuildMesh` reuses four persistent `List<>` fields (the
`PrismTrailRibbon` pattern); `SoundSculptorChallenge`'s chord slots are a plain `ChordSlot[]` rather
than a `List<>` specifically so its fields can be mutated through the array indexer without a
copy. Two hands are serviced by two explicit calls rather than a `new[]{Left,Right}` per frame.
Draw calls: 2 string tubes + 6 handles + 1 confluence light, all always resident (9); up to 2 ghost
markers, 2 zones, 1 token, 5 chord slots, 1 capture crystal, 1 play-mote, each hidden outside its
own stage — worst case simultaneously visible stays under 20, well inside the ~40 budget. Nothing
here is a numerically-integrated system (see above), so there is no fixed-timestep requirement.

## Anything unfinished / possible follow-on

- **Mode 2/3 on a single string** (a natural-harmonic gesture — damp the centre, hear and see it
  jump an octave via its own second mode) would be a beautiful, low-risk addition tying `timbre`
  and `standing-waves` together more tightly, and the underlying math already supports it. Cut for
  scope, not difficulty.
- **Re-entry resets spatial layout, not progress.** Leaving and returning replaces both strings at
  their default positions (String1 back in its cradle, unsummoned) regardless of how far the
  learner had gotten, because `PlaceInFrontOfUser` re-centres the anchor on wherever they are now
  standing and content authored relative to the old anchor would otherwise be orphaned in space.
  Loop stage and evidence are untouched (they live in `Loop`/`Knowledge`, not touched by
  `ResetLayout`), and `SoundSculptorChallenge.ResetChallenges` re-derives which challenge should be
  showing from the current stage — but a learner who re-enters mid-Apply will need to re-summon the
  second string before the ghost target becomes completable again. Chord-slot crystals ARE
  preserved and simply re-anchored to the new position, since discarding a learner's built chord
  seemed worse than a small inconsistency with how the strings behave.
- **No haptic bump at the length clamp.** Hitting `SoundScale.MinLength`/`MaxLength` currently just
  stops the string shrinking/growing; a short `Hands.Buzz` there would make the wall felt, not just
  seen.
- **Shader property names** (`_Opacity`, `_HeadGain`, `_Density`, `_Growth`, `_Luminance`, `_Tint`,
  `_EdgeTint`, `_SpeedLo`/`_SpeedHi`) were all cross-checked against the actual `.shader` Properties
  blocks rather than guessed, but this was never run — a typo would fail silently (a no-op
  `SetFloat`/`SetColor`), not a compile error, so a quick in-headset look is worth it.

---

## Round 2 — pocket demonstrations (`SoundDemos.cs`)

Four `ConceptDemo`s, one per supporting concept, registered with `[ConceptDemoFor]` so
`ConceptDemoRegistry` finds them by reflection — no shared file touched. All four give this world's
four supporting concepts the thing no other pocket demo in the product currently has: continuous,
genuinely retunable sound, built with the exact same `SoundSynth`/`SoundScale` machinery as the full
world (same honesty guarantees, same volume discipline), just reused at pocket scale. The door
concept `sound-sculptor` intentionally has no entry here — see "Left for the master" below.

### StandingWavesDemo (`standing-waves`)
The free hand's height picks an integer mode 1-5. The glowing outline is the real envelope
`A * |sin(mode*pi*x/L)|`, traced as a closed lens shape (forward along the top, back along the
bottom) rather than an animated instant — the same honesty argument as the full world's
`SoundString.RebuildMesh` applies verbatim here, so it is restated rather than re-derived. The pitch
comes from `SoundScale.FrequencyOf(SoundScale.ReferenceLength, mode)` — the SAME function and
reference length the full world uses, so mode 1 here is exactly 220 Hz there too. Small marks under
the string count the mode redundantly (lobes, marks and pitch are three readings of one integer).
**Approximate:** the mode changes as a hard integer snap rather than an eased blend, deliberately —
an eased fractional "mode" would have to either lie about the shape (a non-integer sine is not a
real standing wave) or lie about the pitch (there is no `SoundScale.FrequencyOf` for mode 3.5). A
clean discrete response was judged more honest than a smooth but meaningless one.

### HarmonicRatioDemo (`harmonic-ratio`)
A fixed root tone (220 Hz) and a free-hand-dragged second tone, linked by a cord. A small light at
their midpoint pulses at the literal beat frequency from `SoundScale.NearestCoincidence` — the exact
calculation that gates the full world's Discover stage — and turns gold, brightens and steadies the
instant the ratio is genuinely simple; `Voice.Consonance` (the companion's own stinger) adds one
discrete confirmation on the moment of arrival, on top of the continuous real tones rather than
instead of them. Rests on the fifth (1.5) when not being operated, so picking the concept up
presents an already-resolved interval rather than silence or noise. Nothing approximate here beyond
what NOTES.md already says about the full world's identical coincidence physics.

### TimbreDemo (`timbre`)
One pitch, six independent partials. Harmonics 1-6 of a single 220 Hz root are six separate
`AudioSource`s all resampling the SAME pure-sine clip (see `SoundSynth`'s honesty note on why
resampling one real clip is not an approximation), each with its own hand-proximity-driven volume —
reach toward a coloured mote to bring that harmonic forward, pull away to remove it, continuously
and reversibly. Harmonic 1 carries a 0.55 floor so the pitch itself is always present; harmonics 2-6
are entirely the learner's to add, and each is capped lower than the last (`gain / (k+1)`) the way a
real instrument's overtones roll off, so no single overtone can swallow the fundamental. This is
the one demo built specifically to make an usually-invisible fact audible: that "which note" and
"which instrument" are carried by completely different information in the same sound, and a learner
can now separate them with one hand.

### OctaveEquivalenceDemo (`octave-equivalence`)
A bead climbs a three-turn helix under free-hand control; the same bead is drawn again, projected
straight down onto a flat ring, and the two are joined by a drop-line redrawn every frame. Height on
the helix keeps climbing without limit; angle on the helix (and hence position on the ring, and the
bead's own colour — its pitch class) repeats exactly once per turn, because one turn of the helix
**is** one octave (`angle = frac(log2(f/110)) * 2*pi`, the same `SoundScale.PitchClass` used
everywhere else in this world). The current pitch and its real octave above play together
throughout, so "same note, twice the frequency" is heard at the same instant the geometry is seen
collapsing onto one point on the ring — audible and visible collapse, together, as asked.

### Shared honesty/approximation notes
- All four reuse `SoundSynth.LoopingTone`/`ToneVoice` exactly as built for the full world: one small
  clip per demo, built once in `Build()` (never per-frame), retuned only via `AudioSource.pitch`.
  Real resampling, real summation in the mixer — nothing here fakes a beat, a lock, or a timbre.
- **Volume**: each demo's `AudioSource` target volume is scaled by the base class's own `Openness`
  (0 while blooming/closing, 1 once fully open), so sound fades with the visual bloom rather than
  popping. Per-voice gains (0.09-0.11) were chosen to sit at or below the full world's own
  `VoiceGain` (0.13, itself already revised down once — see above), since a pocket demo is closer to
  the learner's face and several concepts could plausibly be examined in the same session.
  Worst-case simultaneous peaks for each demo were checked by hand and stay under ~0.25.
- **No per-frame allocation.** `StandingWavesDemo` reuses one persistent `List<Vector3>` exactly as
  `WaveDemo` does; `TimbreDemo`'s six voices live in arrays built once in `Build()`; the others use
  only `Segment()`/`CurveView.Set(IList<Vector3>)`, the same per-frame-safe path
  `AngularMomentumDemo` and `WaveDemo` already use. Nothing here allocates inside `OnTick` beyond
  ordinary `Vector3`/`Color` structs, which are stack values, not GC allocations.
- **Left for the master:** the door concept `sound-sculptor` itself still falls back to `LatentDemo`
  when merely held (as opposed to carried to the chest, which opens the full world). I noticed
  `orbital-mechanics` has a bespoke `OrbitalMiniDemo` ("a miniature preview of the world it opens"),
  which suggests door concepts elsewhere DO get one — I did not build one here because the request
  was specific to the four supporting concepts and I did not want to guess past that scope, but it
  would be a natural, low-risk fifth addition to this same file if wanted (e.g. a small version of
  the confluence-light-between-two-strings moment, as the world's own "trailer").
