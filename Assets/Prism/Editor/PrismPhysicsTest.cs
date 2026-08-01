using System;
using System.Collections.Generic;
using Prism.Core;
using Prism.Worlds.Orbital;
using UnityEditor;
using UnityEngine;

namespace Prism.EditorTools
{
    /// <summary>
    /// Numeric self-test for the orbital simulation, run in batch mode.
    ///
    /// This machine cannot preview XR, so "it looked right in the headset" is not available as
    /// a check. But the physics underneath the lesson is pure arithmetic with known closed-form
    /// answers, which means it can be verified completely and honestly from a terminal — and it
    /// must be, because every claim the Formalize stage makes to the learner is downstream of
    /// these numbers being right.
    ///
    ///   PRISM &gt; Test Orbital Physics
    ///   Unity -batchmode -executeMethod Prism.EditorTools.PrismPhysicsTest.RunFromCommandLine
    /// </summary>
    public static class PrismPhysicsTest
    {
        static int _failures;
        static int _checks;

        [MenuItem("PRISM/Test Orbital Physics", priority = 300)]
        public static void Run()
        {
            _failures = 0;
            _checks = 0;

            Log($"mu = {PrismScale.Mu:0.000000} m^3/s^2  " +
                $"(derived from r={PrismScale.ReferenceRadius} m, T={PrismScale.ReferencePeriod} s)");
            Log($"circular speed at 20 cm = {PrismScale.CircularSpeed(0.20f):0.0000} m/s, " +
                $"escape = {PrismScale.EscapeSpeed(0.20f):0.0000} m/s");

            TestCircularOrbitStaysCircular();
            TestEnergyIsConserved();
            TestKeplerThirdLaw();
            TestConicClassification();
            TestEscapeActuallyEscapes();
            TestDeterminism();
            TestSofteningIsNegligibleAtUsableRadii();
            TestHandSpeedBoundary();

            if (_failures == 0) Log($"ALL PASS ({_checks} checks)");
            else                LogError($"{_failures} FAILURE(S) out of {_checks} checks");
        }

        public static void RunFromCommandLine()
        {
            Run();
            EditorApplication.Exit(_failures == 0 ? 0 : 1);
        }

        // ---- the tests -------------------------------------------------------

        /// <summary>
        /// The observation the entire lesson rests on: a circular orbit must still be a circular
        /// orbit ten periods later. A non-symplectic integrator fails this visibly.
        /// </summary>
        static void TestCircularOrbitStaysCircular()
        {
            var sim = MakeSim(softening: 0f);
            float r0 = 0.20f;
            var p = sim.Spawn(new Vector3(r0, 0, 0), new Vector3(0, 0, PrismScale.CircularSpeed(r0)));

            float rMin = float.MaxValue, rMax = 0f;
            int steps = Mathf.RoundToInt(10f * PrismScale.CircularPeriod(r0) / OrbitalSim.FixedStep);
            for (int i = 0; i < steps; i++)
            {
                sim.Step(OrbitalSim.FixedStep);
                float r = p.Position.magnitude;
                rMin = Mathf.Min(rMin, r);
                rMax = Mathf.Max(rMax, r);
            }

            float drift = (rMax - rMin) / r0;
            Check("circular orbit holds its radius over 10 periods", drift < 0.002f,
                  $"radius varied {drift * 100f:0.0000}% (min {rMin:0.00000} max {rMax:0.00000})");
            Check("circular orbit did not decay", p.Active && p.Position.magnitude > r0 * 0.99f,
                  $"final radius {p.Position.magnitude:0.00000} m");
        }

        static void TestEnergyIsConserved()
        {
            var sim = MakeSim(softening: 0f);
            float r0 = 0.18f;
            // Deliberately eccentric: energy error is worst near periapsis, so a near-circular
            // orbit would flatter the integrator.
            var p = sim.Spawn(new Vector3(r0, 0, 0), new Vector3(0, 0, PrismScale.CircularSpeed(r0) * 0.72f));

            float e0 = sim.SpecificEnergy(p);
            float worst = 0f;
            int steps = Mathf.RoundToInt(10f * PrismScale.CircularPeriod(r0) / OrbitalSim.FixedStep);
            for (int i = 0; i < steps && p.Active; i++)
            {
                sim.Step(OrbitalSim.FixedStep);
                worst = Mathf.Max(worst, Mathf.Abs((sim.SpecificEnergy(p) - e0) / e0));
            }

            Check("specific energy stays bounded on an eccentric orbit (e~0.48)", worst < 0.01f,
                  $"worst relative energy error {worst * 100f:0.0000}%");
        }

        /// <summary>
        /// Measure the period by timing a full revolution, and compare with 2*pi*sqrt(a^3/mu).
        /// If this passes, the learner can later be told Kepler's third law and find that it
        /// describes something they have already watched.
        /// </summary>
        static void TestKeplerThirdLaw()
        {
            foreach (float r0 in new[] { 0.12f, 0.20f, 0.32f })
            {
                var sim = MakeSim(softening: 0f);
                var p = sim.Spawn(new Vector3(r0, 0, 0), new Vector3(0, 0, PrismScale.CircularSpeed(r0)));

                // Accumulate the magnitude of the swept angle, so a full turn is detected
                // regardless of which way round the orbit goes.
                double angle = 0;
                Vector3 prev = p.Position;
                float t = 0f;
                float predicted = PrismScale.CircularPeriod(r0);

                while (angle < 2.0 * Math.PI && t < predicted * 3f)
                {
                    sim.Step(OrbitalSim.FixedStep);
                    t += OrbitalSim.FixedStep;
                    angle += Math.Abs(Vector3.SignedAngle(prev, p.Position, Vector3.up)) * Mathf.Deg2Rad;
                    prev = p.Position;
                }

                float err = Mathf.Abs(t - predicted) / predicted;
                Check($"measured period at r={r0:0.00} m matches Kepler", err < 0.005f,
                      $"measured {t:0.0000} s vs predicted {predicted:0.0000} s ({err * 100f:0.00}%)");
            }
        }

        /// <summary>
        /// For a purely tangential throw the eccentricity is exactly |v^2 r / mu - 1|, so each of
        /// these cases has an analytic answer to compare against — including which conic it is.
        /// </summary>
        static void TestConicClassification()
        {
            float r0 = 0.20f;
            float vc = PrismScale.CircularSpeed(r0);
            float ve = PrismScale.EscapeSpeed(r0);

            var cases = new (float speed, ConicKind kind, string label)[]
            {
                (vc,          ConicKind.Circle,    "circular speed"),
                (vc * 0.90f,  ConicKind.Ellipse,   "90% of circular"),
                (vc * 1.25f,  ConicKind.Ellipse,   "125% of circular"),
                (ve,          ConicKind.Parabola,  "escape speed"),
                (ve * 1.20f,  ConicKind.Hyperbola, "120% of escape"),
            };

            foreach (var c in cases)
            {
                var el = OrbitElements.From(new Vector3(r0, 0, 0), new Vector3(0, 0, c.speed), PrismScale.Mu);
                float analytic = Mathf.Abs(c.speed * c.speed * r0 / PrismScale.Mu - 1f);

                Check($"{c.label} is classified as {c.kind}", el.Kind == c.kind,
                      $"got {el.Kind} (e={el.Eccentricity:0.0000})");
                Check($"{c.label} eccentricity matches analytic {analytic:0.0000}",
                      Mathf.Abs(el.Eccentricity - analytic) < 1e-4f,
                      $"got e={el.Eccentricity:0.000000}");
            }

            // The sign of the energy is the bound/unbound boundary the Discover gate watches for.
            var bound   = OrbitElements.From(new Vector3(r0, 0, 0), new Vector3(0, 0, vc), PrismScale.Mu);
            var unbound = OrbitElements.From(new Vector3(r0, 0, 0), new Vector3(0, 0, ve * 1.1f), PrismScale.Mu);
            Check("circular orbit reports bound",   bound.IsBound,    $"energy {bound.SpecificEnergy:0.00000}");
            Check("fast throw reports unbound",    !unbound.IsBound,  $"energy {unbound.SpecificEnergy:0.00000}");
        }

        static void TestEscapeActuallyEscapes()
        {
            var sim = MakeSim(softening: 0f);
            float r0 = 0.20f;
            var p = sim.Spawn(new Vector3(r0, 0, 0), new Vector3(0, 0, PrismScale.EscapeSpeed(r0) * 1.15f));

            float rPrev = r0;
            bool monotonic = true;
            for (int i = 0; i < 240 * 60; i++)
            {
                sim.Step(OrbitalSim.FixedStep);
                float r = p.Position.magnitude;
                if (r < rPrev - 1e-7f) monotonic = false;
                rPrev = r;
            }

            Check("a throw above escape speed recedes monotonically", monotonic, "radius decreased at some point");
            Check("a throw above escape speed gets far away", rPrev > r0 * 20f,
                  $"reached only {rPrev:0.000} m after 60 s");
        }

        /// <summary>
        /// Two identical throws must produce identical trajectories, exactly. Without this the
        /// replay and branch-into-alternative-outcomes features cannot be trusted, and neither
        /// can any claim that a learner's second attempt differed because THEY did something
        /// different.
        /// </summary>
        static void TestDeterminism()
        {
            Vector3 RunOnce()
            {
                var sim = MakeSim(softening: 0.008f);
                var p = sim.Spawn(new Vector3(0.17f, 0.02f, 0f), new Vector3(0.03f, -0.01f, 0.19f));
                for (int i = 0; i < 240 * 20; i++) sim.Step(OrbitalSim.FixedStep);
                return p.Position;
            }

            var a = RunOnce();
            var b = RunOnce();
            Check("identical throws are bitwise reproducible", a == b, $"{a:F8} vs {b:F8}");
        }

        /// <summary>
        /// Softening exists to stop a near-radial throw exploding. It must not be large enough to
        /// distort an orbit a learner would actually make.
        /// </summary>
        static void TestSofteningIsNegligibleAtUsableRadii()
        {
            var soft = MakeSim(softening: new OrbitalSim().Softening);   // the shipping value
            var hard = MakeSim(softening: 0f);
            float r0 = 0.20f;
            float v  = PrismScale.CircularSpeed(r0);

            var ps = soft.Spawn(new Vector3(r0, 0, 0), new Vector3(0, 0, v));
            var ph = hard.Spawn(new Vector3(r0, 0, 0), new Vector3(0, 0, v));

            int steps = Mathf.RoundToInt(PrismScale.CircularPeriod(r0) / OrbitalSim.FixedStep);
            for (int i = 0; i < steps; i++)
            {
                soft.Step(OrbitalSim.FixedStep);
                hard.Step(OrbitalSim.FixedStep);
            }

            // What matters is whether softening changes the SHAPE of the orbit and the PERIOD,
            // because those are the two things this world makes claims about. Raw separation
            // after a period is dominated by along-track phase, which is a slower clock rather
            // than a wrong orbit, so it is reported but not asserted on.
            float e = soft.ElementsOf(ps).Eccentricity;
            Check("softening keeps a circular throw inside the circle tolerance",
                  e < OrbitElements.CircleTolerance,
                  $"eccentricity induced by softening = {e:0.00000} (tolerance {OrbitElements.CircleTolerance})");

            var el = soft.ElementsOf(ps);
            float periodErr = Mathf.Abs(el.Period - PrismScale.CircularPeriod(r0))
                            / PrismScale.CircularPeriod(r0);
            Check("softening leaves Kepler's third law intact to within 0.5%", periodErr < 0.005f,
                  $"period error {periodErr * 100f:0.000}%");

            float sep = (ps.Position - ph.Position).magnitude;
            Log($"      (informational: along-track drift after one period {sep * 1000f:0.00} mm)");
        }

        /// <summary>
        /// The design claim in PrismScale: the gap between orbiting and escaping is a difference
        /// of roughly 9 cm/s of wrist speed, which is why a learner can feel it. Assert the claim
        /// so it cannot quietly stop being true if mu is ever retuned.
        /// </summary>
        static void TestHandSpeedBoundary()
        {
            float r0 = 0.20f;
            float vc = PrismScale.CircularSpeed(r0);
            float ve = PrismScale.EscapeSpeed(r0);
            float gap = ve - vc;

            Check("circular and escape speeds are both hand speeds (5-50 cm/s)",
                  vc > 0.05f && vc < 0.5f && ve > 0.05f && ve < 0.5f,
                  $"vc={vc:0.000} ve={ve:0.000} m/s");
            Check("the orbit/escape boundary is a feelable 5-15 cm/s of wrist",
                  gap > 0.05f && gap < 0.15f,
                  $"gap = {gap * 100f:0.0} cm/s");
        }

        // ---- harness ---------------------------------------------------------

        static OrbitalSim MakeSim(float softening)
        {
            var sim = new OrbitalSim { Softening = softening };
            sim.Attractors.Add(new OrbitalSim.Attractor
            {
                Position = Vector3.zero,
                Mu = PrismScale.Mu,
                Radius = PrismScale.DisplayPlanetRadius
            });
            return sim;
        }

        static void Check(string what, bool ok, string detail)
        {
            _checks++;
            if (ok) Log($"pass  {what}  [{detail}]");
            else { _failures++; LogError($"FAIL  {what}  [{detail}]"); }
        }

        static void Log(string m)      => Debug.Log("[PRISM-TEST] " + m);
        static void LogError(string m) => Debug.LogError("[PRISM-TEST] " + m);
    }
}
