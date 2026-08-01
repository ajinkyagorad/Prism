using System.Collections.Generic;
using Prism.Aesthetic;
using UnityEngine;

namespace Prism.Worlds.AlgorithmCity
{
    /// <summary>
    /// A bead that slides along a short rail, made once and reused for every 1-D physical control
    /// in this world: the two clock speeds, the input-size rail, and the pipeline divider. Grabbing
    /// and moving the bead IS setting the value — there is no separate readout to keep in sync.
    /// </summary>
    public class SliderControl
    {
        public Transform Bead { get; }
        public float T { get; private set; }

        readonly Vector3 _origin;
        readonly Vector3 _axis;
        readonly float _halfLength;
        readonly Transform _rail;

        public Vector3 LocalPosition => Bead.localPosition;

        public SliderControl(string name, Transform parent, Vector3 origin, Vector3 axis, float halfLength,
                             float initialT, Material railMat, Material beadMat, float beadRadius = 0.010f)
        {
            _origin = origin;
            _axis = axis.normalized;
            _halfLength = halfLength;
            T = Mathf.Clamp01(initialT);

            var railPts = new List<Vector3> { origin - _axis * halfLength, origin + _axis * halfLength };
            var railGo = new GameObject(name + "Rail");
            railGo.transform.SetParent(parent, false);
            railGo.AddComponent<MeshFilter>().sharedMesh = PrismMesh.Tube(railPts, 0.0020f, 5, null);
            railGo.AddComponent<MeshRenderer>().sharedMaterial = railMat;
            _rail = railGo.transform;

            var beadGo = new GameObject(name + "Handle");
            beadGo.transform.SetParent(parent, false);
            beadGo.AddComponent<MeshFilter>().sharedMesh = PrismMesh.Icosphere(1);
            beadGo.AddComponent<MeshRenderer>().sharedMaterial = beadMat;
            beadGo.transform.localScale = Vector3.one * beadRadius;
            Bead = beadGo.transform;
            Bead.localPosition = PositionAt(T);
        }

        Vector3 PositionAt(float t) => _origin + _axis * ((t * 2f - 1f) * _halfLength);

        public void SetT(float t)
        {
            T = Mathf.Clamp01(t);
            Bead.localPosition = PositionAt(T);
        }

        /// <summary>Move the bead to the point on the rail nearest a hand position, given in the
        /// same local space the rail was built in.</summary>
        public void DragTo(Vector3 localHandPos)
        {
            float proj = Vector3.Dot(localHandPos - _origin, _axis);
            SetT((proj / _halfLength + 1f) * 0.5f);
        }

        public void SetActive(bool on)
        {
            Bead.gameObject.SetActive(on);
            _rail.gameObject.SetActive(on);
        }
    }

    /// <summary>
    /// A small physical switch near a structure's entrance: "block a path" made literal. While
    /// blocked, the structure it is wired to simply stops advancing — no partial state, no special
    /// casing, the algorithm just does not get its next <c>Step()</c> call.
    /// </summary>
    public class GateToken
    {
        public Transform View { get; }
        public bool Blocked { get; private set; }

        readonly Material _mat;

        public GateToken(string name, Transform parent, Vector3 localPos, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * 0.013f;
            go.AddComponent<MeshFilter>().sharedMesh = PrismMesh.Icosphere(1);
            _mat = material;
            go.AddComponent<MeshRenderer>().sharedMaterial = _mat;
            View = go.transform;
            _mat.SetFloat("_Luminance", 0.22f);
        }

        public void Toggle()
        {
            Blocked = !Blocked;
            _mat.SetFloat("_Luminance", Blocked ? 0.95f : 0.22f);
        }

        public void SetActive(bool on) => View.gameObject.SetActive(on);
    }
}
