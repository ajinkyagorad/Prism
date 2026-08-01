using System.Collections.Generic;
using UnityEngine;

namespace Prism.Aesthetic
{
    /// <summary>
    /// A short-lived explanatory trail, rebuilt every frame as a camera-facing ribbon.
    ///
    /// Speed at each sample is written into UV1 and becomes colour in Prism/Trail. That is the
    /// whole point of this component: the trail is not decoration, it is a plot of speed against
    /// position, drawn in the air, for free, while the learner watches.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
    public class PrismTrailRibbon : MonoBehaviour
    {
        [Tooltip("Seconds a sample survives. Long enough to close an orbit, short enough not to clutter.")]
        public float Lifetime = 7f;

        [Tooltip("Metres of travel between samples. Denser near periapsis where the path curves most.")]
        public float MinSpacing = 0.004f;

        public float Width = 0.0035f;
        public int MaxSamples = 512;

        struct Sample
        {
            public Vector3 Position;
            public float Speed;
            public float Born;
        }

        readonly List<Sample> _samples = new List<Sample>();
        Mesh _mesh;
        MeshFilter _filter;
        Camera _camera;

        readonly List<Vector3> _verts = new List<Vector3>();
        readonly List<Vector2> _uv0   = new List<Vector2>();
        readonly List<Vector2> _uv1   = new List<Vector2>();
        readonly List<int>     _tris  = new List<int>();

        void Awake()
        {
            _filter = GetComponent<MeshFilter>();
            _mesh = new Mesh { name = "PrismTrail" };
            _mesh.MarkDynamic();
            _filter.sharedMesh = _mesh;
        }

        /// <summary>
        /// Which camera the ribbon should face. Under XR Simulation Camera.main is often not the
        /// camera that actually draws, so the world passes its head camera in explicitly rather
        /// than letting this guess.
        /// </summary>
        public void SetCamera(Camera camera) => _camera = camera;

        public void Clear()
        {
            _samples.Clear();
            _mesh.Clear();
        }

        public void Push(Vector3 worldPosition, float speed)
        {
            if (_samples.Count > 0)
            {
                var last = _samples[_samples.Count - 1];
                if ((worldPosition - last.Position).sqrMagnitude < MinSpacing * MinSpacing) return;
            }
            _samples.Add(new Sample { Position = worldPosition, Speed = speed, Born = Time.time });
            if (_samples.Count > MaxSamples) _samples.RemoveAt(0);
        }

        void LateUpdate()
        {
            float now = Time.time;
            while (_samples.Count > 0 && now - _samples[0].Born > Lifetime) _samples.RemoveAt(0);

            if (_samples.Count < 2) { _mesh.Clear(); return; }

            var cam = _camera != null ? _camera : Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : transform.position + Vector3.back;

            _verts.Clear(); _uv0.Clear(); _uv1.Clear(); _tris.Clear();

            for (int i = 0; i < _samples.Count; i++)
            {
                var s = _samples[i];

                Vector3 fwd = (i == 0) ? _samples[1].Position - s.Position
                            : (i == _samples.Count - 1) ? s.Position - _samples[i - 1].Position
                            : _samples[i + 1].Position - _samples[i - 1].Position;
                if (fwd.sqrMagnitude < 1e-12f) fwd = Vector3.forward;

                Vector3 toEye = eye - s.Position;
                Vector3 side = Vector3.Cross(fwd, toEye);
                if (side.sqrMagnitude < 1e-12f) side = Vector3.Cross(fwd, Vector3.up);
                side = side.normalized * Width;

                // Age 0 at the newest sample: the shader brightens the head and fades the tail,
                // so the ribbon reads as motion rather than as a static wire.
                float age = Mathf.Clamp01((now - s.Born) / Lifetime);

                // Local space: the renderer's transform is the world's frame.
                Vector3 local = transform.InverseTransformPoint(s.Position);
                Vector3 lSide = transform.InverseTransformVector(side);

                _verts.Add(local - lSide); _uv0.Add(new Vector2(age, 0f)); _uv1.Add(new Vector2(s.Speed, 0f));
                _verts.Add(local + lSide); _uv0.Add(new Vector2(age, 1f)); _uv1.Add(new Vector2(s.Speed, 0f));
            }

            for (int i = 0; i < _samples.Count - 1; i++)
            {
                int i0 = i * 2, i1 = i0 + 1, i2 = i0 + 2, i3 = i0 + 3;
                _tris.Add(i0); _tris.Add(i2); _tris.Add(i1);
                _tris.Add(i1); _tris.Add(i2); _tris.Add(i3);
            }

            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uv0);
            _mesh.SetUVs(1, _uv1);
            _mesh.SetTriangles(_tris, 0);
            _mesh.RecalculateBounds();
        }
    }
}
