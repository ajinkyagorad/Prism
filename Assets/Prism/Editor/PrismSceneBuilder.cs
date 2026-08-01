using System.Collections.Generic;
using System.IO;
using System.Linq;
using Prism.Atrium;
using Prism.Core;
using Prism.Interaction;
using Prism.Worlds.Orbital;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Prism.EditorTools
{
    /// <summary>
    /// Generates the PRISM scene from nothing.
    ///
    /// No prefabs, no template, no manual wiring. That is a deliberate constraint: this project is
    /// built on a machine with no headset preview and driven from a terminal, so a scene that has
    /// to be assembled by hand in an editor GUI is a scene that cannot be rebuilt reliably. Every
    /// reference this scene needs is assigned here, and PrismVerify fails loudly if one is not.
    ///
    ///   PRISM &gt; 3. Build Scene
    /// </summary>
    public static class PrismSceneBuilder
    {
        public const string ScenePath = "Assets/Prism/Scenes/Prism.unity";

        [MenuItem("PRISM/3. Build Scene", priority = 120)]
        public static void BuildScene()
        {
            if (Application.isPlaying)
            {
                // Editor scene operations throw "This cannot be used during play mode".
                Debug.LogError("[PRISM] Stop play mode before building the scene.");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Nothing in PRISM is lit by scene lights — every material carries its own optics — so
            // the scene has no lights at all. Ambient is set warm anyway so that anything added
            // later which DOES use standard lighting does not arrive pitch black.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = PrismPalette.Warm * 0.85f;
            RenderSettings.fog = false;

            var rig = BuildRig(out var head, out var hands);
            var environment = BuildEnvironment();
            var companion = BuildCompanion(head);
            var atrium = BuildAtrium(head);
            var world = BuildWorld();
            var session = BuildSession(hands, head, atrium, world, companion);
            session.Scenery = environment;
            session.Worlds = BuildDiscoveredWorlds();

            // A visible hand, and a log line per second. Both exist because the first build on a
            // headset could only be reported as "nothing happens", which was true and useless.
            atrium.LeftCursor  = BuildCursor(hands, false);
            atrium.RightCursor = BuildCursor(hands, true);
            BuildDiagnostics(hands, head, atrium, world, session);

            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            if (!saved) { Debug.LogError("[PRISM] Failed to save the scene."); return; }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            Debug.Log($"[PRISM] Scene built at {ScenePath}");
            PrismVerify.Verify();
        }

        public static void BuildSceneFromCommandLine()
        {
            BuildScene();
            EditorApplication.Exit(0);
        }

        // -----------------------------------------------------------------

        static OVRCameraRig BuildRig(out Camera head, out PrismHands hands)
        {
            var go = new GameObject("OVRCameraRig");
            var rig = go.AddComponent<OVRCameraRig>();
            var manager = go.AddComponent<OVRManager>();

            // Builds trackingSpace, the eye anchors and the hand anchors, and puts a camera on
            // CenterEyeAnchor. Called explicitly so the anchors exist NOW, at author time, and can
            // be referenced by everything below.
            rig.EnsureGameObjectIntegrity();

            head = rig.centerEyeAnchor.GetComponent<Camera>();
            if (head == null) head = rig.centerEyeAnchor.gameObject.AddComponent<Camera>();
            head.nearClipPlane = 0.02f;          // concepts are held at 20 cm; the default clips them

            // Must exceed PrismEnvironment.Extent (900 m) or the distant ranges are clipped away
            // and the ground ends in a hard circular edge partway into the valley. Quest uses
            // reversed-Z, so a 0.02 -> 2000 range keeps ample depth precision at these scales.
            head.farClipPlane = 2000f;
            head.clearFlags = CameraClearFlags.SolidColor;
            head.backgroundColor = PrismPalette.Warm;
            head.allowMSAA = true;

            // Per-eye cameras are off; the centre camera renders both eyes single-pass instanced.
            foreach (var t in new[] { rig.leftEyeAnchor, rig.rightEyeAnchor })
            {
                var c = t != null ? t.GetComponent<Camera>() : null;
                if (c != null) c.enabled = false;
            }

            ConfigureManager(manager);
            BuildPassthroughLayer(go);

            // Hands read the anchors OVRCameraRig actually drives. LeftControllerAnchor and
            // RightControllerAnchor exist and look equivalent, but the rig never writes to them —
            // that mistake is what made planetpool's controllers appear dead.
            hands = go.AddComponent<PrismHands>();
            hands.LeftAnchor = rig.leftHandAnchor;
            hands.RightAnchor = rig.rightHandAnchor;
            hands.Head = rig.centerEyeAnchor;
            hands.TrackingSpace = rig.trackingSpace;

            // Hand tracking sources. Without these, pinch is unreadable when no controller is
            // held: OVRInput.Axis1D on Controller.LHand/RHand returns 0 forever, so an earlier
            // build was completely uninteractable for anyone using their hands.
            hands.LeftHandTracking  = BuildHandTracking(rig.leftHandAnchor,  false, out var leftSkel);
            hands.RightHandTracking = BuildHandTracking(rig.rightHandAnchor, true,  out var rightSkel);
            hands.LeftSkeleton = leftSkel;
            hands.RightSkeleton = rightSkel;

            return rig;
        }

        /// <summary>
        /// OVRHand + OVRSkeleton under a hand anchor.
        ///
        /// Both HandType on OVRHand and _skeletonType on OVRSkeleton are non-public, so they are
        /// assigned through SerializedObject. A plain reflection-free field write is not available,
        /// and getting this wrong is silent: the component runs, reports untracked forever, and
        /// nothing says why.
        /// </summary>
        static OVRHand BuildHandTracking(Transform anchor, bool right, out OVRSkeleton skeleton)
        {
            skeleton = null;
            if (anchor == null) { Debug.LogError("[PRISM] No hand anchor for hand tracking."); return null; }

            var go = new GameObject(right ? "HandTrackingRight" : "HandTrackingLeft");
            go.transform.SetParent(anchor, false);

            var hand = go.AddComponent<OVRHand>();
            var so = new SerializedObject(hand);
            var handType = so.FindProperty("HandType");
            if (handType != null) handType.enumValueIndex = right ? 2 : 1;   // None, HandLeft, HandRight
            else Debug.LogError("[PRISM] OVRHand.HandType not found; hand pinch will never register.");
            so.ApplyModifiedProperties();

            skeleton = go.AddComponent<OVRSkeleton>();
            var sso = new SerializedObject(skeleton);
            var skelType = sso.FindProperty("_skeletonType");
            if (skelType != null)
                // OVRSkeleton.SkeletonType: None=-1 in OVRPlugin terms but the serialized enum index
                // order is None, HandLeft, HandRight, Body, FullBody, XRHandLeft, XRHandRight.
                skelType.enumValueIndex = right ? 2 : 1;
            else Debug.LogError("[PRISM] OVRSkeleton._skeletonType not found; fingertips unavailable.");
            sso.ApplyModifiedProperties();

            Debug.Log($"[PRISM] Hand tracking built for {(right ? "right" : "left")} hand.");
            return hand;
        }

        static void ConfigureManager(OVRManager manager)
        {
            manager.isInsightPassthroughEnabled = false;   // PrismSession owns this at runtime
            manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;

            // Hand tracking support in OVRProjectConfig only writes manifest entries. The scene's
            // OVRManager needs this too, or the runtime commits to one input modality at startup
            // and the other never tracks for the rest of the session.
            var so = new SerializedObject(manager);
            var prop = so.FindProperty("launchSimultaneousHandsControllersOnStartup");
            if (prop != null)
            {
                prop.boolValue = true;
                so.ApplyModifiedProperties();
                Debug.Log("[PRISM] OVRManager: simultaneous hands + controllers enabled.");
            }
            else
            {
                Debug.LogError("[PRISM] launchSimultaneousHandsControllersOnStartup not found on " +
                               "OVRManager — controllers and hands will not both track. Check the SDK version.");
            }
        }

        static void BuildPassthroughLayer(GameObject rigGo)
        {
            var layer = rigGo.AddComponent<OVRPassthroughLayer>();
            var so = new SerializedObject(layer);

            // Underlay: passthrough must composite BEHIND the simulation, not over it.
            var overlay = so.FindProperty("overlayType");
            if (overlay != null) overlay.enumValueIndex = (int)OVROverlay.OverlayType.Underlay;

            var surface = so.FindProperty("projectionSurfaceType");
            if (surface != null) surface.enumValueIndex = 0;   // Reconstructed: the whole room

            so.ApplyModifiedProperties();
        }

        static Companion.PrismCompanion BuildCompanion(Camera head)
        {
            var go = new GameObject("Companion");
            var c = go.AddComponent<Companion.PrismCompanion>();
            go.AddComponent<Companion.PrismVoice>();
            c.Head = head;
            return c;
        }

        /// <summary>
        /// Instantiate every Concept World found by reflection.
        ///
        /// Nothing is listed here by name. Ten world modules were built in parallel by agents that
        /// could not edit this file, so registration is by existence: WorldRegistry finds every
        /// PrismWorldBase subclass and each gets a dormant GameObject. They build their geometry
        /// lazily on first entry, so ten worlds cost ten empty objects at startup rather than ten
        /// worlds' worth of meshes.
        /// </summary>
        static List<Prism.Worlds.PrismWorldBase> BuildDiscoveredWorlds()
        {
            var list = new List<Prism.Worlds.PrismWorldBase>();
            var seenIds = new HashSet<string>();

            foreach (var type in Prism.Worlds.WorldRegistry.WorldTypes)
            {
                try
                {
                    var go = new GameObject("World_" + type.Name);
                    var w = go.AddComponent(type) as Prism.Worlds.PrismWorldBase;
                    if (w == null) { Object.DestroyImmediate(go); continue; }

                    if (!seenIds.Add(w.WorldId))
                    {
                        Debug.LogError($"[PRISM] Two worlds claim the id '{w.WorldId}'. " +
                                       $"'{type.Name}' is being dropped — ids must be unique or the " +
                                       "constellation cannot route to the right one.");
                        Object.DestroyImmediate(go);
                        continue;
                    }

                    list.Add(w);
                    Debug.Log($"[PRISM] World registered: '{w.WorldId}' ({type.Name})");
                }
                catch (System.Exception e)
                {
                    // One broken module must not stop the scene from building.
                    Debug.LogError($"[PRISM] Could not instantiate world '{type.Name}': {e.Message}");
                }
            }
            return list;
        }

        static Prism.Scenery.PrismEnvironment BuildEnvironment()
        {
            var go = new GameObject("PrismEnvironment");
            var env = go.AddComponent<Prism.Scenery.PrismEnvironment>();
            // Moving air. A silent landscape reads as a backdrop; the same landscape with wind
            // reads as somewhere the learner is standing.
            go.AddComponent<AudioSource>();
            go.AddComponent<Prism.Scenery.PrismAmbience>();
            return env;
        }

        static Prism.Interaction.PrismHandCursor BuildCursor(PrismHands hands, bool right)
        {
            var go = new GameObject(right ? "HandCursorRight" : "HandCursorLeft");
            var c = go.AddComponent<Prism.Interaction.PrismHandCursor>();
            c.Hands = hands;
            c.IsRight = right;
            return c;
        }

        static void BuildDiagnostics(PrismHands hands, Camera head, KnowledgeAtrium atrium,
                                     OrbitalWorld world, PrismSession session)
        {
            var go = new GameObject("PrismDiagnostics");
            var d = go.AddComponent<PrismDiagnostics>();
            d.Hands = hands;
            d.Head = head;
            d.Atrium = atrium;
            d.World = world;
            d.Session = session;
        }

        static KnowledgeAtrium BuildAtrium(Camera head)
        {
            var go = new GameObject("KnowledgeAtrium");
            var a = go.AddComponent<KnowledgeAtrium>();
            a.Head = head;      // labels are created in Awake and need this before then
            return a;
        }

        static OrbitalWorld BuildWorld()
        {
            var go = new GameObject("World_OrbitalMechanics");
            var w = go.AddComponent<OrbitalWorld>();
            w.ConceptId = "orbital-mechanics";
            return w;
        }

        static PrismSession BuildSession(PrismHands hands, Camera head, KnowledgeAtrium atrium,
                                         OrbitalWorld world, Companion.PrismCompanion companion)
        {
            var go = new GameObject("PrismSession");
            var s = go.AddComponent<PrismSession>();
            s.Hands = hands;
            s.Head = head;
            s.Atrium = atrium;
            s.World = world;
            s.Companion = companion;
            s.Graph = AssetDatabase.LoadAssetAtPath<ConceptGraph>("Assets/Prism/Content/ConceptGraph.asset");

            if (s.Graph == null)
                Debug.LogError("[PRISM] No ConceptGraph asset. Run PRISM > 2. Seed Concept Graph first.");

            return s;
        }
    }

    /// <summary>
    /// Fails loudly if the generated scene is not fully wired.
    ///
    /// This exists because the characteristic failure of a code-generated scene is not an
    /// exception — it is a null reference that silently makes one feature do nothing, on a device
    /// with nobody watching. Every check here corresponds to something that would otherwise fail
    /// quietly.
    /// </summary>
    public static class PrismVerify
    {
        [MenuItem("PRISM/Verify Scene", priority = 210)]
        public static void Verify()
        {
            int problems = 0;
            void Bad(string m) { Debug.LogError("[PRISM-VERIFY] " + m); problems++; }
            void Ok(string m) => Debug.Log("[PRISM-VERIFY] ok: " + m);

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != PrismSceneBuilder.ScenePath)
            {
                if (Application.isPlaying) { Debug.LogWarning("[PRISM-VERIFY] skipped: play mode."); return; }
                EditorSceneManager.OpenScene(PrismSceneBuilder.ScenePath);
                scene = SceneManager.GetActiveScene();
            }

            var session = Object.FindFirstObjectByType<PrismSession>();
            if (session == null) Bad("no PrismSession in the scene");
            else
            {
                if (session.Hands == null)   Bad("PrismSession.Hands unassigned");
                if (session.Head == null)    Bad("PrismSession.Head unassigned");
                if (session.Atrium == null)  Bad("PrismSession.Atrium unassigned");
                if (session.World == null)   Bad("PrismSession.World unassigned");
                if (session.Graph == null)   Bad("PrismSession.Graph unassigned (seed the concept graph)");
                if (session.Companion == null) Bad("PrismSession.Companion unassigned");
                if (problems == 0) Ok("session fully wired");
            }

            var hands = Object.FindFirstObjectByType<PrismHands>();
            if (hands == null) Bad("no PrismHands");
            else
            {
                if (hands.LeftAnchor == null || hands.RightAnchor == null)
                    Bad("PrismHands anchors unassigned");
                else if (hands.LeftAnchor.name != "LeftHandAnchor" || hands.RightAnchor.name != "RightHandAnchor")
                    Bad($"PrismHands is bound to '{hands.LeftAnchor.name}'/'{hands.RightAnchor.name}'. " +
                        "It must be LeftHandAnchor/RightHandAnchor — the rig never writes to the " +
                        "ControllerAnchor transforms, so controllers would not track.");
                else Ok("hand anchors correct");
            }

            var rig = Object.FindFirstObjectByType<OVRCameraRig>();
            if (rig == null) Bad("no OVRCameraRig");
            else if (rig.centerEyeAnchor == null || rig.centerEyeAnchor.GetComponent<Camera>() == null)
                Bad("no camera on CenterEyeAnchor");
            else Ok("rig and head camera present");

            var manager = Object.FindFirstObjectByType<OVRManager>();
            if (manager == null) Bad("no OVRManager");
            else
            {
                var prop = new SerializedObject(manager).FindProperty("launchSimultaneousHandsControllersOnStartup");
                if (prop == null || !prop.boolValue)
                    Bad("OVRManager simultaneous hands+controllers is off; one modality will not track");
                else Ok("simultaneous hands + controllers on");
            }

            if (Object.FindFirstObjectByType<OVRPassthroughLayer>() == null)
                Bad("no OVRPassthroughLayer; entering a world cannot reveal the room");
            else Ok("passthrough layer present");

            // Hand tracking must have real sources, not just be "supported" in the manifest.
            if (hands != null)
            {
                if (hands.LeftHandTracking == null || hands.RightHandTracking == null)
                    Bad("PrismHands has no OVRHand sources; pinch is unreadable without controllers " +
                        "(OVRInput.Axis1D on Controller.LHand/RHand always returns 0)");
                else if (hands.LeftSkeleton == null || hands.RightSkeleton == null)
                    Bad("PrismHands has no OVRSkeleton; there are no fingertips to interact with");
                else Ok("hand tracking sources present");

                if (hands.TrackingSpace == null)
                    Bad("PrismHands.TrackingSpace unassigned; throw velocity will be in the wrong frame");
            }

            // ---- REACHABILITY -------------------------------------------------------
            // The check that was missing, and whose absence let a completely uninteractable build
            // pass every other test here. Wiring correctness says nothing about whether the
            // learner's arm can actually get to anything: the first build authored concepts between
            // 0.85 m and 2.85 m away and would only select one within 0.13 m of the hand, so no
            // concept in the product could ever be touched. Everything below was green.
            var reachGraph = AssetDatabase.LoadAssetAtPath<ConceptGraph>(
                "Assets/Prism/Content/ConceptGraph.asset");
            var atrium = Object.FindFirstObjectByType<KnowledgeAtrium>();
            if (atrium == null) Bad("no KnowledgeAtrium");
            else if (reachGraph != null)
            {
                float near = float.MaxValue, far = 0f;
                foreach (var c in reachGraph.All)
                {
                    if (c == null) continue;
                    near = Mathf.Min(near, c.distance);
                    far  = Mathf.Max(far, c.distance);
                }

                if (far > atrium.ReachRange)
                    Bad($"the furthest concept is {far:0.00} m away but ReachRange is " +
                        $"{atrium.ReachRange:0.00} m — those concepts can never be selected");
                else Ok($"reach range {atrium.ReachRange:0.0} m covers the furthest concept at {far:0.00} m");

                if (near > atrium.TouchRadius && atrium.ReachConeDegrees <= 0.5f)
                    Bad($"nearest concept is {near:0.00} m away, beyond TouchRadius " +
                        $"{atrium.TouchRadius:0.00} m, and the reach cone is disabled — NOTHING is selectable");
                else if (near > atrium.TouchRadius)
                    Ok($"nearest concept at {near:0.00} m is out of touch range, so selection relies on " +
                       $"the {atrium.ReachConeDegrees:0.0} degree reach cone (as intended)");

                if (atrium.LeftCursor == null || atrium.RightCursor == null)
                    Bad("atrium has no hand cursors; a failed reach would be indistinguishable from a dead app");
                else Ok("hand cursors assigned");
            }

            // Text resources. TMP renders nothing at all without a font asset, and the import is
            // a menu click nobody performs on a build machine.
            if (!PrismTextResources.IsReady)
                Bad("TextMeshPro resources missing — every label will render as nothing. " +
                    "Run PRISM > Ensure Text Resources.");
            else Ok("text resources present");

            // Counting PrismLabels in the scene does NOT work: they are created in Awake, which
            // does not run at author time, so the saved scene legitimately contains none. What has
            // to be true instead is that TMP can find a default font at runtime — that is the
            // actual dependency, and without it every AddComponent<TextMeshPro>() renders nothing.
            var defaultFont = TMPro.TMP_Settings.defaultFontAsset;
            if (defaultFont == null)
                Bad("TMP_Settings has no default font asset; labels created at runtime will be blank");
            else Ok($"TMP default font '{defaultFont.name}' resolves for runtime label creation");

            // The landscape. Without it the atrium is the featureless white void again.
            var scenery = Object.FindFirstObjectByType<Prism.Scenery.PrismEnvironment>();
            if (scenery == null) Bad("no PrismEnvironment; the atrium has no sky, ground or horizon");
            else
            {
                Ok($"environment present (sun {scenery.SunElevation:0.0} deg, " +
                   $"turbidity {scenery.Turbidity:0.0}, backdrop luma {scenery.BackdropLuma:0.00})");

                // The camera must be able to SEE the landscape it was given. Clipping the far
                // terrain leaves a hard circular edge on the ground and deletes the horizon —
                // which would undo the entire reason the landscape exists.
                var headCam = Object.FindFirstObjectByType<OVRCameraRig>()?.centerEyeAnchor
                                    ?.GetComponent<Camera>();
                if (headCam != null && headCam.farClipPlane < scenery.Extent)
                    Bad($"camera far clip {headCam.farClipPlane:0} m is closer than the terrain " +
                        $"extent {scenery.Extent:0} m — the distant ranges will be clipped away");
                else if (headCam != null)
                    Ok($"far clip {headCam.farClipPlane:0} m covers the {scenery.Extent:0} m landscape");
                var session2 = Object.FindFirstObjectByType<PrismSession>();
                if (session2 != null && session2.Scenery == null)
                    Bad("PrismSession.Scenery unassigned; the landscape cannot be hidden for passthrough");
            }

            // DEMONSTRATION COVERAGE.
            //
            // Written when sixteen of seventeen concepts did literally nothing when held, which is
            // what "no content" meant. That is no longer the failure mode, so this is now a
            // coverage REPORT rather than a hard failure, and it distinguishes three cases:
            //
            //   world    the concept opens a Concept World — entering it IS the interaction
            //   demo     a bespoke pocket demonstration
            //   latent   LatentDemo: responds to the hand, deliberately does not pretend to teach
            //
            // Latent is an honest gap, not a bug. It is reported with a count because the number
            // is the work queue, and it fails only if it ever covers the whole constellation —
            // which would mean the product had gone hollow again.
            if (reachGraph != null)
            {
                int withWorld = 0, withDemo = 0, latent = 0;
                foreach (var c in reachGraph.All)
                {
                    if (c == null) continue;
                    if (!string.IsNullOrEmpty(c.worldId)) withWorld++;
                    else if (Prism.Demos.ConceptDemoRegistry.HasBespokeDemo(c.id)) withDemo++;
                    else latent++;
                }

                int total = withWorld + withDemo + latent;
                if (total > 0 && latent == total)
                    Bad("every concept falls back to LatentDemo — nothing in the constellation teaches anything");
                else
                    Ok($"concept coverage: {withWorld} open a world, {withDemo} have a demonstration, " +
                       $"{latent} fall back to the honest LatentDemo");
            }

            // CONSTELLATION COLLISIONS.
            //
            // Ten world modules were told to place their concepts in an assigned azimuth wedge, but
            // "azimuth" has no code-enforced meaning here — Direction is a free Vector3, and the
            // compass convention (0 = +Z, clockwise) and the maths convention (0 = +X, anticlockwise)
            // disagree by a reflection. Rather than police the convention, assert the thing that
            // actually matters: no two concepts may sit on top of each other, because a learner
            // cannot point at one of two coincident targets.
            if (reachGraph != null)
            {
                var placed = new List<ConceptDefinition>();
                foreach (var c in reachGraph.All) if (c != null) placed.Add(c);

                float worstGap = float.MaxValue;
                string worstPair = "";
                int tooClose = 0;
                const float MinGap = 0.22f;   // metres between concept centres

                for (int i = 0; i < placed.Count; i++)
                    for (int j = i + 1; j < placed.Count; j++)
                    {
                        var a = placed[i].direction.normalized * placed[i].distance;
                        var b = placed[j].direction.normalized * placed[j].distance;
                        float gap = Vector3.Distance(a, b);
                        if (gap < worstGap) { worstGap = gap; worstPair = $"{placed[i].id} / {placed[j].id}"; }
                        if (gap < MinGap) tooClose++;
                    }

                if (tooClose > 0)
                    Bad($"{tooClose} concept pair(s) are closer than {MinGap:0.00} m and will overlap " +
                        $"in the constellation; the worst is {worstPair} at {worstGap:0.000} m");
                else
                    Ok($"constellation has no collisions ({placed.Count} concepts, " +
                       $"closest pair {worstPair} at {worstGap:0.00} m)");
            }

            // DUPLICATE DEMONSTRATION CLAIMS.
            //
            // Two [ConceptDemoFor] attributes naming the same concept means one demo is silently
            // dead — the registry keeps the first and drops the rest. With eleven modules written
            // independently that is a live risk, and it cannot be checked by grepping for the
            // attribute text because the attribute's own doc comment contains an example of itself
            // (which produced exactly one false positive). Reflection sees only real attributes.
            {
                var claims = new Dictionary<string, string>();
                int dupes = 0;
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    var an = asm.GetName().Name;
                    if (!an.StartsWith("Assembly-CSharp") && !an.StartsWith("Prism")) continue;
                    System.Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (System.Reflection.ReflectionTypeLoadException e)
                    { types = e.Types.Where(t => t != null).ToArray(); }

                    foreach (var t in types)
                    {
                        if (t == null || t.IsAbstract) continue;
                        foreach (Prism.Demos.ConceptDemoForAttribute a in
                                 t.GetCustomAttributes(typeof(Prism.Demos.ConceptDemoForAttribute), false))
                        {
                            if (string.IsNullOrEmpty(a.ConceptId)) continue;
                            if (claims.TryGetValue(a.ConceptId, out var first))
                            {
                                Bad($"concept '{a.ConceptId}' is claimed by both {first} and " +
                                    $"{t.Name}; one of them will never run");
                                dupes++;
                            }
                            else claims[a.ConceptId] = t.Name;
                        }
                    }
                }
                if (dupes == 0) Ok($"{claims.Count} demonstration claims, none duplicated");
            }

            // VERSIONING. A build that cannot identify itself from inside a headset is a build
            // whose bug reports cannot be attributed — which is exactly what happened repeatedly
            // before this existed.
            if (PlayerSettings.Android.bundleVersionCode < 1)
                Bad("Android bundleVersionCode is not set; store uploads require it to increase");
            else if (PlayerSettings.bundleVersion != Prism.Core.PrismVersion.Semantic)
                Bad($"PlayerSettings.bundleVersion '{PlayerSettings.bundleVersion}' does not match " +
                    $"PrismVersion.Semantic '{Prism.Core.PrismVersion.Semantic}' — the APK would " +
                    "declare a different version than the code believes it is");
            else
                Ok($"version {Prism.Core.PrismVersion.Semantic}+{PlayerSettings.Android.bundleVersionCode} " +
                   $"('{Prism.Core.PrismVersion.Codename}')");

            var world = Object.FindFirstObjectByType<OrbitalWorld>();
            if (world != null)
            {
                // The moon cradle sits 0.16 m from the world anchor, which is itself placed at
                // arm's length. Assert the grab radius is big enough to actually catch one.
                if (world.GrabRadius < 0.05f)
                    Bad($"OrbitalWorld.GrabRadius {world.GrabRadius:0.000} m is too small to catch a moon");
                else Ok($"moon grab radius {world.GrabRadius:0.00} m");
            }

            // Enabled cameras: exactly one should draw.
            int enabledCameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Count(c => c.enabled);
            if (enabledCameras != 1) Bad($"{enabledCameras} enabled cameras; expected exactly 1");
            else Ok("exactly one enabled camera");

            // Every runtime shader must resolve, or its material renders nothing at all.
            foreach (var name in Aesthetic.PrismMaterials.AllShaders)
                if (Shader.Find(name) == null) Bad($"shader missing: {name}");

            var graph = AssetDatabase.LoadAssetAtPath<ConceptGraph>("Assets/Prism/Content/ConceptGraph.asset");
            if (graph == null) Bad("no ConceptGraph asset");
            else
            {
                foreach (var p in graph.Validate()) Bad("graph: " + p);
                bool anyWorld = graph.All.Any(c => c != null && !string.IsNullOrEmpty(c.worldId));
                if (!anyWorld) Bad("no concept in the graph has a worldId, so no world can be entered");
                else Ok($"graph: {graph.concepts.Count} concepts, at least one enterable world");
            }

            if (problems == 0) Debug.Log("[PRISM-VERIFY] ALL CHECKS PASSED");
            else Debug.LogError($"[PRISM-VERIFY] {problems} PROBLEM(S)");
        }

        public static void VerifyFromCommandLine()
        {
            Verify();
            EditorApplication.Exit(0);
        }
    }
}
