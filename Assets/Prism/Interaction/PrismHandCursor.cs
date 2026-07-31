using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using UnityEngine;

namespace Prism.Interaction
{
    /// <summary>
    /// A visible presence for one hand: a small bead at the fingertip and a soft beam along where
    /// the hand is pointing.
    ///
    /// This exists for a blunt reason. The first build shipped with no hand representation at all,
    /// which meant a learner who could not interact had no way to tell WHY — whether tracking had
    /// failed, whether they were aiming at nothing, or whether the app was simply inert. Those are
    /// three completely different faults and they looked identical.
    ///
    /// It also earns its place in the finished product: the brief asks for interfaces that assemble
    /// near the hand, and a bead that brightens as it approaches a concept is the smallest possible
    /// version of that. It is deliberately not a laser pointer — the beam is short, soft and fades
    /// out, so it reads as reach rather than as a cursor.
    /// </summary>
    public class PrismHandCursor : MonoBehaviour
    {
        public PrismHands Hands;
        public bool IsRight;

        [Tooltip("Metres. How far the reach beam is drawn when nothing is being aimed at.")]
        public float FreeBeamLength = 0.22f;

        [Tooltip("Bead radius in metres.")]
        public float BeadRadius = 0.009f;

        Transform _bead;
        Material _beadMat;
        Transform _beam;
        Material _beamMat;
        Mesh _beamMesh;
        readonly List<Vector3> _path = new List<Vector3>();

        /// <summary>Set by the atrium each frame: the world point this hand is aiming at, if any.</summary>
        public bool HasTarget;
        public Vector3 TargetPoint;

        void Awake()
        {
            var beadGo = new GameObject("Bead");
            beadGo.transform.SetParent(transform, false);
            beadGo.AddComponent<MeshFilter>().sharedMesh = PrismMesh.Icosphere(2);
            _beadMat = PrismMaterials.New(PrismMaterials.Mote);
            _beadMat.SetColor("_Tint", PrismPalette.Cyan);
            _beadMat.SetColor("_EdgeTint", PrismPalette.Warm);
            beadGo.AddComponent<MeshRenderer>().sharedMaterial = _beadMat;
            _bead = beadGo.transform;
            _bead.localScale = Vector3.one * BeadRadius;

            var beamGo = new GameObject("Beam");
            beamGo.transform.SetParent(transform, false);
            _beamMesh = new Mesh { name = "ReachBeam" };
            _beamMesh.MarkDynamic();
            beamGo.AddComponent<MeshFilter>().sharedMesh = _beamMesh;
            _beamMat = PrismMaterials.ForRelation(Relation.Measures, 0.5f);
            _beamMat.SetFloat("_CoreGain", 0.30f);
            _beamMat.SetFloat("_Packets", 3f);
            beamGo.AddComponent<MeshRenderer>().sharedMaterial = _beamMat;
            _beam = beamGo.transform;
        }

        void LateUpdate()
        {
            var h = Hands == null ? null : (IsRight ? Hands.Right : Hands.Left);
            if (h == null || !h.IsTracked)
            {
                // An untracked hand shows nothing at all. Parking a bead at the last known pose —
                // or worse at the origin — is how you end up with a mystery object inside the
                // learner's head.
                _bead.gameObject.SetActive(false);
                _beam.gameObject.SetActive(false);
                return;
            }

            _bead.gameObject.SetActive(true);

            Vector3 point = PrismHands.PointOf(h);
            _bead.position = point;

            // The bead answers the grasp: it tightens and turns gold as the hand closes, so the
            // learner can see the gesture register before it has any effect on anything.
            float grasp = Mathf.Max(h.Pinch, h.Grip);
            _bead.localScale = Vector3.one * (BeadRadius * Mathf.Lerp(1f, 0.62f, grasp)
                                              * (HasTarget ? 1.35f : 1f));
            _beadMat.SetColor("_Tint", Color.Lerp(
                HasTarget ? PrismPalette.Gold : PrismPalette.Cyan, PrismPalette.Coral, grasp));
            _beadMat.SetFloat("_Density", Mathf.Lerp(0.9f, 1.8f, Mathf.Max(grasp, HasTarget ? 0.5f : 0f)));

            // The beam.
            if (!h.ReachValid)
            {
                _beam.gameObject.SetActive(false);
                return;
            }

            Vector3 from = point;
            Vector3 to = HasTarget ? TargetPoint : h.Reach.origin + h.Reach.direction * FreeBeamLength;

            // When aiming at something far away, do not draw the whole span — a long hard line
            // reads as a laser sight. Draw a short stub from the hand plus a bloom at the target,
            // which the hovered concept itself provides.
            float span = Vector3.Distance(from, to);
            if (span > FreeBeamLength * 2.2f)
                to = from + (to - from).normalized * (FreeBeamLength * 2.2f);

            _beam.gameObject.SetActive(true);
            _path.Clear();
            const int n = 12;
            for (int i = 0; i < n; i++) _path.Add(Vector3.Lerp(from, to, i / (float)(n - 1)));
            for (int i = 0; i < _path.Count; i++) _path[i] = _beam.InverseTransformPoint(_path[i]);

            PrismMesh.Tube(_path, 0.0016f, 5, _beamMesh);
            _beamMat.SetFloat("_Pulse", HasTarget ? 1f : 0.25f);
            _beamMat.SetFloat("_Strength", HasTarget ? 0.7f : 0.35f);
        }
    }
}
