using Prism.Aesthetic;
using Prism.Core;
using UnityEngine;

namespace Prism.Atrium
{
    /// <summary>
    /// One concept, floating in the atrium.
    ///
    /// Everything visible about a node is derived from the learner's actual state — nothing is
    /// authored per node. Mastery crystallises it, isolation dims it, misconception makes it
    /// shiver, and a Question hollows it out. A learner glancing across their constellation is
    /// therefore reading their own understanding directly, not reading a summary of it.
    /// </summary>
    public class ConstellationNode : MonoBehaviour
    {
        public ConceptDefinition Concept;

        public float Mastery { get; private set; }
        public float Isolation { get; private set; }
        public float Misconception { get; private set; }

        /// <summary>Where this node rests when nothing is disturbing it.</summary>
        public Vector3 HomePosition;

        /// <summary>Set while the learner is holding it.</summary>
        public bool Summoned;

        Material _mat;
        MeshRenderer _renderer;
        float _phase;
        float _baseRadius;
        Vector3 _distortion;
        float _hover, _held;
        bool _wantHover, _wantHeld;
        Camera _camera;

        public void Initialise(ConceptDefinition concept, KnowledgeState state, Vector3 home)
        {
            Concept = concept;
            HomePosition = home;
            _phase = Mathf.Repeat(Mathf.Abs(concept.id.GetHashCode()) * 0.000173f, 1f);
            _baseRadius = KnowledgeAppearance.RadiusFor(concept.kind);

            // NOT `GetComponent<T>() ?? AddComponent<T>()`. UnityEngine.Object overloads == so a
            // missing component compares equal to null, but ?? uses REFERENCE equality and skips
            // that overload entirely — so the ?? never fires, the fake-null is returned, and the
            // next line throws MissingComponentException. That is exactly what happened: every
            // constellation node threw on its first node and the atrium built ZERO concepts.
            if (!gameObject.TryGetComponent<MeshFilter>(out var mf)) mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = PrismMesh.Icosphere(concept.kind == KnowledgeKind.Fact ? 2 : 3);

            if (!gameObject.TryGetComponent<MeshRenderer>(out _renderer)) _renderer = gameObject.AddComponent<MeshRenderer>();
            _mat = PrismMaterials.ForConcept(concept, state?.MasteryOf(concept.id) ?? 0f, 0f, _phase);
            _renderer.sharedMaterial = _mat;

            transform.localPosition = home;
            transform.localScale = Vector3.one * _baseRadius;

            Refresh(state);
        }

        /// <summary>Re-read the learner's state. Cheap; safe to call whenever anything changes.</summary>
        public void Refresh(KnowledgeState state)
        {
            if (Concept == null) return;

            var s = state?.Get(Concept.id);
            Mastery = s?.mastery ?? 0f;
            Misconception = s?.misconception ?? 0f;
            Isolation = state?.Isolation(Concept.id) ?? 1f;

            if (_mat == null) return;

            if (_mat.HasProperty("_Growth")) _mat.SetFloat("_Growth", Mastery);
            if (_mat.HasProperty("_Instability")) _mat.SetFloat("_Instability", Misconception);

            // A well-understood concept with no connections is not finished, and must not look
            // finished. Alpha carries that: it is the one signal that cannot be mistaken for
            // decoration.
            //
            // The floor was 0.45 and the hue was washed toward white at low mastery, which meant a
            // brand new learner's constellation was pale blobs on a pale sky. The floor is now high
            // enough that an untouched concept is plainly a coloured object; what mastery adds is
            // structure, not visibility. Nobody can learn from something they cannot see.
            var tint = Concept.Colour;
            tint.a = Mathf.Lerp(0.80f, 1f, state?.Luminosity(Concept.id) ?? 0f);
            _mat.SetColor("_Tint", tint);
        }

        /// <summary>
        /// Questions distort the knowledge around them. Called by the atrium with the accumulated
        /// displacement from every nearby void.
        /// </summary>
        public void SetDistortion(Vector3 offset) => _distortion = offset;

        /// <summary>The learner is reaching toward this. Smoothed, so it cannot flicker.</summary>
        public void SetHover(bool on) => _wantHover = on;

        /// <summary>The learner has hold of this.</summary>
        public void SetHeld(bool on) => _wantHeld = on;

        void Update()
        {
            // Approach and grab are smoothed rather than switched, so a hand wavering at the edge
            // of the selection cone does not make a concept strobe.
            _hover = Mathf.MoveTowards(_hover, _wantHover ? 1f : 0f, Time.deltaTime * 6f);
            _held  = Mathf.MoveTowards(_held,  _wantHeld  ? 1f : 0f, Time.deltaTime * 8f);
            if (_mat != null)
            {
                if (_mat.HasProperty("_Hover")) _mat.SetFloat("_Hover", _hover);
                if (_mat.HasProperty("_Held"))  _mat.SetFloat("_Held", _held);
            }

            // Mastered concepts grow. Not by much — the difference between a seed and a crystal is
            // carried by the shader, not by size — but enough that a dense region of the
            // constellation feels substantial. A reached-for concept swells slightly, which is the
            // clearest possible "yes, this one".
            float grow = Mathf.Lerp(0.85f, 1.15f, Mastery) * (1f + _hover * 0.18f + _held * 0.10f);

            // APPARENT size, not absolute size.
            //
            // These radii (3-9 cm) were chosen for something held at arm's length, but the
            // constellation is authored out to 2.85 m. A 4 cm sphere at 2.85 m subtends 1.4
            // degrees — a speck, indistinguishable from a dust mote, which is a large part of why
            // the atrium read as empty. Scaling with distance keeps a concept a legible OBJECT
            // wherever it sits, which is what the eye needs to accept it as a thing at all.
            float dist = 1f;
            var cam = _camera != null ? _camera : Worlds.Orbital.OrbitalWorld.FindHeadCamera();
            if (cam != null)
            {
                _camera = cam;
                dist = Vector3.Distance(cam.transform.position, transform.position);
            }
            float apparent = Mathf.Clamp(dist / 0.9f, 1f, 3.2f);

            transform.localScale = Vector3.one * (_baseRadius * grow * apparent);

            if (!Summoned)
            {
                var want = HomePosition + _distortion;
                transform.localPosition = Vector3.Lerp(transform.localPosition, want, Time.deltaTime * 2.2f);
            }
        }

        /// <summary>Current drawn radius in metres, including the distance compensation.</summary>
        public float Radius => transform.localScale.x;
        public float Phase => _phase;
    }
}
