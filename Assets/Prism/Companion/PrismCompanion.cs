using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using UnityEngine;

namespace Prism.Companion
{
    /// <summary>
    /// The learning companion. It has no body, no face and no text panel.
    ///
    /// It is a cloud of particles that follows the learner's attention, drawn as ONE mesh so the
    /// whole presence costs a single draw call. What it does is point: when the loop reports that
    /// a learner has been stuck in a stage for a while, the cloud gathers on the thing they have
    /// not yet tried — the arrowhead they have not grabbed, the ring they have not reached — and
    /// pulses. It demonstrates where to look; it does not explain.
    ///
    /// The brief's harder requirements (spoken questions, generated experiments, verbal
    /// explanations turned into 3D scenes) need a language model behind them. <see cref="Intent"/>
    /// is the seam for that: this class already decides WHAT should be communicated and when, and
    /// records it, so a later model has a well-defined job rather than an open-ended one.
    /// </summary>
    public class PrismCompanion : MonoBehaviour
    {
        [Tooltip("How many motes make up the presence. One mesh regardless of count.")]
        public int MoteCount = 28;

        [Tooltip("Metres. How tightly the cloud gathers when it is pointing at something.")]
        public float GatherRadius = 0.05f;

        [Tooltip("Metres. How loosely it drifts when it is merely present.")]
        public float IdleRadius = 0.22f;

        public Camera Head;

        /// <summary>
        /// What the companion currently wants to communicate, and why. Nothing renders this as
        /// words today; it is logged, and it is the contract a future spoken companion implements.
        /// </summary>
        public struct Intent
        {
            public string Reason;      // why the companion spoke up at all
            public string Subject;     // what it is pointing at
            public Vector3 Where;
            public float Urgency;      // 0 present, 1 the learner is properly stuck
        }

        public Intent Current { get; private set; }

        struct Mote
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float Phase;
            public float Size;
        }

        Mote[] _motes;
        Mesh _mesh;
        Material _mat;
        Transform _target;
        Vector3 _targetPoint;
        float _gather;
        PrismVoice _voice;

        readonly List<Vector3> _verts = new List<Vector3>();
        readonly List<Vector2> _uvs   = new List<Vector2>();
        readonly List<int>     _tris  = new List<int>();

        void Awake()
        {
            _motes = new Mote[Mathf.Max(4, MoteCount)];
            for (int i = 0; i < _motes.Length; i++)
            {
                _motes[i] = new Mote
                {
                    Position = Random.insideUnitSphere * IdleRadius,
                    Phase = Random.value,
                    Size = Random.Range(0.004f, 0.010f)
                };
            }

            _mesh = new Mesh { name = "CompanionMotes" };
            _mesh.MarkDynamic();

            // See ConstellationNode: ?? does not work with GetComponent, because UnityEngine.Object's
            // == overload is bypassed by reference-equality operators.
            if (!gameObject.TryGetComponent<MeshFilter>(out var mf)) mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = _mesh;

            // Prism/Mote, not Prism/Volumetric. Volumetric solves a chord through a UNIT SPHERE in
            // object space; these motes are quads whose object-space positions are metres from the
            // transform origin, so the chord came out zero and the entire companion rendered as
            // nothing at all. See PrismMote.shader.
            _mat = PrismMaterials.New(PrismMaterials.Mote);
            _mat.SetColor("_Tint", PrismPalette.Lavender);
            _mat.SetColor("_EdgeTint", PrismPalette.Cyan);
            _mat.SetFloat("_Density", 1.1f);

            if (!gameObject.TryGetComponent<MeshRenderer>(out var mr)) mr = gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;

            if (!gameObject.TryGetComponent<PrismVoice>(out _voice)) _voice = gameObject.AddComponent<PrismVoice>();
        }

        /// <summary>Attend to a moving thing.</summary>
        public void Attend(Transform target, string reason, string subject, float urgency = 0.5f)
        {
            _target = target;
            _targetPoint = target != null ? target.position : _targetPoint;
            Announce(reason, subject, urgency, _targetPoint);
        }

        /// <summary>Attend to a fixed point.</summary>
        public void Attend(Vector3 point, string reason, string subject, float urgency = 0.5f)
        {
            _target = null;
            _targetPoint = point;
            Announce(reason, subject, urgency, point);
        }

        public void Release()
        {
            _target = null;
            Current = default;
        }

        void Announce(string reason, string subject, float urgency, Vector3 where)
        {
            Current = new Intent { Reason = reason, Subject = subject, Where = where, Urgency = urgency };
            // This log line is the companion's whole vocabulary today. It is deliberately shaped
            // like something a model would be asked to say, not like a debug print.
            Debug.Log($"[PRISM-COMPANION] ({urgency:0.00}) {reason} -> attending to {subject}");
        }

        /// <summary>
        /// Called when a world's loop changes stage. The companion marks the transition and then
        /// gets out of the way: a new stage is exactly the wrong moment to start talking, because
        /// the learner has just succeeded at something and is about to look around.
        /// </summary>
        public void OnStage(LoopStage stage, LearningLoop loop)
        {
            _gather = 0f;
            Release();

            if (_voice != null && Head != null)
            {
                var where = Head.transform.position + Head.transform.forward * 0.8f;
                // Later stages settle lower: the pitch falls as the concept comes to rest.
                float pitch = Mathf.Lerp(1.25f, 0.85f, (int)stage / 7f);
                _voice.Consonance(where, 0.7f, pitch);
            }
        }

        void Update()
        {
            if (_target != null) _targetPoint = _target.position;

            bool pointing = Current.Urgency > 0.01f;
            _gather = Mathf.MoveTowards(_gather, pointing ? 1f : 0f, Time.deltaTime * 1.5f);

            Vector3 home = pointing
                ? _targetPoint
                : (Head != null ? Head.transform.position + Head.transform.forward * 0.75f
                                              + Head.transform.right * 0.28f
                                : transform.position);

            float radius = Mathf.Lerp(IdleRadius, GatherRadius, _gather);
            float pull = Mathf.Lerp(1.4f, 5.5f, _gather);
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);

            for (int i = 0; i < _motes.Length; i++)
            {
                var m = _motes[i];

                // Each mote orbits its own slowly moving point on a sphere around home, so the
                // cloud has internal life without any of them ever arriving anywhere.
                float t = Time.time * 0.35f + m.Phase * 20f;
                Vector3 want = home + new Vector3(
                    Mathf.Sin(t * 1.13f + m.Phase * 6.2f),
                    Mathf.Sin(t * 0.91f + m.Phase * 3.7f) * 0.7f,
                    Mathf.Cos(t * 1.07f + m.Phase * 5.1f)) * radius;

                m.Velocity += (want - m.Position) * pull * dt;
                m.Velocity *= Mathf.Exp(-2.6f * dt);          // damping, frame-rate independent
                m.Position += m.Velocity * dt;
                _motes[i] = m;
            }

            // A pointing cloud pulses; a present one does not.
            _mat.SetFloat("_Density", pointing ? 1.1f + 0.5f * Mathf.Sin(Time.time * 4f) : 0.85f);

            RebuildMesh();
        }

        /// <summary>
        /// Camera-facing quads in one mesh. Two triangles per mote, one draw call for the whole
        /// presence — which is the only reason a distributed companion is affordable on this GPU.
        /// </summary>
        void RebuildMesh()
        {
            var cam = Head != null ? Head : Camera.main;
            if (cam == null) { _mesh.Clear(); return; }

            Vector3 right = cam.transform.right;
            Vector3 up = cam.transform.up;

            _verts.Clear(); _uvs.Clear(); _tris.Clear();

            for (int i = 0; i < _motes.Length; i++)
            {
                var m = _motes[i];
                float s = m.Size * Mathf.Lerp(1f, 1.6f, _gather);
                Vector3 c = transform.InverseTransformPoint(m.Position);
                Vector3 r = transform.InverseTransformVector(right * s);
                Vector3 u = transform.InverseTransformVector(up * s);

                int b = _verts.Count;
                _verts.Add(c - r - u); _uvs.Add(new Vector2(0, 0));
                _verts.Add(c + r - u); _uvs.Add(new Vector2(1, 0));
                _verts.Add(c - r + u); _uvs.Add(new Vector2(0, 1));
                _verts.Add(c + r + u); _uvs.Add(new Vector2(1, 1));

                _tris.Add(b); _tris.Add(b + 2); _tris.Add(b + 1);
                _tris.Add(b + 1); _tris.Add(b + 2); _tris.Add(b + 3);
            }

            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetTriangles(_tris, 0);
            _mesh.RecalculateBounds();
        }

        public PrismVoice Voice => _voice;
    }
}
