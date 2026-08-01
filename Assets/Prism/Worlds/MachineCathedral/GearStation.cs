using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using Prism.Interaction;
using UnityEngine;

namespace Prism.Worlds.MachineCathedral
{
    /// <summary>
    /// A hand crank meshed to a swappable driven gear, which shares a shaft with a drum that winds
    /// a rope to a hanging weight. This is the WONDER mechanism — turn the crank, something heavy
    /// rises at the far end — and the station the learner returns to for Explain's speed
    /// prediction, because a gear pair is where "predict the output speed" means something concrete.
    ///
    /// GEOMETRY CONVENTION: every shaft in this station spins about LOCAL Z, which is also the axis
    /// <see cref="MachineMesh.Extrude"/> builds gears and cylinders around — so a gear or the drum
    /// needs no reorientation to spin correctly, and the learner sees each gear face-on, teeth and
    /// mesh point clearly visible, like looking at a clock movement. Only the crank arm (a plain
    /// bar, not an extrusion) is positioned to sweep the perpendicular X-Y plane.
    ///
    /// PHYSICS NOTE: unlike the lever, a gear's torque arm does not change as it turns, so
    /// omega_out = omega_in * N_in/N_out and the drum's rope-length-per-radian are both exact and
    /// LINEAR relationships. That is why this station needs no geometric correction term — see
    /// <see cref="Tick"/> and compare with LeverStation's class comment.
    /// </summary>
    public class GearStation
    {
        public readonly TradeRig Rig = new TradeRig();

        public const int DriverTeeth = 12;
        public static readonly int[] DrivenOptions = { 6, 12, 18, 24 };
        const float Module = 0.005f;
        const float GearThickness = 0.012f;
        const float DrumRadius = 0.012f;
        const float CrankRadius = 0.045f;
        const float MaxLift = 0.10f;
        const float RestDrop = 0.14f;          // rope length below the drum when Output = 0

        readonly MachineCathedralWorld _world;

        Transform _root, _crankAnchor, _crank, _crankHandle, _driverGear, _drivenAnchor, _drivenGear, _drum, _weight, _rack;
        readonly List<Transform> _rackGears = new List<Transform>();
        Material _crankMat, _weightMat, _drivenMat;
        Mesh _ropeMesh, _drivenMesh;
        GameObject _ropeGo;
        readonly List<Vector3> _ropePts = new List<Vector3>(2);

        int _drivenIndex = 1;                 // starts at 12 teeth, R = 1
        float _loadMassKg = 0.10f;
        float _drivenPhysAngle;               // accumulated visual angle of the driven shaft, radians

        PrismHands.Hand _crankHand;
        float _evidenceCooldown;

        public GearStation(MachineCathedralWorld world) { _world = world; }

        public int DrivenTeeth => DrivenOptions[_drivenIndex];
        public float DisplayRatio { get; private set; } = 1f;
        public Vector3 WeightPosition => _weight != null ? _weight.position : Vector3.zero;
        public Vector3 CrankHandlePosition => _crankHandle != null ? _crankHandle.position : Vector3.zero;
        /// <summary>Live angular speed ratio of the output shaft to the input crank right now.</summary>
        public float SpeedRatioOutOverIn => (float)DriverTeeth / DrivenTeeth;
        public float MaxLiftMetres => MaxLift;
        /// <summary>Attachment point for other systems (challenges) that want to sit near this
        /// station without reaching into its private geometry.</summary>
        public Transform Root => _root;

        /// <summary>World position the weight would occupy at a given fraction of its full travel,
        /// for placing a fixed target marker independent of where the weight currently is.</summary>
        public Vector3 HeightMarkerPosition(float liftFraction)
        {
            float lift = Mathf.Clamp01(liftFraction) * MaxLift;
            return _drum.position + (-_root.up) * (RestDrop - lift);
        }

        public void Build(Transform parent, Vector3 localOrigin)
        {
            var rootGo = new GameObject("GearStation");
            _root = rootGo.transform;
            _root.SetParent(parent, false);
            _root.localPosition = localOrigin;

            var structural = PrismMaterials.CeramicBody(PrismPalette.Gold, 0.28f);

            var crankAnchorGo = new GameObject("CrankAnchor");
            _crankAnchor = crankAnchorGo.transform;
            _crankAnchor.SetParent(_root, false);
            _crankAnchor.localPosition = new Vector3(0f, 0.05f, 0f);

            // _crank is a pure pivot at the shaft origin: its rotation IS the crank angle. The arm
            // mesh and the handle are children offset outward, so rotating the pivot swings them
            // around the true shaft axis rather than around their own midpoint.
            var crankGo = new GameObject("Crank");
            _crank = crankGo.transform;
            _crank.SetParent(_crankAnchor, false);
            _crankMat = PrismMaterials.CeramicBody(MachineConstants.ForceColour(0f), 0.42f);

            var arm = MakePart(_crank, "Arm", MachineMesh.Box(new Vector3(0.006f, CrankRadius, 0.006f)), _crankMat);
            arm.localPosition = new Vector3(0f, CrankRadius * 0.5f, 0f);

            _crankHandle = MakePart(_crank, "CrankHandle", PrismMesh.Icosphere(2), _crankMat);
            _crankHandle.localPosition = new Vector3(0f, CrankRadius, 0f);
            _crankHandle.localScale = Vector3.one * 0.018f;

            _driverGear = MakePart(_crankAnchor, "DriverGear",
                MachineMesh.Gear(DriverTeeth, Module, GearThickness), structural);

            var drivenAnchorGo = new GameObject("DrivenAnchor");
            _drivenAnchor = drivenAnchorGo.transform;
            _drivenAnchor.SetParent(_crankAnchor, false);

            _drivenMat = PrismMaterials.CeramicBody(MachineConstants.ForceColour(0f), 0.42f);
            _drivenGear = MakePart(_drivenAnchor, "DrivenGear", null, _drivenMat);

            _drum = MakePart(_drivenAnchor, "Drum",
                MachineMesh.Cylinder(DrumRadius, 0.014f, 14), structural);

            RebuildDriven();

            // The rope: a two-point tube from the drum's underside straight down to the weight.
            // Rebuilt every frame from the live drum position and weight height.
            _ropeGo = new GameObject("Rope");
            _ropeGo.transform.SetParent(_root, false);
            _ropeMesh = new Mesh { name = "GearRope" };
            _ropeMesh.MarkDynamic();
            _ropeGo.AddComponent<MeshFilter>().sharedMesh = _ropeMesh;
            _ropeGo.AddComponent<MeshRenderer>().sharedMaterial = structural;

            _weightMat = PrismMaterials.CeramicBody(MachineConstants.ForceColour(0f), 0.42f);
            _weight = MakePart(_root, "Weight", PrismMesh.Icosphere(2), _weightMat);
            _weight.localScale = Vector3.one * 0.020f;

            // A small rack of alternative driven gears. Grasping one mounts it immediately — a
            // touch-to-select gesture rather than a carry-and-place animation, kept simple on
            // purpose since the choice itself, not the choreography of swapping it, is the lesson.
            var rackGo = new GameObject("Rack");
            _rack = rackGo.transform;
            _rack.SetParent(_root, false);
            _rack.localPosition = new Vector3(0.10f, 0.06f, -0.02f);
            for (int i = 0; i < DrivenOptions.Length; i++)
            {
                var mat = PrismMaterials.CeramicBody(PrismPalette.Lavender, 0.4f);
                var g = MakePart(_rack, "Rack" + DrivenOptions[i],
                    MachineMesh.Gear(DrivenOptions[i], Module, GearThickness * 0.7f), mat);
                g.localPosition = new Vector3(0f, i * 0.03f, 0f);
                _rackGears.Add(g);
            }

            Rig.Damping = 0.08f;
        }

        void RebuildDriven()
        {
            float driverPitch = MachineMesh.PitchRadius(DriverTeeth, Module);
            float pitch = MachineMesh.PitchRadius(DrivenTeeth, Module);
            _drivenAnchor.localPosition = new Vector3(0f, -(driverPitch + pitch), 0f);
            _drivenMesh = MachineMesh.Gear(DrivenTeeth, Module, GearThickness, _drivenMesh);
            _drivenGear.GetComponent<MeshFilter>().sharedMesh = _drivenMesh;
        }

        static Transform MakePart(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            if (mesh != null) go.AddComponent<MeshFilter>().sharedMesh = mesh;
            else go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        public void SetLoadMass(float kg) => _loadMassKg = kg;

        /// <summary>Force a specific driven gear (used by the Explain prediction so the learner is
        /// tested on a configuration they did not necessarily just pick themselves).</summary>
        public void SetDrivenIndex(int index)
        {
            _drivenIndex = Mathf.Clamp(index, 0, DrivenOptions.Length - 1);
            RebuildDriven();
        }

        public void Tick(float dt)
        {
            float toothRatio = (float)DriverTeeth / DrivenTeeth;     // N_in / N_out
            DisplayRatio = (float)DrivenTeeth / DriverTeeth;         // R = N_out / N_in, mechanical advantage

            Rig.LoadForce = _loadMassKg * MachineConstants.Gravity;
            // Output (rope length wound) = q * DrumRadius * toothRatio, exactly and linearly, so
            // dOutput/dQ = DrumRadius*toothRatio and Ratio (its reciprocal) needs no correction.
            Rig.Ratio = 1f / (DrumRadius * toothRatio);
            Rig.EffMass = MachineConstants.HandleMass * CrankRadius * CrankRadius
                        + _loadMassKg * DrumRadius * DrumRadius * toothRatio * toothRatio;

            // Dynamic bound from the CURRENT accurate Output and CURRENT ratio only -- Output is an
            // accumulator, not Q/Ratio, because Ratio changes discretely on a gear swap, so only a
            // locally-valid bound (recomputed every frame) is honest. See TradeRig's class comment.
            Rig.QMax = Rig.Q + (MaxLift - Rig.Output) * Rig.Ratio;
            Rig.QMin = Rig.Q + (0f - Rig.Output) * Rig.Ratio;

            HandleRackGrab();

            float qBefore = Rig.Q;
            HandleCrankGrab(dt);
            float dq = Rig.Q - qBefore;

            // Visual: crank turns with q directly, in the X-Y plane (rotation about local Z).
            _crank.localRotation = Quaternion.Euler(0f, 0f, Rig.Q * Mathf.Rad2Deg);

            // Driven shaft turns opposite, at the exact tooth ratio for the motion that actually
            // happened this frame -- the honesty requirement this world was built against.
            _drivenPhysAngle += -dq * toothRatio;
            _drivenAnchor.localRotation = Quaternion.Euler(0f, 0f, _drivenPhysAngle * Mathf.Rad2Deg);

            UpdateRope();

            _crankMat.SetColor("_Tint", MachineConstants.ForceColour(Rig.LastEffort / Mathf.Max(CrankRadius, 1e-3f)));
            _weightMat.SetColor("_Tint", MachineConstants.ForceColour(Rig.LoadForce));
            _drivenMat.SetColor("_Tint", MachineConstants.ForceColour(Rig.LoadForce * DisplayRatio));

            _evidenceCooldown -= dt;
            bool moving = Mathf.Abs(Rig.QDot) > 0.3f;
            if (moving && _evidenceCooldown <= 0f)
            {
                _world.RecordDrive(DisplayRatio, MachineEvidence.StationGear);
                _evidenceCooldown = 0.22f;
            }
        }

        void UpdateRope()
        {
            Vector3 drumWorld = _drum.position;
            Vector3 down = -_root.up;
            float lift = Mathf.Clamp(Rig.Output, 0f, MaxLift);
            Vector3 weightWorld = drumWorld + down * (RestDrop - lift);
            _weight.position = weightWorld;

            _ropePts.Clear();
            _ropePts.Add(_root.InverseTransformPoint(drumWorld + down * DrumRadius));
            _ropePts.Add(_root.InverseTransformPoint(weightWorld));
            PrismMesh.Tube(_ropePts, 0.0018f, 5, _ropeMesh);
        }

        void HandleRackGrab()
        {
            for (int i = 0; i < _rackGears.Count; i++)
            {
                if (i == _drivenIndex) continue;
                var h = _world.NearestGraspingHand(_rackGears[i].position, MachineConstants.GrabRadius);
                if (h != null)
                {
                    SetDrivenIndex(i);
                    _world.Hands.Buzz(h, 0.2f, 0.05f);
                    break;
                }
            }
        }

        void HandleCrankGrab(float dt)
        {
            if (_crankHand == null)
            {
                var h = _world.NearestGraspingHand(_crankHandle.position, MachineConstants.GrabRadius);
                if (h != null) _crankHand = h;
            }
            else if (!_crankHand.IsTracked || !_crankHand.IsGrasping)
            {
                _crankHand = null;
            }

            if (_crankHand != null)
            {
                // A point starting at local (0, r, 0) rotated by angle Q about local Z lands at
                // (-r sinQ, r cosQ, 0) -- invert that to read an angle back out of a hand position.
                Vector3 rel = _crankAnchor.InverseTransformPoint(_crankHand.Position);
                float handAngleDeg = Mathf.Atan2(-rel.x, rel.y) * Mathf.Rad2Deg;
                float curDeg = Rig.Q * Mathf.Rad2Deg;
                float deltaDeg = Mathf.DeltaAngle(curDeg, handAngleDeg);
                float targetQ = Rig.Q + deltaDeg * Mathf.Deg2Rad;

                Rig.AdvanceSpring(dt, targetQ, MachineConstants.SpringK * CrankRadius * CrankRadius,
                                  MachineConstants.SpringC * CrankRadius * CrankRadius,
                                  MachineConstants.MaxEffortForce * CrankRadius);
            }
            else
            {
                Rig.AdvanceConstant(dt, 0f);
            }
        }
    }
}
