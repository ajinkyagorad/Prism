# PRISM — A Living Universe of Knowledge

> The product brief, as written. This is the document the implementation is measured against.
> Implementation notes, deviations and hard-won environment facts live in `../CLAUDE.md`.

A complete Unity-based educational XR suite for Meta Quest 3 where learners do not merely watch
lessons—they enter concepts, manipulate them, conduct experiments, become part of systems, and
construct their own explanations.

The sellable Quest Store APK would contain a polished core application, while downloadable
"Knowledge Worlds" expand it over time.

## Central idea: concepts become interactive realities

Instead of organizing education only as subjects and video courses, PRISM organizes knowledge into
connected conceptual worlds:

* Matter and energy
* Space and time
* Life and evolution
* Body and mind
* Machines and computation
* Earth and civilization
* Mathematics and patterns
* Language and communication
* Art, music and design
* Economics, society and decision-making

A learner can shrink inside an atom, pull on a geometric proof, stand within an electromagnetic
field, operate a cell, negotiate an ancient trade network, or physically walk through an algorithm.

## Three scales of learning

### 1. Pocket Laboratory — Mixed Reality

Educational objects appear in the learner's real room:

* A miniature solar system orbiting the table
* A virtual organism walking across the floor
* A beating heart floating in front of the learner
* Magnetic-field lines responding to real hand-held magnets
* Molecules assembled directly with both hands
* A small civilization growing across furniture
* Geometry attached to walls, floors and household objects

The room becomes the laboratory. Tables serve as simulation surfaces, walls become timelines, and
open floor space becomes a coordinate system.

### 2. Concept Worlds — Virtual Reality

The learner enters realities built specifically around each concept:

* Travel alongside a photon through optical systems
* Stand on curved spacetime around a black hole
* Enter the bloodstream and diagnose circulation problems
* Become an electrical signal moving through a neural network
* Operate a planetary climate system
* Walk through a functioning computer, from transistor to operating system
* Observe evolution across millions of years within minutes
* Experience mathematical transformations as changing spaces

These worlds should be stylized, readable and interactive—not just realistic museum scenes.

### 3. Creation Studio — MR and VR

Every completed concept gives the learner new building blocks. They can combine them to create:

* Machines
* Ecosystems
* Chemical processes
* Mathematical structures
* Simulated societies
* Space missions
* Interactive explanations
* Educational games for other learners

Understanding is demonstrated by making something that works.

## The learning loop

Every module follows a consistent immersive loop:

1. **Wonder** — encounter an unexplained phenomenon.
2. **Explore** — freely manipulate it without instructions.
3. **Discover** — reveal causal relationships through experimentation.
4. **Formalize** — introduce terminology, equations and diagrams only after intuition develops.
5. **Apply** — solve a practical or imaginative challenge.
6. **Explain** — teach the idea to an AI character or another learner.
7. **Create** — use the concept in a personal construction.
8. **Connect** — reveal relationships to other fields.

For example, a learner encountering orbital mechanics first throws miniature moons around a planet.
Trajectories and velocity vectors gradually appear. Only afterward are gravity, conic sections,
energy and equations introduced.

## Example Knowledge Worlds

| World                  | Immersive activity                                           | Concepts learned                              |
| ---------------------- | ------------------------------------------------------------ | --------------------------------------------- |
| Universe Foundry       | Construct stars and planetary systems                        | Gravity, fusion, elements, orbital mechanics  |
| Quantum Garden         | Grow probability clouds and perform interference experiments | Waves, uncertainty, measurement, quantization |
| Living Cell            | Operate a cell as a responsive factory                       | DNA, proteins, metabolism, membranes          |
| Body Expedition        | Enter and interact with functioning human systems            | Anatomy, physiology, health                   |
| Evolution Engine       | Change environments and observe life adapt                   | Selection, genetics, ecology                  |
| Mathematics of Motion  | Grab functions and walk through transformations              | Algebra, calculus, vectors, geometry          |
| Machine Cathedral      | Enter a working machine from gears to processors             | Mechanics, electronics, computing             |
| Algorithm City         | Redirect data through living computational structures        | Algorithms, AI, networks, logic               |
| Planet Guardian        | Control water, energy, ecosystems and cities                 | Climate, sustainability, systems thinking     |
| Civilization Simulator | Participate in societies across historical periods           | History, economics, politics, culture         |
| Language Worlds        | Communicate with characters through contextual situations    | Vocabulary, grammar, pronunciation            |
| Sound Sculptor         | Shape music as spatial geometry and moving fields            | Rhythm, harmony, acoustics, composition       |

## Interaction should teach the concept

The controls themselves communicate meaning:

* Grab a vector by its arrowhead to change magnitude and direction.
* Stretch a wavelength with both hands.
* Push time forward or backward using a spatial timeline.
* Separate mathematical variables and reconnect them.
* Follow energy by tracing glowing flows through a system.
* Place your hand inside a probability field to sample an outcome.
* Use your body as a coordinate frame.
* Conduct an orchestra by shaping sound volumes.
* Scale continuously from a galaxy to an atom.
* Pause any simulation, inspect causes and branch into alternative outcomes.

Traditional buttons and panels remain secondary. Whenever possible, the concept itself becomes the
interface.

## AI learning companion

A persistent AI guide appears in forms appropriate to each world—a robot, historical character,
organism, spacecraft intelligence or abstract holographic entity.

It can:

* Explain what the learner is currently observing
* Generate experiments from spoken questions
* Adjust vocabulary and difficulty
* Detect misconceptions from actions, not only answers
* Ask the learner to predict outcomes
* Transform verbal explanations into 3D demonstrations
* Generate personalized challenges
* Speak the learner's preferred language
* Play characters in history and language scenarios
* Help build custom educational worlds

The AI should not constantly lecture. It watches, asks useful questions and intervenes when the
learner is genuinely stuck.

## Knowledge Constellation

Progress is represented as a navigable three-dimensional constellation rather than a linear course
list.

Each star is a concept. Connections show how knowledge relates:

* Fractions connect to rhythm, ratios, chemistry and probability.
* Waves connect to music, light, quantum mechanics and communication.
* Feedback connects to biology, electronics, climate and economics.
* Symmetry connects to art, geometry, physics and molecular structure.

As learners progress, their constellation becomes brighter and more interconnected. Weak or isolated
concepts remain visually visible, making revision intuitive.

## Game structure

PRISM can feel like an exploratory science-fiction adventure:

* The Knowledge Universe has fractured.
* Each world contains unstable or incomplete systems.
* Learners restore them by understanding their underlying principles.
* Solving concepts unlocks powers rather than arbitrary cosmetics.
* Understanding optics grants control over light.
* Understanding vectors enables flight and trajectory construction.
* Understanding biology allows ecosystem creation.
* Understanding logic unlocks programmable companions.
* Cross-disciplinary knowledge unlocks the largest inventions.

There are no conventional enemies. Challenges come from unstable systems, mysteries, resource
constraints, competing objectives and incomplete understanding.

## Social learning

Optional multiplayer spaces could include:

* Collaborative laboratories
* Student–teacher shared worlds
* Debate and historical role-play
* Group engineering challenges
* Multiplayer anatomy and emergency simulations
* Shared planetary or civilization management
* Learner-created exhibitions
* A spatial classroom where participants manipulate the same model

A teacher can freeze the world, resize a concept, reveal layers, assign roles and observe each
learner's actions.

## Practical Unity product structure

The initial Quest Store release should not attempt to contain every subject. It should ship as a
strong platform with approximately six deep worlds:

1. Spatial mathematics
2. Physics playground
3. Living cell and human body
4. Solar system and orbital mechanics
5. Computing and AI
6. Earth systems and civilization

Unity architecture:

* OpenXR and Meta XR SDK
* Passthrough and room-mesh support
* Hand tracking and Touch Plus controllers
* Modular Addressables-based world downloads
* Shared interaction vocabulary across modules
* ScriptableObject-based concept definitions
* Deterministic simulation and replay system
* Local learner profile with optional cloud sync
* Performance-scalable visual layers
* Desktop simulator for development and teacher presentation
* Content-authoring toolkit for adding new lessons without rebuilding the core app

## Commercial model

A sensible product structure would be:

* **Base APK:** €19.99–€29.99 with the core six worlds
* **Expansion worlds:** €4.99–€14.99 each
* **Complete family edition:** one-time larger purchase
* **School edition:** device management, analytics, teacher mode and classroom licensing
* **Creator edition:** tools for universities, museums and educators to publish modules
* **Free MR discovery room:** limited experience that demonstrates the product before purchase

Avoid making the learner pay continuously merely to retain access to previously purchased
educational material. Subscriptions make more sense for regularly generated AI content,
institutional analytics and new monthly worlds.

## The strongest differentiator

The real innovation is not "school lessons inside VR." It is a universal spatial language for
knowledge:

* Objects represent entities.
* Fields represent invisible influence.
* Flows represent movement of matter, energy or information.
* Scale transitions connect levels of reality.
* Timelines expose causality.
* Branches reveal alternative outcomes.
* Construction demonstrates understanding.
* The learner's own body becomes part of reasoning.

That would make PRISM feel less like an educational app and more like a navigable, playable model of
reality—one capable of growing from childhood fundamentals to university-level concepts without
losing its intuitive immersive foundation.

---

# Aesthetic direction: Post-Digital Living Knowledge

If the suite teaches a new way, it cannot resemble a classroom, museum, textbook, or ordinary
sci-fi control room. Its aesthetic should express a new epistemic age—knowledge as something living,
spatial, interconnected and responsive.

Not dark cyberpunk, neon grids, floating rectangular panels or sterile school laboratories. Instead:

* Luminous white spatial environments
* Soft pastel spectral colours
* Volumetric, non-planar interfaces
* Translucent matter and internal depth
* Organic geometry mixed with precise scientific structures
* Knowledge appearing as fields, particles, organisms, flows and transformations
* Minimal text until requested
* Environments that evolve with understanding

It should feel as though the learner has entered an intelligent material universe—not opened an app.

## The visual language

| Conventional educational XR | New-age learning suite                          |
| --------------------------- | ----------------------------------------------- |
| Floating screens            | Knowledge physically inhabits the room          |
| Menus and buttons           | Grabbable constellations and spatial gestures   |
| Progress bars               | Growing knowledge organisms                     |
| Lesson lists                | Navigable concept universes                     |
| Multiple-choice quizzes     | Observable consequences and constructed answers |
| Teacher avatar              | Shape-shifting intelligent presence             |
| Glowing neon sci-fi         | Soft luminous matter with spectral colour       |
| Realistic museum models     | Reality augmented with invisible mechanisms     |
| Separate subjects           | Visually interconnected knowledge ecology       |

## The Knowledge Atrium

The home environment is an immense, calm, white space with no obvious walls or floor boundary.

Around the learner floats their personal knowledge constellation:

* Concepts resemble translucent living seeds.
* Mastered ideas become complex crystalline organisms.
* Connections flow between them like coloured currents.
* Unexplored domains exist as distant atmospheric structures.
* Recent learning gently orbits the learner.
* A concept can be summoned by reaching toward it.
* Pulling two concepts together reveals their relationship.

There is no conventional home screen. The learner stands inside their evolving understanding.

## Knowledge should have different states of matter

Different types of concepts receive distinct spatial behaviour:

* **Facts:** small stable crystals
* **Processes:** animated flowing ribbons
* **Systems:** responsive miniature worlds
* **Equations:** spatial constraints governing objects
* **Theories:** translucent fields connecting observations
* **Questions:** dark or iridescent voids that distort nearby knowledge
* **Uncertainty:** shimmering probability clouds
* **Misconceptions:** unstable structures that collapse under testing
* **Skills:** persistent tools attached to the learner's hands
* **Memories:** slowly orbiting sensory fragments

This creates an intuitive grammar. Learners recognise what kind of knowledge they are encountering
before reading its label.

## Interfaces grown from the subject

There should not be one generic UI applied everywhere.

* Biology interfaces grow like membranes and branching cells.
* Mathematics forms from clean geometric constraints.
* Physics appears through fields, vectors and causal trails.
* History unfolds as layered spatial timelines.
* Computing manifests as flowing information architecture.
* Language surrounds the learner as semantic objects and contextual scenes.
* Music becomes moving spatial harmonics.
* Psychology reshapes the environment according to perception and attention.

Despite these differences, common gestures remain consistent: pull closer, expand, dissect, rewind,
connect, simulate and release.

## A world that responds to comprehension

The environment changes as the learner understands:

1. A new concept initially appears mysterious and visually incomplete.
2. Experimentation reveals internal structure.
3. Relationships become visible as the learner discovers them.
4. Correct understanding stabilises its geometry.
5. Deep understanding makes it usable as a tool.
6. Connected understanding allows it to merge with other concepts.

A learner does not receive a "Correct!" message. The world becomes coherent.

## AI should not look like a chatbot

The AI companion has no permanent humanoid body. It is a distributed intelligence embedded
throughout the world:

* A group of particles following the learner's attention
* A temporary hand demonstrating an action
* A voice emerging from the concept being examined
* A soft field highlighting causal relationships
* A small creature during playful lessons
* A historical character when embodiment matters
* A geometric presence during mathematics
* A living annotation drawn directly into three-dimensional space

When asked a question, it answers using dynamic spatial scenes rather than filling a text panel. For
example, "Why is the sky blue?" causes the room to transform into a miniature atmosphere with
visible wavelengths scattering around the learner.

## New-age material palette

Use materials that feel beyond present industrial design:

* Frosted luminous ceramic
* Thin curved optical glass
* Translucent gels with visible internal currents
* Pearlescent biological surfaces
* Cloud-like volumetric matter
* Soft crystalline structures
* Fine point-cloud particles
* Light behaving like fabric
* Floating droplets that merge into simulations
* Materials that transition between solid, liquid, particle and diagrammatic states

Use restrained pastel colours—cyan, lavender, coral, mint, gold and deep spectral violet—against
warm white environments. Colour should encode meaning, not merely decorate.

## MR aesthetic

In passthrough, the learner's room should remain recognisable. The digital layer behaves like
intelligent material growing through it:

* A galaxy pours gently across a table edge.
* A timeline spirals around the room instead of covering a wall.
* A cell expands around the learner while furniture remains visible through it.
* Equations attach themselves to physical motion.
* Virtual roots follow the geometry of the floor.
* A simulation occupies the empty volume between furniture.
* Knowledge objects cast soft contextual light without hiding reality.

The room becomes an instrument—not merely a backdrop.

## VR aesthetic

VR environments should avoid conventional buildings. Worlds can be formed from the concept itself:

* Inside calculus, the landscape continuously transforms under functions.
* Inside biology, the world is a nested living membrane.
* Inside history, multiple eras coexist as translucent temporal layers.
* Inside computation, landscapes reorganise based on algorithmic state.
* Inside astronomy, scale shifts continuously from the learner's hand to cosmic structures.
* Inside language, words acquire physical meaning and reshape situations.

There need not be a classroom, spaceship corridor or museum hall unless the lesson specifically
requires one.

## Movement and sound

Everything should move slowly and purposefully:

* Interfaces assemble near the hand rather than popping open.
* Structures breathe subtly.
* Relationships pulse when causally active.
* Objects leave short-lived explanatory trails.
* Distant concepts produce faint harmonic resonance.
* Correct connections resolve into musical consonance.
* Contradictions produce spatial tension rather than harsh error sounds.

The result should feel calm, intelligent and alive—not overstimulating.

## Overall identity

> A post-digital spatial universe of living knowledge, rendered with luminous white space, soft
> spectral pastels, translucent intelligent matter, curved optical structures, volumetric particles
> and organically evolving scientific forms. No rectangular screens, no ordinary dashboard UI, no
> classroom aesthetic and no generic neon cyberpunk. Every concept becomes an inhabitable,
> manipulable phenomenon, while interfaces emerge naturally from the knowledge itself.

That aesthetic would make PRISM visibly belong to the age after screens: a learning environment
where knowledge is no longer represented—it is temporarily made real.
