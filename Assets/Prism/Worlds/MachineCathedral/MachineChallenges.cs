using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using Prism.Interaction;
using UnityEngine;

namespace Prism.Worlds.MachineCathedral
{
    /// <summary>
    /// Apply, Explain and Create, evaluated against the live simulation exactly as
    /// <c>OrbitalChallenge</c> does it: no answer is ever typed or chosen from a list, only watched
    /// for.
    ///
    ///   Apply    the gear station's load is swapped for one heavier than the current gear ratio
    ///            can lift within the shared force budget (see MachineConstants.MaxEffortForce) —
    ///            reaching the marked height is only possible after mounting a bigger driven gear.
    ///   Explain  before the crank turns, the learner sets a bead on a track to their predicted
    ///            speed ratio, then turns the crank at least half a turn; a wrong guess is recorded
    ///            as a misconception and offered again with a new gear, never as a failure.
    ///   Create   the pulley's load is swapped for one that needs at least two supporting strands —
    ///            a specification (a load, a target height, the same force budget) rather than a
    ///            free play area.
    /// </summary>
    public class MachineChallenges : MonoBehaviour
    {
        public MachineCathedralWorld World;

        enum Mode { Idle, Apply, Explain, Create }
        Mode _mode = Mode.Idle;

        // ---- Apply: gear station, force budget ----
        const float ApplyLoadKg = 0.6f;
        const float ApplyTargetFraction = 0.9f;
        const float ApplyHoldSeconds = 0.8f;
        Transform _applyMarker;
        Material _applyMat;
        float _applyHeld;

        // ---- Explain: gear station, speed prediction ----
        const float TrackLength = 0.11f;
        const float TrackMin = 0.35f, TrackMax = 2.2f;
        Transform _trackBase, _bead;
        Material _beadMat;
        bool _predictionLocked;
        bool _beadHeldOnce;
        float _predictedRatio = 1f;
        float _crankAtLock;

        // ---- Create: pulley station, specification ----
        const float CreateLoadKg = 0.9f;
        const float CreateTargetFraction = 0.9f;
        const float CreateHoldSeconds = 0.8f;
        Transform _createMarker;
        Material _createMat;
        float _createHeld;

        void Awake()
        {
            BuildApplyMarker();
            BuildExplainTrack();
            BuildCreateMarker();
        }

        static Mesh RingMesh(float radius)
        {
            var pts = new List<Vector3>(25);
            for (int i = 0; i <= 24; i++)
            {
                float a = i / 24f * Mathf.PI * 2f;
                pts.Add(new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
            }
            var mesh = new Mesh { name = "MachineTargetRing" };
            PrismMesh.Tube(pts, 0.0015f, 6, mesh);
            return mesh;
        }

        void BuildApplyMarker()
        {
            var go = new GameObject("ApplyMarker");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = RingMesh(0.022f);
            _applyMat = PrismMaterials.ForRelation(Relation.Constrains, 0.85f);
            go.AddComponent<MeshRenderer>().sharedMaterial = _applyMat;
            _applyMarker = go.transform;
            go.SetActive(false);
        }

        void BuildCreateMarker()
        {
            var go = new GameObject("CreateMarker");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = RingMesh(0.028f);
            _createMat = PrismMaterials.ForRelation(Relation.Constrains, 0.85f);
            go.AddComponent<MeshRenderer>().sharedMaterial = _createMat;
            _createMarker = go.transform;
            go.SetActive(false);
        }

        void BuildExplainTrack()
        {
            var baseGo = new GameObject("ExplainTrack");
            baseGo.transform.SetParent(transform, false);
            var structural = PrismMaterials.CeramicBody(PrismPalette.Gold, 0.25f);
            baseGo.AddComponent<MeshFilter>().sharedMesh = MachineMesh.Box(new Vector3(TrackLength, 0.004f, 0.004f));
            baseGo.AddComponent<MeshRenderer>().sharedMaterial = structural;
            _trackBase = baseGo.transform;
            baseGo.SetActive(false);

            _beadMat = PrismMaterials.CeramicBody(PrismPalette.Lavender, 0.5f);
            var beadGo = new GameObject("PredictionBead");
            beadGo.transform.SetParent(_trackBase, false);
            beadGo.AddComponent<MeshFilter>().sharedMesh = PrismMesh.Icosphere(2);
            beadGo.AddComponent<MeshRenderer>().sharedMaterial = _beadMat;
            beadGo.transform.localScale = Vector3.one * 0.014f;
            _bead = beadGo.transform;
        }

        // -----------------------------------------------------------------

        public void BeginApply()
        {
            _mode = Mode.Apply;
            World.Gear.SetLoadMass(ApplyLoadKg);
            _applyMarker.position = World.Gear.HeightMarkerPosition(ApplyTargetFraction);
            _applyMarker.gameObject.SetActive(true);
            _applyHeld = 0f;
        }

        public void BeginExplain()
        {
            _mode = Mode.Explain;
            World.Gear.SetDrivenIndex(Random.Range(0, GearStation.DrivenOptions.Length));
            _predictionLocked = false;
            _beadHeldOnce = false;
            SetBeadFraction(0.5f);

            _trackBase.SetParent(World.Gear.Root, false);
            _trackBase.localPosition = new Vector3(-0.10f, 0.10f, 0.02f);
            _trackBase.gameObject.SetActive(true);
        }

        public void BeginCreate()
        {
            _mode = Mode.Create;
            World.Pulley.SetLoadMass(CreateLoadKg);
            _createMarker.position = World.Pulley.HeightMarkerPosition(CreateTargetFraction);
            _createMarker.gameObject.SetActive(true);
            _createHeld = 0f;
        }

        void SetBeadFraction(float f)
        {
            f = Mathf.Clamp01(f);
            _predictedRatio = Mathf.Lerp(TrackMin, TrackMax, f);
            _bead.localPosition = new Vector3((f - 0.5f) * TrackLength, 0f, 0f);
        }

        public void Evaluate(float dt)
        {
            switch (_mode)
            {
                case Mode.Apply:   EvaluateApply(dt);   break;
                case Mode.Explain: EvaluateExplain(dt); break;
                case Mode.Create:  EvaluateCreate(dt);  break;
            }
        }

        void EvaluateApply(float dt)
        {
            float target = World.Gear.MaxLiftMetres * ApplyTargetFraction;
            bool near = World.Gear.Rig.Output >= target - 0.01f;
            _applyHeld = near ? _applyHeld + dt : 0f;
            _applyMat.SetFloat("_Pulse", Mathf.Clamp01(_applyHeld / ApplyHoldSeconds));

            if (_applyHeld >= ApplyHoldSeconds)
            {
                World.Loop.Evidence.Record(MachineEvidence.ApplySuccess);
                World.Knowledge?.Confirm(World.PrimaryConceptId, 0.3f);
                _applyMarker.gameObject.SetActive(false);
                _mode = Mode.Idle;
            }
        }

        void EvaluateExplain(float dt)
        {
            if (!_predictionLocked)
            {
                var h = World.NearestGraspingHand(_bead.position, MachineConstants.GrabRadius);
                if (h != null)
                {
                    _beadHeldOnce = true;
                    float localX = _trackBase.InverseTransformPoint(h.Position).x;
                    SetBeadFraction(localX / TrackLength + 0.5f);
                }
                else if (_beadHeldOnce)
                {
                    _predictionLocked = true;
                    _crankAtLock = World.Gear.Rig.Q;
                    World.Loop.Evidence.Record(MachineEvidence.ExplainAttempt);
                }
                return;
            }

            if (Mathf.Abs(World.Gear.Rig.Q - _crankAtLock) < Mathf.PI) return;

            float actual = World.Gear.SpeedRatioOutOverIn;
            bool good = Mathf.Abs(actual - _predictedRatio) < actual * 0.2f + 0.05f;

            if (good)
            {
                World.Loop.Evidence.Record(MachineEvidence.ExplainCorrect);
                World.Knowledge?.Confirm(World.PrimaryConceptId, 0.35f);
                _trackBase.gameObject.SetActive(false);
                _mode = Mode.Idle;
            }
            else
            {
                World.Knowledge?.FlagMisconception(World.PrimaryConceptId, 0.15f);
                _predictionLocked = false;
                _beadHeldOnce = false;
                World.Gear.SetDrivenIndex(Random.Range(0, GearStation.DrivenOptions.Length));
                SetBeadFraction(0.5f);
            }
        }

        void EvaluateCreate(float dt)
        {
            float target = World.Pulley.MaxLiftMetres * CreateTargetFraction;
            bool near = World.Pulley.Rig.Output >= target - 0.008f;
            _createHeld = near ? _createHeld + dt : 0f;
            _createMat.SetFloat("_Pulse", Mathf.Clamp01(_createHeld / CreateHoldSeconds));

            if (_createHeld >= CreateHoldSeconds)
            {
                World.Loop.Evidence.Record(MachineEvidence.CreateSuccess);
                World.Knowledge?.Confirm(World.PrimaryConceptId, 0.4f);
                _createMarker.gameObject.SetActive(false);
                _mode = Mode.Idle;
            }
        }
    }
}
