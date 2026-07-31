using System;
using System.Collections.Generic;
using UnityEngine;

namespace Prism.Core
{
    [Serializable]
    public struct ConceptLink
    {
        [Tooltip("Stable id of the concept at the other end.")]
        public string targetId;
        public Relation relation;
        [Range(0f, 1f)] public float strength;

        public ConceptLink(string targetId, Relation relation, float strength)
        {
            this.targetId = targetId;
            this.relation = relation;
            this.strength = Mathf.Clamp01(strength);
        }
    }

    /// <summary>
    /// One star in the Knowledge Constellation.
    ///
    /// Concepts are data, not scenes. A world teaches a concept; it does not own it. That
    /// separation is what lets the constellation show a learner that the orbital mechanics
    /// they just held is the same shape as something in a world they have not opened yet —
    /// and it is what allows new lessons to be authored without rebuilding the core app.
    /// </summary>
    [CreateAssetMenu(menuName = "PRISM/Concept", fileName = "Concept")]
    public class ConceptDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable across releases. Learner progress is keyed on this, so renaming it forgets what someone knew.")]
        public string id;
        public string title;
        public ConceptDomain domain;
        public KnowledgeKind kind = KnowledgeKind.Fact;

        [Header("Placement in the constellation")]
        [Tooltip("Direction from the learner, in the atrium. Normalised on load.")]
        public Vector3 direction = Vector3.forward;
        [Tooltip("Metres from the learner. Unexplored domains sit far out as atmospheric structures.")]
        public float distance = 1.4f;

        [Header("Relationships")]
        public List<ConceptLink> links = new List<ConceptLink>();

        [Header("Teaching")]
        [Tooltip("Which world teaches this concept. Empty means nothing teaches it yet.")]
        public string worldId;

        [Tooltip("Introduced only at the Formalize stage, never before. Intuition first.")]
        [TextArea(2, 5)] public string formalisation;

        [Tooltip("What the learner should be able to DO. Not what they should be able to recite.")]
        [TextArea(2, 4)] public string capability;

        public Color Colour => PrismPalette.Spectral(PrismPalette.DomainHue(domain));

        void OnValidate()
        {
            if (string.IsNullOrEmpty(id)) id = name;
            if (direction.sqrMagnitude < 1e-6f) direction = Vector3.forward;
            distance = Mathf.Max(0.35f, distance);
        }
    }
}
