using System.Collections.Generic;
using UnityEngine;

namespace Prism.Worlds.Orbital
{
    /// <summary>
    /// The deterministic gravitational simulation behind the Orbital Mechanics world.
    ///
    /// Plain C#, no MonoBehaviour, no UnityEngine.Physics. Three reasons, all of them load
    /// bearing:
    ///
    /// 1. INTEGRATOR CHOICE IS A PEDAGOGICAL DECISION HERE. Explicit Euler — and Unity's
    ///    rigidbody solver, for this purpose — bleeds energy, so an ellipse visibly spirals
    ///    inward over a few dozen seconds. A learner watching that would correctly conclude
    ///    that orbits decay, which is false and is a much harder misconception to remove than
    ///    the one we were trying to teach. This uses kick-drift-kick leapfrog, which is
    ///    symplectic: energy error is bounded and oscillates rather than accumulating, so the
    ///    ellipse closes on itself for as long as anyone cares to watch. The orbit closing is
    ///    not a nicety. It is the observation the whole lesson rests on.
    ///
    /// 2. Fixed timestep accumulated from real time, so the simulation is frame-rate
    ///    independent and reproducible. Same throw, same trajectory, on a 72 Hz headset and
    ///    on a headless build server.
    ///
    /// 3. Because it is testable without a headset. <c>PrismPhysicsTest</c> runs this against
    ///    analytic Kepler solutions in batch mode, which is the only way to know the physics
    ///    is right on a machine that cannot preview XR.
    /// </summary>
    public class OrbitalSim
    {
        /// <summary>
        /// A source of gravity. Kinematic: attractors are placed by the learner and never fall
        /// toward each other. That is an approximation, and an honest one at these mass ratios —
        /// a 4 cm planet and a 6 mm moon differ in mass by enough that the planet's motion would
        /// be invisible. It also means the learner can pick a planet up and move it, which is
        /// worth more than the accuracy it costs.
        /// </summary>
        public class Attractor
        {
            public Vector3 Position;
            /// <summary>Standard gravitational parameter GM, m^3/s^2.</summary>
            public float Mu;
            /// <summary>Physical radius. A moon reaching this is absorbed.</summary>
            public float Radius;
        }

        public class Particle
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float Radius = 0.006f;
            public bool Active = true;
            /// <summary>Set when the particle hit something, for the world to react to.</summary>
            public bool JustAbsorbed;
            /// <summary>Seconds of simulated time since release.</summary>
            public float Age;
            public object Tag;
        }

        /// <summary>
        /// Fixed step, seconds. 1/240 keeps the leapfrog's per-orbit phase error under a
        /// thousandth at the tightest periapsis a learner can reach by hand, and costs nothing
        /// for a handful of particles.
        /// </summary>
        public const float FixedStep = 1f / 240f;

        /// <summary>
        /// Plummer softening length. Not a fudge to keep things stable — it is what stops a
        /// near-radial throw producing an enormous acceleration in one step and flinging a moon
        /// across the room, which reads to a learner as a bug rather than as physics.
        ///
        /// 4 mm, kept deliberately small. Gravity is never evaluated closer than the absorption
        /// radius (planet 40 mm + moon 6 mm), where softening weakens the force by ~1%; at the
        /// 20 cm radii learners actually orbit at it is 0.04%, which is four hundred times
        /// smaller than the eccentricity we are willing to call a circle. Raising it is the
        /// wrong instinct: softening that is large enough to notice makes Kepler's third law
        /// measurably wrong, and that law is something this world claims out loud.
        /// </summary>
        public float Softening = 0.004f;

        public readonly List<Attractor> Attractors = new List<Attractor>();
        public readonly List<Particle>  Particles  = new List<Particle>();

        /// <summary>Simulated seconds elapsed. Advances only in whole fixed steps.</summary>
        public double Time { get; private set; }
        public long Steps { get; private set; }

        /// <summary>Simulation rate. Time can be pushed forward and backward at the spatial timeline.</summary>
        public float TimeScale = 1f;

        float _accumulator;

        /// <summary>
        /// Advance by real elapsed time. Leftover time is carried, never dropped, so the
        /// simulation cannot drift against the clock.
        /// </summary>
        public void Advance(float realDeltaSeconds)
        {
            _accumulator += realDeltaSeconds * TimeScale;

            // Clamp catch-up. A hitch (a domain reload, a permission dialog) must not spend
            // thousands of steps trying to make up lost simulated time.
            const int maxStepsPerFrame = 64;
            int budget = maxStepsPerFrame;

            while (_accumulator >= FixedStep && budget-- > 0)
            {
                Step(FixedStep);
                _accumulator -= FixedStep;
            }
            if (budget <= 0) _accumulator = 0f;

            // Running time backwards is a scrub, not an integration: reversing leapfrog is
            // exact only if nothing was absorbed, so the world replays from a recorded state
            // instead. See OrbitalWorld's timeline.
            if (TimeScale < 0f) _accumulator = 0f;
        }

        public Vector3 AccelerationAt(Vector3 p)
        {
            Vector3 a = Vector3.zero;
            float soft2 = Softening * Softening;
            for (int i = 0; i < Attractors.Count; i++)
            {
                var at = Attractors[i];
                Vector3 d = at.Position - p;
                float r2 = d.sqrMagnitude + soft2;
                float invR = 1f / Mathf.Sqrt(r2);
                a += d * (at.Mu * invR * invR * invR);
            }
            return a;
        }

        /// <summary>One kick-drift-kick leapfrog step. Symplectic; see the class comment.</summary>
        public void Step(float dt)
        {
            float half = dt * 0.5f;

            for (int i = 0; i < Particles.Count; i++)
            {
                var p = Particles[i];
                if (!p.Active) continue;

                p.Velocity += AccelerationAt(p.Position) * half;   // kick
                p.Position += p.Velocity * dt;                     // drift
                p.Velocity += AccelerationAt(p.Position) * half;   // kick
                p.Age += dt;

                for (int j = 0; j < Attractors.Count; j++)
                {
                    var at = Attractors[j];
                    float touch = at.Radius + p.Radius;
                    if ((p.Position - at.Position).sqrMagnitude <= touch * touch)
                    {
                        p.Active = false;
                        p.JustAbsorbed = true;
                        break;
                    }
                }
            }

            Time += dt;
            Steps++;
        }

        /// <summary>
        /// Specific orbital energy of a particle about the dominant attractor. Negative is bound.
        /// The sign of this number is what the Discover stage is quietly waiting for the learner
        /// to produce on both sides of.
        /// </summary>
        public float SpecificEnergy(Particle p)
        {
            var primary = Primary;
            if (primary == null) return 0f;
            float r = Mathf.Max((p.Position - primary.Position).magnitude, 1e-6f);
            return 0.5f * p.Velocity.sqrMagnitude - primary.Mu / r;
        }

        public OrbitElements ElementsOf(Particle p)
        {
            var primary = Primary;
            if (primary == null) return default;
            return OrbitElements.From(p.Position - primary.Position, p.Velocity, primary.Mu);
        }

        /// <summary>The heaviest attractor. Orbital elements are only meaningful relative to one body.</summary>
        public Attractor Primary
        {
            get
            {
                Attractor best = null;
                for (int i = 0; i < Attractors.Count; i++)
                    if (best == null || Attractors[i].Mu > best.Mu) best = Attractors[i];
                return best;
            }
        }

        public Particle Spawn(Vector3 position, Vector3 velocity, float radius = 0.006f, object tag = null)
        {
            var p = new Particle { Position = position, Velocity = velocity, Radius = radius, Tag = tag };
            Particles.Add(p);
            return p;
        }

        public void Reset()
        {
            Particles.Clear();
            Time = 0;
            Steps = 0;
            _accumulator = 0f;
        }

        /// <summary>
        /// Integrate a copy of a particle forward without touching the live simulation. Used to
        /// draw the predicted path ahead of a throw the learner has not released yet, and by the
        /// Explain stage to check a prediction against what will actually happen.
        /// </summary>
        public List<Vector3> Predict(Vector3 position, Vector3 velocity, float seconds, int maxSamples = 256)
        {
            var path = new List<Vector3>(maxSamples);
            // Coarser than the live step: this runs every frame while aiming, and a prediction
            // is a sketch, not a simulation.
            float dt = Mathf.Max(seconds / maxSamples, FixedStep);
            var pos = position;
            var vel = velocity;
            var primary = Primary;

            for (int i = 0; i < maxSamples; i++)
            {
                float half = dt * 0.5f;
                vel += AccelerationAt(pos) * half;
                pos += vel * dt;
                vel += AccelerationAt(pos) * half;
                path.Add(pos);

                if (primary != null)
                {
                    float touch = primary.Radius + 0.006f;
                    if ((pos - primary.Position).sqrMagnitude <= touch * touch) break;
                    // Stop drawing once it is clearly gone rather than running off to infinity.
                    if ((pos - primary.Position).sqrMagnitude > 9f) break;
                }
            }
            return path;
        }
    }
}
