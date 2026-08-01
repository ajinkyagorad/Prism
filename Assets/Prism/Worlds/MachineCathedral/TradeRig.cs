using UnityEngine;

namespace Prism.Worlds.MachineCathedral
{
    /// <summary>
    /// The one idea, as one integrator.
    ///
    /// A lever, a gear train and a block-and-tackle look nothing alike, but each reduces to exactly
    /// the same 1-DOF system once you write it in a generalised coordinate q: something drives q
    /// against a constant load force, through a ratio that trades the load's effective force for the
    /// distance q must travel. Every station in this world (levers, cranks, pulleys, switches,
    /// relays) is a thin skin around one instance of this class, computing its own <see cref="Ratio"/>
    /// and <see cref="EffMass"/> from its own geometry and calling <see cref="Step"/>. That is not a
    /// code-reuse convenience — it is the whole thesis made structural: if two mechanisms use the
    /// same integrator with only their ratio changed, then the ratio really was the only thing that
    /// ever differed between them.
    ///
    /// GENERALISED COORDINATE. q means whatever the owning station says it means: an angle in
    /// radians for a lever or a crank, a length in metres for a pulley's paid-out rope. Whatever it
    /// is, <see cref="Output"/> — the quantity actually delivered to the load — is always
    /// <c>dq / Ratio</c>, integrated from the CURRENT ratio at each step rather than computed
    /// algebraically from total q. That is deliberate and load-bearing: it is what lets a learner
    /// change a gear mid-turn or slide a fulcrum mid-swing without the load's height silently
    /// jumping — sliding a fulcrum with the beam level does no work on the load, and this
    /// integration scheme guarantees that by construction rather than by special-casing it.
    ///
    /// WHAT IS IDEAL HERE, ON PURPOSE. The beam, gears, pulleys and ropes are treated as massless
    /// and frictionless; only the load itself and a small handle inertia carry mass. That is the
    /// standard first idealisation of a machine, and it is the right one for this lesson: it isolates
    /// the ONE relationship (ratio trades force for distance, product conserved) from the secondary
    /// true fact that spinning up the machine's own parts also costs a little energy. The only
    /// dissipative term anywhere is <see cref="Damping"/> on the input coordinate, which stands for
    /// the give in a human grip — real, small, and named honestly in NOTES.md rather than hidden.
    /// </summary>
    public class TradeRig
    {
        /// <summary>Fixed physics step, seconds. Small mechanisms with a stiff hand-spring need a
        /// short step to stay numerically calm; this is cheap enough that a handful of rigs cost
        /// nothing measurable.</summary>
        public const float FixedStep = 1f / 120f;

        // ---- configuration, set by the owning station before each Advance ----

        /// <summary>Mechanical advantage of the CURRENT configuration. R = (effort distance) /
        /// (load distance) = (load force) / (effort force) for an ideal machine — the two ratios
        /// are the same number, which is the reason a machine can never multiply both at once.
        /// R &gt; 1 trades distance for force; R &lt; 1 trades force for distance; R = 1 trades nothing.</summary>
        public float Ratio = 1f;

        /// <summary>Constant physical force or torque the load exerts against increasing Output —
        /// almost always a weight, mass times g. Always &gt;= 0; the sign is applied internally.</summary>
        public float LoadForce;

        /// <summary>Inertia reflected to q: handle/crank inertia plus (load mass)/Ratio^2. The
        /// classic "reflected inertia falls with the square of the ratio" result, computed by the
        /// station because only it knows whether q is an angle or a length.</summary>
        public float EffMass = 0.05f;

        /// <summary>Velocity damping on q. The only place energy leaves this system — see the class
        /// comment. Kept small; it exists for numerical calm and for the honest fact that a hand's
        /// grip is not a lossless spring, not to make the machine itself lossy.</summary>
        public float Damping = 0.35f;

        /// <summary>Optional linear restoring stiffness toward q = 0, i.e. a return spring. Zero for
        /// the lever/crank/pulley stations; positive for switches and relays, which must fall back
        /// open on their own.</summary>
        public float ReturnK = 0f;

        /// <summary>Soft mechanical stops. A stop kills the velocity component driving past it
        /// rather than bouncing — an inelastic limit, which is what a beam hitting a cross-brace or
        /// a rope running out actually does.</summary>
        public float QMin = float.NegativeInfinity;
        public float QMax = float.PositiveInfinity;

        // ---- state ----

        public float Q;
        public float QDot;

        /// <summary>Accumulated output delivered to the load, in the load's own units (metres of
        /// rise, almost always). See the class comment for why this is integrated, not derived.</summary>
        public float Output;

        /// <summary>Running work done ON q by the effort force, and work done BY the load side
        /// against its constant resisting force. These are the two lengths Discover shows side by
        /// side; at rest they differ only by whatever Damping removed.</summary>
        public float WorkIn;
        public float WorkOut;

        public float LastEffort { get; private set; }

        float _accum;

        public float KineticEnergy => 0.5f * EffMass * QDot * QDot;

        /// <summary>A hand (or any position-controlled driver) pulling q toward a target through a
        /// damped virtual spring. Force is clamped — a stand-in for how hard a human hand can push,
        /// and also the numerical safety valve that keeps a stiff spring from ever diverging.</summary>
        public static float SpringEffort(float target, float q, float qdot, float k, float c, float maxForce)
        {
            float f = k * (target - q) - c * qdot;
            return Mathf.Clamp(f, -maxForce, maxForce);
        }

        /// <summary>Advance by real elapsed time, re-deriving the spring effort from the live state
        /// at every fixed sub-step — required for a stiff spring to stay honest, since the force
        /// depends on where q actually is, not on where it was a whole frame ago.</summary>
        public void AdvanceSpring(float realDt, float targetQ, float k, float c, float maxForce)
        {
            _accum += realDt;
            int budget = 32;
            while (_accum >= FixedStep && budget-- > 0)
            {
                float effort = SpringEffort(targetQ, Q, QDot, k, c, maxForce);
                Step(FixedStep, effort);
                _accum -= FixedStep;
            }
            if (budget <= 0) _accum = 0f;
        }

        /// <summary>Advance under a constant driving effort for the whole call — a relay coil's pull,
        /// or zero for a mechanism nobody is touching right now.</summary>
        public void AdvanceConstant(float realDt, float effort)
        {
            _accum += realDt;
            int budget = 32;
            while (_accum >= FixedStep && budget-- > 0)
            {
                Step(FixedStep, effort);
                _accum -= FixedStep;
            }
            if (budget <= 0) _accum = 0f;
        }

        /// <summary>One fixed sub-step. Semi-implicit (symplectic) Euler: stable for a damped spring
        /// at these stiffnesses and step sizes without needing the higher-order machinery a real
        /// orbit does, because nothing here is asked to stay accurate over thousands of periods —
        /// only to stay honest and not blow up under a jittery hand.</summary>
        void Step(float dt, float effort)
        {
            float loadGeneralised = Mathf.Abs(Ratio) > 1e-4f ? LoadForce / Ratio : 0f;
            float mass = Mathf.Max(EffMass, 1e-5f);

            float accel = (effort - loadGeneralised - Damping * QDot - ReturnK * Q) / mass;
            QDot += accel * dt;

            float newQ = Q + QDot * dt;
            if (newQ < QMin) { newQ = QMin; if (QDot < 0f) QDot = 0f; }
            if (newQ > QMax) { newQ = QMax; if (QDot > 0f) QDot = 0f; }
            float dq = newQ - Q;
            Q = newQ;

            float dOut = Mathf.Abs(Ratio) > 1e-4f ? dq / Ratio : 0f;
            Output += dOut;

            WorkIn += effort * dq;
            WorkOut += LoadForce * dOut;
            LastEffort = effort;
        }

        public void HardReset()
        {
            Q = QDot = Output = WorkIn = WorkOut = LastEffort = 0f;
            _accum = 0f;
        }
    }
}
