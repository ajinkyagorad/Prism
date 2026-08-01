using System.Collections.Generic;
using Prism.Atrium;
using Prism.Interaction;
using Prism.Worlds;
using Prism.Worlds.Orbital;
using UnityEngine;

namespace Prism.Core
{
    /// <summary>
    /// Owns the learner, and moves them between the atrium and a world.
    ///
    /// The transition is a SCALE TRANSITION, not a scene load. The concept the learner is holding
    /// expands around them until they are inside it, while the constellation rushes past and away.
    /// That is the brief's own grammar — scale transitions connect levels of reality — and it has a
    /// practical virtue too: because both places exist in one scene at all times, there is no
    /// loading screen, no additive-scene bookkeeping, and no way for a build to become a one-way
    /// door into a world with no route home.
    ///
    /// Passthrough follows the same logic. The atrium is an immense white VR space; a table-top
    /// simulation belongs in the learner's actual room. So entering a world dissolves the white
    /// space into passthrough, and leaving it brings the white space back.
    /// </summary>
    public class PrismSession : MonoBehaviour
    {
        [Header("Wiring (assigned by PrismSceneBuilder)")]
        public PrismHands Hands;
        public Camera Head;
        public KnowledgeAtrium Atrium;
        public OrbitalWorld World;
        public Companion.PrismCompanion Companion;

        /// <summary>
        /// Every other Concept World, discovered by reflection and instantiated by the scene
        /// builder. Orbital Mechanics predates PrismWorldBase and is kept as its own field rather
        /// than retrofitted — it is the one world that has been through three device tests, and
        /// rewriting it to gain uniformity would risk the only thing known to work.
        /// </summary>
        public List<PrismWorldBase> Worlds = new List<PrismWorldBase>();

        [Header("Content")]
        public ConceptGraph Graph;

        [Header("Feel")]
        [Tooltip("Seconds for the scale transition either way.")]
        public float TransitionSeconds = 1.6f;

        [Tooltip("Seconds both grips must be held to leave a world.")]
        public float ReturnHoldSeconds = 1.4f;

        [Tooltip("Dissolve the landscape into the learner's real room when inside a world. " +
                 "Off by default: passthrough needs a runtime permission, and a denied permission " +
                 "leaves the learner staring at nothing with no way to tell why.")]
        public bool PassthroughInWorlds = false;

        [Header("Environment")]
        public Scenery.PrismEnvironment Scenery;

        public KnowledgeState Knowledge { get; private set; }

        enum Place { Atrium, EnteringWorld, InWorld, LeavingWorld }
        Place _place = Place.Atrium;
        float _t;
        float _returnHeld;
        Transform _worldRoot;
        Transform _atriumRoot;

        void Awake()
        {
            if (Graph == null)
                Debug.LogError("[PRISM] PrismSession has no ConceptGraph. Nothing will be learnable.");

            Knowledge = new KnowledgeState(Graph);
            Knowledge.Load();

            if (Atrium != null)
            {
                Atrium.Knowledge = Knowledge;
                Atrium.Graph = Graph;
                Atrium.Hands = Hands;
                Atrium.Head = Head;
                Atrium.Companion = Companion;
                Atrium.WorldEntered += OnWorldEntered;
                _atriumRoot = Atrium.transform;
            }

            if (World != null)
            {
                World.Knowledge = Knowledge;
                World.Hands = Hands;
                World.Head = Head;
                World.SetCompanion(Companion);
                _worldRoot = World.transform;
                _worldRoot.gameObject.SetActive(false);
            }

            foreach (var w in Worlds)
            {
                if (w == null) continue;
                w.Hands = Hands;
                w.Head = Head;
                w.Knowledge = Knowledge;
                w.Companion = Companion;
            }

            if (Companion != null) Companion.Head = Head;

            SetPassthrough(false);
        }

        void OnWorldEntered(string worldId)
        {
            if (_place != Place.Atrium) return;

            if (worldId == "orbital" && World != null)
            {
                _active = null;
                _worldRoot = World.transform;
                _worldRoot.gameObject.SetActive(true);
                World.PlaceInFrontOfUser();
                World.EnterShell();
            }
            else
            {
                var target = Worlds.Find(w => w != null && w.WorldId == worldId);
                if (target == null)
                {
                    // An honest dead end rather than a silent one: saying so is better than
                    // nothing happening, which is indistinguishable from a broken build.
                    Debug.Log($"[PRISM] World '{worldId}' is not in this build yet.");
                    Companion?.Attend(Head.transform.position + Head.transform.forward * 0.6f,
                                      "learner reached for a world that does not exist yet", worldId, 0.3f);
                    return;
                }

                _active = target;
                _worldRoot = target.transform;
                target.Enter();          // builds lazily on first entry
            }

            _place = Place.EnteringWorld;
            _t = 0f;
            SetPassthrough(true);
            Debug.Log($"[PRISM] Entering world '{worldId}'.");
        }

        PrismWorldBase _active;

        void Update()
        {
            switch (_place)
            {
                case Place.Atrium:
                    break;

                case Place.EnteringWorld:
                    _t += Time.deltaTime / Mathf.Max(TransitionSeconds, 0.01f);
                    ApplyTransition(_t);
                    if (_t >= 1f)
                    {
                        _place = Place.InWorld;
                        if (_atriumRoot != null) _atriumRoot.gameObject.SetActive(false);
                    }
                    break;

                case Place.InWorld:
                    ServiceReturnGesture();
                    break;

                case Place.LeavingWorld:
                    _t -= Time.deltaTime / Mathf.Max(TransitionSeconds, 0.01f);
                    ApplyTransition(_t);
                    if (_t <= 0f)
                    {
                        _place = Place.Atrium;
                        if (_active != null) { _active.Leave(); _active = null; }
                        else if (_worldRoot != null)
                        {
                            World?.LeaveShell();
                            _worldRoot.gameObject.SetActive(false);
                        }
                        _worldRoot = null;
                        Atrium?.RefreshAll();
                        Knowledge.Save();
                    }
                    break;
            }
        }

        /// <summary>
        /// t = 0 is the atrium, t = 1 is inside the world. The world grows from the size of a held
        /// concept; the constellation grows past the learner and is left behind.
        /// </summary>
        void ApplyTransition(float t)
        {
            t = Mathf.Clamp01(t);
            float s = Mathf.SmoothStep(0f, 1f, t);

            if (_worldRoot != null)
                _worldRoot.localScale = Vector3.one * Mathf.Lerp(0.04f, 1f, s);

            if (_atriumRoot != null)
            {
                _atriumRoot.gameObject.SetActive(t < 0.999f);
                // Rushing outward rather than fading: the constellation is still there, the
                // learner has simply gone inside one of its stars.
                _atriumRoot.localScale = Vector3.one * Mathf.Lerp(1f, 5.5f, s);
            }
        }

        /// <summary>
        /// Leaving a world: hold both grips. A bodily gesture rather than a button, and one nobody
        /// performs by accident — but the secondary face button does it too, because a learner who
        /// wants out and cannot find the way out is the worst failure this product can have.
        /// </summary>
        void ServiceReturnGesture()
        {
            bool bothGrips = Hands != null
                          && Hands.Left != null && Hands.Right != null
                          && Hands.Left.IsTracked && Hands.Right.IsTracked
                          && Hands.Left.Grip > 0.7f && Hands.Right.Grip > 0.7f;

            _returnHeld = bothGrips ? _returnHeld + Time.deltaTime : 0f;

            bool button = OVRInput.GetDown(OVRInput.Button.Two)          // B / Y
                       || OVRInput.GetDown(OVRInput.Button.Start);

            if (_returnHeld >= ReturnHoldSeconds || button)
            {
                _returnHeld = 0f;
                _place = Place.LeavingWorld;
                _t = 1f;
                if (_atriumRoot != null) _atriumRoot.gameObject.SetActive(true);
                Atrium?.PlaceAroundUser();
                SetPassthrough(false);
                Knowledge.Save();
                Debug.Log("[PRISM] Returning to the atrium.");
            }
        }

        void SetPassthrough(bool on)
        {
            bool want = on && PassthroughInWorlds;
            if (OVRManager.instance != null)
                OVRManager.instance.isInsightPassthroughEnabled = want;

            // The landscape and passthrough are mutually exclusive: one of them is the background.
            Scenery?.SetVisible(!want);

            if (Head != null)
            {
                Head.clearFlags = CameraClearFlags.SolidColor;
                // With the sky dome drawn there is nothing to clear to; the colour only shows if
                // the dome is hidden, in which case passthrough wants transparent black.
                Head.backgroundColor = want ? new Color(0, 0, 0, 0) : new Color(0.05f, 0.06f, 0.08f);
            }
        }

        void OnApplicationPause(bool paused) { if (paused) Knowledge?.Save(); }
        void OnApplicationQuit() => Knowledge?.Save();
    }
}
