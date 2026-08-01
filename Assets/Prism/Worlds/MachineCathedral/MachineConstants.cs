using UnityEngine;
using Prism.Core;

namespace Prism.Worlds.MachineCathedral
{
    /// <summary>Numbers shared by every station, gathered in one place for the same reason
    /// <c>PrismScale</c> gathers the orbital world's: so the felt strength of a "push" means the
    /// same thing everywhere in this world, and so retuning it once retunes it everywhere.</summary>
    public static class MachineConstants
    {
        public const float Gravity = 9.8f;

        /// <summary>
        /// The comfortable hand-push budget, in newtons of generalised effort. This single number
        /// does three jobs at once: it is the numerical clamp that keeps the hand-spring stable, it
        /// is the honest stand-in for "how hard a human will comfortably push," and it is the exact
        /// quantity the Apply stage tests against — a load that needs more force than this AT THE
        /// CURRENT RATIO genuinely cannot be lifted, not until the learner changes the ratio. The
        /// constraint is not a game-design cap bolted on afterwards; it is the same number in three
        /// places.
        /// </summary>
        public const float MaxEffortForce = 5.0f;

        /// <summary>Hand-to-handle spring stiffness and damping. Tuned so the natural frequency
        /// stays well inside the stable region of TradeRig's fixed step at every ratio a station
        /// can reach — see TradeRig's class comment.</summary>
        public const float SpringK = 70f;
        public const float SpringC = 3.2f;

        /// <summary>Small felt inertia given to every crank/handle so a rig never has literally
        /// zero mass at the input side, which would make EffMass degenerate at very high ratios.</summary>
        public const float HandleMass = 0.02f;

        /// <summary>Grab radius for any handle in this world, metres.</summary>
        public const float GrabRadius = 0.045f;

        /// <summary>Reference span for the force-colour ramp — see MachineCathedralWorld's colour
        /// encoding note. A gentle push and a heavily multiplied load reaction both need to land at
        /// visually distinct points on one shared ramp, so both ends of a drivetrain read on the
        /// same scale.</summary>
        public const float ForceColourReference = 3.2f * MaxEffortForce;

        /// <summary>
        /// Colour encoding for this whole world: hue is FORCE (or torque) magnitude at the point in
        /// the drivetrain being coloured, on the spectral ramp, cool (cyan) for a light load and hot
        /// (violet) for a heavy one. It is live from Wonder onward — it is not a label, it is the
        /// phenomenon, the same way the orbital world's trail colour is speed before anyone names
        /// Kepler's second law. A learner who has never been told anything can still see that the
        /// crank glows gently while the load it is lifting glows hot, and that is the whole lesson
        /// in one glance, before a single word is spoken.
        /// </summary>
        public static Color ForceColour(float forceMagnitude) =>
            PrismPalette.Spectral(Mathf.Clamp01(Mathf.Abs(forceMagnitude) / ForceColourReference));
    }
}
