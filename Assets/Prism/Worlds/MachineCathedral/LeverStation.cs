using Prism.Aesthetic;
using Prism.Core;
using Prism.Interaction;
using UnityEngine;

namespace Prism.Worlds.MachineCathedral
{
    /// <summary>
    /// A first-class lever: a beam on a sliding fulcrum, effort at one end, a weighted pan at the
    /// other. The learner pushes the effort end and slides the fulcrum by hand; nothing else moves
    /// this beam.
    ///
    /// PHYSICS NOTE, because this is the one station where the generic <see cref="TradeRig"/>
    /// abstraction needs an honest footnote. The rig's generalised coordinate here is the beam
    /// angle theta — the natural DOF — but a lever's effort and load points travel on ARCS, not
    /// straight lines, so the torque arm each force acts through shrinks by cos(theta) as the beam
    /// tilts. That factor is real and is included: <see cref="Rig"/>.Ratio is set every frame to
    /// <c>1 / (d_l * cos(theta))</c> so that <c>LoadForce / Ratio</c> comes out to the true
    /// generalised load torque <c>m*g*d_l*cos(theta)</c>, and the hand's spring is written directly
    /// as a rotational spring in theta so its torque needs no separate arm multiplication. The
    /// MECHANICAL ADVANTAGE shown to the learner and used for evidence is the plain, exact
    /// <c>d_effort / d_load</c> — that ratio is what a lever means, and it is not approximated here;
    /// only the hand's TARGET angle (see HandleEffortGrab) uses a small-angle convenience, which
    /// affects nothing but how eagerly the spring chases the hand.
    ///
    /// INERTIA is the idealisation stated in TradeRig's class comment: the beam itself is massless;
    /// only a small handle mass and the real load mass carry inertia, as point masses at their own
    /// radius from the fulcrum (I = m*r^2, exact for a point mass, summed for two of them).
    /// </summary>
    public class LeverStation
    {
        public readonly TradeRig Rig = new TradeRig();

        const float BeamLength = 0.28f;
        const float BeamMargin = 0.035f;
        const float PostHeight = 0.065f;
        const float ThetaMax = 0.5f;             // ~29 degrees either way
        const float BeamHalfThick = 0.007f;

        readonly MachineCathedralWorld _world;

        Transform _root, _pivotAnchor, _beam, _effortHandle, _loadPan, _railBase, _fulcrumBead, _post;
        Material _effortMat, _loadMat, _beadMat;
        Mesh _loadMesh;

        float _tf;                                // fulcrum offset from beam centre, metres
        float _loadMassKg = 0.15f;
        float _handleMassKg = MachineConstants.HandleMass;

        PrismHands.Hand _handleHand, _fulcrumHand;
        float _evidenceCooldown;

        public LeverStation(MachineCathedralWorld world) { _world = world; }

        public float DisplayRatio { get; private set; } = 1f;
        public Vector3 HandlePosition => _effortHandle != null ? _effortHandle.position : Vector3.zero;
        public Vector3 LoadPosition => _loadPan != null ? _loadPan.position : Vector3.zero;

        public void Build(Transform parent, Vector3 localOrigin)
        {
            var rootGo = new GameObject("LeverStation");
            _root = rootGo.transform;
            _root.SetParent(parent, false);
            _root.localPosition = localOrigin;

            var structural = PrismMaterials.CeramicBody(PrismPalette.Gold, 0.28f);

            _post = MakePart(_root, "Post", MachineMesh.Cylinder(0.008f, PostHeight, 12), structural);
            _post.localPosition = new Vector3(0f, PostHeight * 0.5f, 0f);
            _post.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);

            var pivotGo = new GameObject("Pivot");
            _pivotAnchor = pivotGo.transform;
            _pivotAnchor.SetParent(_root, false);
            _pivotAnchor.localPosition = new Vector3(0f, PostHeight, 0f);

            var beamGo = new GameObject("Beam");
            _beam = beamGo.transform;
            _beam.SetParent(_pivotAnchor, false);
            var beamMat = PrismMaterials.CeramicBody(PrismPalette.Gold, 0.25f);
            beamGo.AddComponent<MeshFilter>().sharedMesh =
                MachineMesh.Box(new Vector3(BeamLength, BeamHalfThick * 2f, BeamHalfThick * 2f));
            beamGo.AddComponent<MeshRenderer>().sharedMaterial = beamMat;

            _effortMat = PrismMaterials.CeramicBody(MachineConstants.ForceColour(0f), 0.4f);
            _effortHandle = MakePart(_beam, "EffortHandle", PrismMesh.Icosphere(2), _effortMat);
            _effortHandle.localPosition = new Vector3(-BeamLength * 0.5f, 0f, 0f);
            _effortHandle.localScale = Vector3.one * 0.020f;

            _loadMat = PrismMaterials.CeramicBody(MachineConstants.ForceColour(0f), 0.4f);
            _loadMesh = MachineMesh.Cylinder(0.022f, 0.02f, 14);
            _loadPan = MakePart(_beam, "LoadPan", _loadMesh, _loadMat);
            _loadPan.localPosition = new Vector3(BeamLength * 0.5f, 0f, 0f);
            _loadPan.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);

            // A fixed, non-rotating rail below the beam. The fulcrum bead rides it directly at
            // local-x = t_f, and dragging the bead sets t_f 1:1 from the hand's position along the
            // rail — a plain kinematic control, not a dynamic DOF, because repositioning a fulcrum
            // is a setup action, not a force to integrate.
            var railGo = new GameObject("Rail");
            _railBase = railGo.transform;
            _railBase.SetParent(_root, false);
            _railBase.localPosition = new Vector3(0f, PostHeight * 0.35f, 0f);
            railGo.AddComponent<MeshFilter>().sharedMesh =
                MachineMesh.Box(new Vector3(BeamLength - BeamMargin, 0.004f, 0.004f));
            railGo.AddComponent<MeshRenderer>().sharedMaterial = structural;

            _beadMat = PrismMaterials.CeramicBody(PrismPalette.Lavender, 0.5f);
            _fulcrumBead = MakePart(_railBase, "FulcrumBead", PrismMesh.Icosphere(1), _beadMat);
            _fulcrumBead.localScale = Vector3.one * 0.016f;

            Rig.QMin = -ThetaMax;
            Rig.QMax = ThetaMax;
            Rig.ReturnK = 0f;
            Rig.Damping = 0.06f;
        }

        static Transform MakePart(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        public void SetLoadMass(float kg) => _loadMassKg = kg;

        public void Tick(float dt)
        {
            float theta = Rig.Q;
            float cosT = Mathf.Max(Mathf.Cos(theta), 0.05f);
            float de = BeamLength * 0.5f + _tf;
            float dl = BeamLength * 0.5f - _tf;
            DisplayRatio = de / Mathf.Max(dl, 1e-4f);

            Rig.LoadForce = _loadMassKg * MachineConstants.Gravity;
            Rig.Ratio = 1f / (dl * cosT);
            Rig.EffMass = _handleMassKg * de * de + _loadMassKg * dl * dl;

            HandleFulcrumGrab();
            HandleEffortGrab(dt, de);

            // Pose the beam: rotate about the (fixed) pivot, then slide so beam-local x = t_f sits
            // exactly on the pivot. See the class comment for why t_f-changes never appear as work.
            _beam.localRotation = Quaternion.Euler(0f, 0f, theta * Mathf.Rad2Deg);
            _beam.localPosition = _beam.localRotation * new Vector3(-_tf, 0f, 0f);
            _fulcrumBead.localPosition = new Vector3(_tf, 0f, 0f);

            _effortMat.SetColor("_Tint", MachineConstants.ForceColour(Rig.LastEffort / Mathf.Max(de, 1e-3f)));
            _loadMat.SetColor("_Tint", MachineConstants.ForceColour(Rig.LoadForce));

            _evidenceCooldown -= dt;
            bool moving = Mathf.Abs(Rig.QDot) > 0.05f;
            if (moving && _evidenceCooldown <= 0f)
            {
                _world.RecordDrive(DisplayRatio, MachineEvidence.StationLever);
                _evidenceCooldown = 0.22f;
            }
        }

        void HandleFulcrumGrab()
        {
            if (_fulcrumHand == null)
            {
                var h = _world.NearestGraspingHand(_fulcrumBead.position, MachineConstants.GrabRadius);
                if (h != null) _fulcrumHand = h;
                else return;
            }
            if (!_fulcrumHand.IsTracked || !_fulcrumHand.IsGrasping) { _fulcrumHand = null; return; }

            float localX = _railBase.InverseTransformPoint(_fulcrumHand.Position).x;
            _tf = Mathf.Clamp(localX, -(BeamLength * 0.5f - BeamMargin), BeamLength * 0.5f - BeamMargin);
        }

        void HandleEffortGrab(float dt, float de)
        {
            if (_handleHand == null)
            {
                var h = _world.NearestGraspingHand(_effortHandle.position, MachineConstants.GrabRadius);
                if (h != null) _handleHand = h;
            }
            else if (!_handleHand.IsTracked || !_handleHand.IsGrasping)
            {
                _handleHand = null;
            }

            if (_handleHand != null)
            {
                // Control-mapping convenience, not a physics approximation (see class comment):
                // the hand's height above/below the pivot maps linearly to a target angle.
                float localY = _pivotAnchor.InverseTransformPoint(_handleHand.Position).y;
                float targetTheta = Mathf.Clamp(-localY / Mathf.Max(de, 0.02f), -ThetaMax, ThetaMax);
                float maxTorque = MachineConstants.MaxEffortForce * de;
                Rig.AdvanceSpring(dt, targetTheta, MachineConstants.SpringK * de * de,
                                  MachineConstants.SpringC * de * de, maxTorque);
            }
            else
            {
                Rig.AdvanceConstant(dt, 0f);
            }
        }
    }
}
