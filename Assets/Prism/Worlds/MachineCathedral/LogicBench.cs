using System.Collections.Generic;
using Prism.Aesthetic;
using Prism.Core;
using Prism.Interaction;
using UnityEngine;

namespace Prism.Worlds.MachineCathedral
{
    /// <summary>
    /// A switch is a lever with two positions instead of a continuum, and once that is true, a
    /// switch that another circuit throws instead of a hand is a relay, and two switches wired
    /// together are a logic gate. This is the ladder upward the world is named for: the same
    /// TradeRig that swings a beam under a hand is what snaps a contact shut under a coil, because
    /// mechanically they are not two different things.
    ///
    /// Built and revealed at Connect. Two hand-thrown switches (A, B) drive a lamp; a third,
    /// identical switch acts as a momentary toggle that flips the lamp's wiring between series
    /// (AND) and parallel (OR); a relay, thrown by switch A's own circuit rather than a hand,
    /// shows the same mechanism operating without a hand at all. The lamp's state is never asserted
    /// — it is computed each frame from which switches are actually closed, exactly the way a real
    /// relay logic panel works, which is what makes this an honest AND/OR gate rather than an
    /// animation of one.
    /// </summary>
    public class LogicBench
    {
        readonly MachineCathedralWorld _world;

        public MechanicalSwitch SwitchA, SwitchB, Relay, TopologyToggle;
        bool _seriesWiring = true;
        bool _prevToggleClosed;

        Transform _root, _lamp, _relayLamp;
        Material _lampMat, _relayLampMat, _wireAMat, _wireBMat;
        Mesh _wireAMesh, _wireBMesh;

        public LogicBench(MachineCathedralWorld world) { _world = world; }

        public bool LampOn => _seriesWiring ? (SwitchA.Closed && SwitchB.Closed)
                                             : (SwitchA.Closed || SwitchB.Closed);
        public bool SeriesWiring => _seriesWiring;

        public void Build(Transform parent, Vector3 localOrigin)
        {
            var rootGo = new GameObject("LogicBench");
            _root = rootGo.transform;
            _root.SetParent(parent, false);
            _root.localPosition = localOrigin;

            SwitchA = new MechanicalSwitch(_world, handDriven: true);
            SwitchA.Build(_root, new Vector3(-0.06f, 0.02f, 0f), PrismPalette.Cyan);

            SwitchB = new MechanicalSwitch(_world, handDriven: true);
            SwitchB.Build(_root, new Vector3(-0.06f, 0.02f, 0.05f), PrismPalette.Mint);

            Relay = new MechanicalSwitch(_world, handDriven: false);
            Relay.Build(_root, new Vector3(-0.06f, 0.02f, -0.06f), PrismPalette.Coral);

            TopologyToggle = new MechanicalSwitch(_world, handDriven: true);
            TopologyToggle.Build(_root, new Vector3(-0.06f, 0.02f, 0.11f), PrismPalette.Lavender);

            _lampMat = PrismMaterials.CeramicBody(PrismPalette.Violet, 0.2f);
            var lampGo = new GameObject("GateLamp");
            lampGo.transform.SetParent(_root, false);
            lampGo.AddComponent<MeshFilter>().sharedMesh = PrismMesh.Icosphere(2);
            lampGo.AddComponent<MeshRenderer>().sharedMaterial = _lampMat;
            lampGo.transform.localPosition = new Vector3(0.03f, 0.03f, 0.025f);
            lampGo.transform.localScale = Vector3.one * 0.022f;
            _lamp = lampGo.transform;

            _relayLampMat = PrismMaterials.CeramicBody(PrismPalette.Violet, 0.2f);
            var rLampGo = new GameObject("RelayLamp");
            rLampGo.transform.SetParent(_root, false);
            rLampGo.AddComponent<MeshFilter>().sharedMesh = PrismMesh.Icosphere(2);
            rLampGo.AddComponent<MeshRenderer>().sharedMaterial = _relayLampMat;
            rLampGo.transform.localPosition = new Vector3(0.03f, 0.03f, -0.06f);
            rLampGo.transform.localScale = Vector3.one * 0.016f;
            _relayLamp = rLampGo.transform;

            _wireAMat = PrismMaterials.ForRelation(Relation.Causes, 0.8f);
            _wireBMat = PrismMaterials.ForRelation(Relation.Causes, 0.8f);
            _wireAMesh = new Mesh { name = "WireA" }; _wireAMesh.MarkDynamic();
            _wireBMesh = new Mesh { name = "WireB" }; _wireBMesh.MarkDynamic();
            MakeWire("WireA", _wireAMesh, _wireAMat);
            MakeWire("WireB", _wireBMesh, _wireBMat);

            _root.gameObject.SetActive(false);
        }

        void MakeWire(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        public void Show(bool on) => _root.gameObject.SetActive(on);

        readonly List<Vector3> _wirePts = new List<Vector3>(2);

        public void Tick(float dt)
        {
            SwitchA.Tick(dt, false);
            SwitchB.Tick(dt, false);
            TopologyToggle.Tick(dt, false);

            if (TopologyToggle.Closed && !_prevToggleClosed) _seriesWiring = !_seriesWiring;
            _prevToggleClosed = TopologyToggle.Closed;

            // The relay's coil is energised by switch A's own circuit, not by a hand -- the same
            // mechanism, now thrown electrically.
            Relay.Tick(dt, SwitchA.Closed);

            bool on = LampOn;
            _lampMat.SetColor("_Tint", on ? PrismPalette.Gold : PrismPalette.Violet * 0.5f);
            _lampMat.SetFloat("_Luminance", on ? 0.9f : 0.15f);
            _relayLampMat.SetColor("_Tint", Relay.Closed ? PrismPalette.Gold : PrismPalette.Violet * 0.5f);
            _relayLampMat.SetFloat("_Luminance", Relay.Closed ? 0.9f : 0.15f);

            float aLive = _seriesWiring ? (on ? 1f : 0.12f) : (SwitchA.Closed ? 1f : 0.12f);
            float bLive = _seriesWiring ? (on ? 1f : 0.12f) : (SwitchB.Closed ? 1f : 0.12f);
            _wireAMat.SetFloat("_Pulse", aLive);
            _wireBMat.SetFloat("_Pulse", bLive);

            _wirePts.Clear();
            _wirePts.Add(_root.InverseTransformPoint(SwitchA.ArmPosition));
            _wirePts.Add(_root.InverseTransformPoint(_lamp.position));
            PrismMesh.Tube(_wirePts, 0.0012f, 5, _wireAMesh);

            _wirePts.Clear();
            _wirePts.Add(_root.InverseTransformPoint(SwitchB.ArmPosition));
            _wirePts.Add(_root.InverseTransformPoint(_lamp.position));
            PrismMesh.Tube(_wirePts, 0.0012f, 5, _wireBMesh);

            if (on && SwitchA.Closed && SwitchB.Closed && _seriesWiring)
                _world.Loop.Evidence.Record(MachineEvidence.LogicAndSeen);
            if (on && !_seriesWiring && (SwitchA.Closed != SwitchB.Closed))
                _world.Loop.Evidence.Record(MachineEvidence.LogicOrSeen);
            if (SwitchA.Closed) _world.Loop.Evidence.Record(MachineEvidence.SwitchClosed);
            if (Relay.Closed) _world.Loop.Evidence.Record(MachineEvidence.RelayClosed);
        }
    }

    /// <summary>
    /// A lever with two positions. Hand-driven, it snaps closed while grasped and springs open on
    /// release, from the same TradeRig dynamics as every other station -- a constant push against a
    /// return spring, with real inertia and damping, not a boolean flipped by a trigger event. Coil-
    /// driven (a relay), the "push" comes from another circuit's state instead of a hand.
    /// </summary>
    public class MechanicalSwitch
    {
        public readonly TradeRig Rig = new TradeRig();

        const float MaxAngle = 0.6f;
        const float CloseThreshold = 0.7f;
        // Return spring, inertia and push force are tuned for a snappy, numerically stable toggle
        // rather than derived from a real switch's mass -- unlike the three main stations, where
        // every constant traces back to a stated physical quantity. See NOTES.md. The one thing
        // that IS load-bearing here: omega = sqrt(ReturnK/EffMass) must satisfy omega*FixedStep < 2
        // for TradeRig's semi-implicit step to stay stable (163 rad/s * 1/120 s =~ 1.4, comfortably
        // inside it), and PushForce/ReturnK must clear MaxAngle so the arm actually reaches its
        // hard stop rather than settling short of "closed".
        const float PushForce = 35f;

        readonly MachineCathedralWorld _world;
        readonly bool _handDriven;
        Transform _pivot, _arm;
        Material _armMat;
        PrismHands.Hand _hand;

        public MechanicalSwitch(MachineCathedralWorld world, bool handDriven)
        {
            _world = world;
            _handDriven = handDriven;
            Rig.QMin = 0f;
            Rig.QMax = MaxAngle;
            Rig.ReturnK = 40f;
            Rig.Damping = 0.15f;
            Rig.EffMass = 0.0015f;
        }

        public bool Closed => Rig.Q >= MaxAngle * CloseThreshold;
        public Vector3 ArmPosition => _arm != null ? _arm.position : Vector3.zero;

        public void Build(Transform parent, Vector3 localOrigin, Color tint)
        {
            var structural = PrismMaterials.CeramicBody(PrismPalette.Gold, 0.22f);

            var baseGo = new GameObject("SwitchBase");
            baseGo.transform.SetParent(parent, false);
            baseGo.transform.localPosition = localOrigin;
            baseGo.AddComponent<MeshFilter>().sharedMesh = MachineMesh.Box(new Vector3(0.03f, 0.006f, 0.018f));
            baseGo.AddComponent<MeshRenderer>().sharedMaterial = structural;

            var pivotGo = new GameObject("Pivot");
            _pivot = pivotGo.transform;
            _pivot.SetParent(baseGo.transform, false);
            _pivot.localPosition = new Vector3(-0.013f, 0.004f, 0f);

            _armMat = PrismMaterials.CeramicBody(tint, 0.5f);
            var armGo = new GameObject("Arm");
            armGo.transform.SetParent(_pivot, false);
            armGo.AddComponent<MeshFilter>().sharedMesh = MachineMesh.Box(new Vector3(0.022f, 0.0035f, 0.0035f));
            armGo.AddComponent<MeshRenderer>().sharedMaterial = _armMat;
            armGo.transform.localPosition = new Vector3(0.011f, 0f, 0f);
            _arm = armGo.transform;
        }

        /// <summary>externalEnergised drives a coil-based switch (a relay); ignored for a
        /// hand-driven one, which reads its own grasp instead.</summary>
        public void Tick(float dt, bool externalEnergised)
        {
            bool active;
            if (_handDriven)
            {
                if (_hand == null)
                {
                    var h = _world.NearestGraspingHand(_arm.position, MachineConstants.GrabRadius);
                    if (h != null) _hand = h;
                }
                else if (!_hand.IsTracked || !_hand.IsGrasping)
                {
                    _hand = null;
                }
                active = _hand != null;
            }
            else
            {
                active = externalEnergised;
            }

            Rig.AdvanceConstant(dt, active ? PushForce : 0f);
            _pivot.localRotation = Quaternion.Euler(0f, 0f, Rig.Q * Mathf.Rad2Deg);
            _armMat.SetColor("_Tint", MachineConstants.ForceColour(Closed ? PushForce : 0f));
        }
    }
}
