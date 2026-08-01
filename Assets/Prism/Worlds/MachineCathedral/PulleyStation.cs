using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using Prism.Interaction;
using UnityEngine;

namespace Prism.Worlds.MachineCathedral
{
    /// <summary>
    /// A block and tackle: a fixed crossbar, a movable block carrying the load, and a free end the
    /// learner hauls toward themselves. Wrap count N (how many strands support the block) is set by
    /// grasping one of four pegs. Mechanical advantage is exactly N and velocity ratio is exactly N
    /// — the one station in this world that cannot demonstrate the distance-multiplying half of the
    /// straddle, and NOTES.md says so rather than hiding it: a simple block and tackle only ever
    /// trades speed away for force, never the other way, which is itself a true and useful thing to
    /// notice once the lever and the gears have shown both directions.
    ///
    /// A hauled rope has no natural rest position the way a beam or a crank does — pulling enough
    /// rope to raise a load through N strands can mean several times that length through the hand.
    /// Real block-and-tackle rigs solve this by hauling hand over hand, cleating off between pulls
    /// so tension is never lost while the free hand repositions. This station models the CLEAT
    /// rather than the hand-over-hand choreography: releasing the rope holds it exactly where it is
    /// instead of letting the load fall, which is what a cleat is for. See <see cref="Tick"/>.
    /// </summary>
    public class PulleyStation
    {
        public readonly TradeRig Rig = new TradeRig();

        const float MaxLift = 0.06f;
        const float CrossbarHeight = 0.17f;
        const float BlockGap = 0.03f;
        const float StrandSpacing = 0.009f;
        const float RestZ = 0.09f;
        public const int MaxWraps = 4;

        readonly MachineCathedralWorld _world;

        Transform _root, _crossbar, _block, _pullHandle;
        readonly List<Transform> _strands = new List<Transform>();
        readonly List<Mesh> _strandMeshes = new List<Mesh>();
        readonly List<Transform> _pegs = new List<Transform>();
        Material _handleMat, _loadMat;
        Transform _load;

        int _wraps = 1;
        float _loadMassKg = 0.10f;

        PrismHands.Hand _pullHand;
        float _evidenceCooldown;

        public PulleyStation(MachineCathedralWorld world) { _world = world; }

        public int Wraps => _wraps;
        public float DisplayRatio => _wraps;
        public Vector3 HandlePosition => _pullHandle != null ? _pullHandle.position : Vector3.zero;
        public Vector3 LoadPosition => _load != null ? _load.position : Vector3.zero;
        public float MaxLiftMetres => MaxLift;
        public Transform Root => _root;

        /// <summary>World position the block would occupy at a given fraction of its full travel,
        /// for placing a fixed target marker independent of where the block currently is.</summary>
        public Vector3 HeightMarkerPosition(float liftFraction)
        {
            float lift = Mathf.Clamp01(liftFraction) * MaxLift;
            return _root.TransformPoint(new Vector3(0f, CrossbarHeight - BlockGap - MaxLift + lift, 0f));
        }

        public void Build(Transform parent, Vector3 localOrigin)
        {
            var rootGo = new GameObject("PulleyStation");
            _root = rootGo.transform;
            _root.SetParent(parent, false);
            _root.localPosition = localOrigin;

            var structural = PrismMaterials.CeramicBody(PrismPalette.Gold, 0.28f);

            // Two uprights and a crossbar, all simple boxes.
            for (int side = -1; side <= 1; side += 2)
            {
                var post = MakePart(_root, "Post", MachineMesh.Cylinder(0.006f, CrossbarHeight, 8), structural);
                post.localPosition = new Vector3(side * 0.045f, CrossbarHeight * 0.5f, 0f);
                post.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            }
            _crossbar = MakePart(_root, "Crossbar", MachineMesh.Box(new Vector3(0.10f, 0.012f, 0.012f)), structural);
            _crossbar.localPosition = new Vector3(0f, CrossbarHeight, 0f);

            _block = MakePart(_root, "Block", MachineMesh.Box(new Vector3(0.03f, 0.016f, 0.016f)), structural);

            _loadMat = PrismMaterials.CeramicBody(MachineConstants.ForceColour(0f), 0.42f);
            _load = MakePart(_block, "Load", PrismMesh.Icosphere(2), _loadMat);
            _load.localPosition = new Vector3(0f, -0.03f, 0f);
            _load.localScale = Vector3.one * 0.020f;

            for (int i = 0; i < MaxWraps; i++)
            {
                var go = new GameObject("Strand" + i);
                go.transform.SetParent(_root, false);
                var mesh = new Mesh { name = "Strand" + i };
                mesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = structural;
                _strands.Add(go.transform);
                _strandMeshes.Add(mesh);
            }

            _handleMat = PrismMaterials.CeramicBody(MachineConstants.ForceColour(0f), 0.45f);
            _pullHandle = MakePart(_root, "PullHandle", PrismMesh.Icosphere(2), _handleMat);
            _pullHandle.localPosition = new Vector3(0.055f, 0.07f, RestZ);
            _pullHandle.localScale = Vector3.one * 0.018f;

            // Four pegs, one per wrap count. Grasping a peg sets N immediately, matching the gear
            // station's touch-to-select rack.
            for (int i = 0; i < MaxWraps; i++)
            {
                var mat = PrismMaterials.CeramicBody(PrismPalette.Lavender, 0.4f);
                var peg = MakePart(_root, "Peg" + (i + 1), PrismMesh.Icosphere(1), mat);
                peg.localPosition = new Vector3(-0.06f, 0.03f + i * 0.022f, -0.02f);
                peg.localScale = Vector3.one * 0.010f;
                _pegs.Add(peg);
            }

            Rig.Damping = 0.10f;
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

        public void SetWraps(int n)
        {
            _wraps = Mathf.Clamp(n, 1, MaxWraps);
        }

        public void Tick(float dt)
        {
            Rig.LoadForce = _loadMassKg * MachineConstants.Gravity;
            Rig.Ratio = _wraps;
            Rig.EffMass = MachineConstants.HandleMass + _loadMassKg / (_wraps * (float)_wraps);

            // Same locally-valid dynamic bound as the gear station, for the same reason: Ratio can
            // change discretely (a new wrap count) between frames.
            Rig.QMax = Rig.Q + (MaxLift - Rig.Output) * Rig.Ratio;
            Rig.QMin = Rig.Q + (0f - Rig.Output) * Rig.Ratio;

            HandlePegGrab();
            HandlePullGrab(dt);

            float lift = Mathf.Clamp(Rig.Output, 0f, MaxLift);
            _block.localPosition = new Vector3(0f, CrossbarHeight - BlockGap - MaxLift + lift, 0f);
            _pullHandle.localPosition = new Vector3(0.055f, 0.07f, RestZ - Rig.Q);

            UpdateStrands();

            _handleMat.SetColor("_Tint", MachineConstants.ForceColour(Rig.LastEffort));
            _loadMat.SetColor("_Tint", MachineConstants.ForceColour(Rig.LoadForce));

            _evidenceCooldown -= dt;
            bool moving = Mathf.Abs(Rig.QDot) > 0.02f;
            if (moving && _evidenceCooldown <= 0f)
            {
                _world.RecordDrive(DisplayRatio, MachineEvidence.StationPulley);
                _evidenceCooldown = 0.22f;
            }
        }

        readonly List<Vector3> _strandPts = new List<Vector3>(2);

        void UpdateStrands()
        {
            for (int i = 0; i < _strands.Count; i++)
            {
                bool active = i < _wraps;
                _strands[i].gameObject.SetActive(active);
                if (!active) continue;

                float offset = (i - (_wraps - 1) * 0.5f) * StrandSpacing;
                Vector3 top = _root.TransformPoint(new Vector3(offset, CrossbarHeight - 0.006f, 0f));
                Vector3 bottom = _block.position + _root.TransformVector(new Vector3(offset, 0.008f, 0f));

                _strandPts.Clear();
                _strandPts.Add(_root.InverseTransformPoint(top));
                _strandPts.Add(_root.InverseTransformPoint(bottom));
                PrismMesh.Tube(_strandPts, 0.0012f, 4, _strandMeshes[i]);
            }
        }

        void HandlePegGrab()
        {
            for (int i = 0; i < _pegs.Count; i++)
            {
                int n = i + 1;
                if (n == _wraps) continue;
                var h = _world.NearestGraspingHand(_pegs[i].position, MachineConstants.GrabRadius);
                if (h != null)
                {
                    SetWraps(n);
                    _world.Hands.Buzz(h, 0.2f, 0.05f);
                    break;
                }
            }
        }

        void HandlePullGrab(float dt)
        {
            if (_pullHand == null)
            {
                var h = _world.NearestGraspingHand(_pullHandle.position, MachineConstants.GrabRadius);
                if (h != null) _pullHand = h;
            }
            else if (!_pullHand.IsTracked || !_pullHand.IsGrasping)
            {
                _pullHand = null;
            }

            if (_pullHand != null)
            {
                float handLocalZ = _root.InverseTransformPoint(_pullHand.Position).z;
                float targetQ = RestZ - handLocalZ;
                Rig.AdvanceSpring(dt, targetQ, MachineConstants.SpringK, MachineConstants.SpringC,
                                  MachineConstants.MaxEffortForce);
            }
            else
            {
                // Cleated off: holds exactly where it is between pulls. See the class comment.
                Rig.QDot = 0f;
            }
        }
    }
}
