using UnityEngine;

namespace Prism.Core
{
    /// <summary>
    /// The ten states of matter that knowledge can take. This is the grammar of the whole
    /// product: a learner should recognise WHAT KIND of thing they are looking at before
    /// they read its label, because the kind determines how it behaves in space.
    /// </summary>
    public enum KnowledgeKind
    {
        /// <summary>A small stable crystal. Settled, inert, safe to build on.</summary>
        Fact,
        /// <summary>An animated flowing ribbon. Has a direction and a rate.</summary>
        Process,
        /// <summary>A responsive miniature world. Can be poked and will answer.</summary>
        System,
        /// <summary>A spatial constraint governing objects. Not a written formula — a rule the space obeys.</summary>
        Equation,
        /// <summary>A translucent field connecting observations. Covers more than it is made of.</summary>
        Theory,
        /// <summary>An iridescent void that distorts nearby knowledge. Attracts attention by absence.</summary>
        Question,
        /// <summary>A shimmering probability cloud. Has extent instead of position.</summary>
        Uncertainty,
        /// <summary>An unstable structure that collapses under testing. Looks like a Fact until leaned on.</summary>
        Misconception,
        /// <summary>A persistent tool attached to the learner's hands. Leaves the world with them.</summary>
        Skill,
        /// <summary>Slowly orbiting sensory fragments. Personal, not shared.</summary>
        Memory
    }

    public enum ConceptDomain
    {
        MatterAndEnergy, SpaceAndTime, LifeAndEvolution, BodyAndMind, Machines,
        EarthAndCiv, Mathematics, Language, ArtAndMusic, Society
    }

    /// <summary>How one concept stands to another. Determines the colour and behaviour of the current between them.</summary>
    public enum Relation
    {
        /// <summary>A is the general case of B.</summary>
        Generalises,
        /// <summary>A is a concrete instance of B.</summary>
        Instantiates,
        /// <summary>A limits what B can do. Equations relate to systems this way.</summary>
        Constrains,
        /// <summary>A and B have the same shape in different materials. The most valuable link in PRISM.</summary>
        Analogy,
        /// <summary>A brings B about.</summary>
        Causes,
        /// <summary>A is how you find out about B.</summary>
        Measures,
        /// <summary>A is built out of B.</summary>
        Composes,
        /// <summary>A must be understood before B is available.</summary>
        Prerequisite
    }

    /// <summary>
    /// Maps a knowledge kind onto its physical behaviour. Keeping this in one place is what
    /// stops the grammar drifting: nobody has to remember that Questions are voids, because
    /// there is exactly one function that decides it.
    /// </summary>
    public static class KnowledgeAppearance
    {
        public enum Body { Crystal, Ribbon, Miniature, Lattice, Field, Void, Cloud, Fracture, Tool, Fragment }

        public static Body BodyFor(KnowledgeKind kind)
        {
            switch (kind)
            {
                case KnowledgeKind.Fact:          return Body.Crystal;
                case KnowledgeKind.Process:       return Body.Ribbon;
                case KnowledgeKind.System:        return Body.Miniature;
                case KnowledgeKind.Equation:      return Body.Lattice;
                case KnowledgeKind.Theory:        return Body.Field;
                case KnowledgeKind.Question:      return Body.Void;
                case KnowledgeKind.Uncertainty:   return Body.Cloud;
                case KnowledgeKind.Misconception: return Body.Fracture;
                case KnowledgeKind.Skill:         return Body.Tool;
                default:                          return Body.Fragment;
            }
        }

        /// <summary>Which shader family carries this kind.</summary>
        public static string ShaderFor(KnowledgeKind kind)
        {
            switch (kind)
            {
                case KnowledgeKind.Uncertainty:
                case KnowledgeKind.Memory:        return "Prism/Volumetric";
                case KnowledgeKind.Process:       return "Prism/Flow";
                case KnowledgeKind.Theory:        return "Prism/Gel";
                default:                          return "Prism/Seed";
            }
        }

        /// <summary>Base radius in metres when floating in the atrium at arm's length.</summary>
        public static float RadiusFor(KnowledgeKind kind)
        {
            switch (kind)
            {
                case KnowledgeKind.Fact:          return 0.035f;
                case KnowledgeKind.Equation:      return 0.048f;
                case KnowledgeKind.Process:       return 0.055f;
                case KnowledgeKind.System:        return 0.075f;
                case KnowledgeKind.Theory:        return 0.090f;
                case KnowledgeKind.Question:      return 0.060f;
                case KnowledgeKind.Uncertainty:   return 0.080f;
                case KnowledgeKind.Misconception: return 0.050f;
                case KnowledgeKind.Skill:         return 0.040f;
                default:                          return 0.030f;
            }
        }

        /// <summary>
        /// Questions distort the space around them. Returns the radius over which a kind
        /// perturbs its neighbours, 0 for kinds that leave the space alone.
        /// </summary>
        public static float DistortionRadius(KnowledgeKind kind)
        {
            switch (kind)
            {
                case KnowledgeKind.Question:      return 0.45f;
                case KnowledgeKind.Misconception: return 0.20f;
                default:                          return 0f;
            }
        }
    }
}
