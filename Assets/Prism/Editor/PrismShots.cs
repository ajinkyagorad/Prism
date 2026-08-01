using System.Collections.Generic;
using System.IO;
using Prism.Atrium;
using Prism.Core;
using Prism.Worlds;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Prism.EditorTools
{
    /// <summary>
    /// Renders the product offscreen and writes PNGs.
    ///
    /// This is the first thing in the project capable of producing an image. Every claim made about
    /// PRISM so far has been "it compiles / the numbers are right / the scene is wired", never "it
    /// looks like this" — and three device tests proved those are different claims. A headless Linux
    /// box with Xvfb and a GPU can in fact draw frames, so there is no good reason to keep guessing.
    ///
    ///   xvfb-run -a Unity -batchmode -projectPath . \
    ///            -executeMethod Prism.EditorTools.PrismShots.CaptureFromCommandLine
    ///
    /// NOTE the absence of -nographics: that flag is what has prevented rendering all along, and it
    /// must not be passed here. Xvfb supplies the display.
    ///
    /// Two traps this works around:
    ///
    /// 1. ENTERING PLAY MODE RELOADS THE DOMAIN, which wipes static state and unsubscribes
    ///    EditorApplication.update — so a capture state machine started before the transition simply
    ///    evaporates. Domain reload is disabled for the run.
    ///
    /// 2. The scene's content does not exist until it RUNS. The atrium builds its constellation in
    ///    Start and each world builds lazily on first Enter, so nothing can be photographed from
    ///    edit mode; the shots have to be taken from inside play mode after a settling period.
    /// </summary>
    public static class PrismShots
    {
        const string OutDir = "Shots";
        const int Width = 1280, Height = 720;

        struct Shot
        {
            public string Name;
            public Vector3 Eye;
            public Vector3 Look;
            public float Fov;
            public string WorldId;      // null = the atrium
            public string Caption;
        }

        static readonly List<Shot> Plan = new List<Shot>();
        static int _index;
        static int _settle;
        static Camera _cam;
        static PrismSession _session;
        static PrismWorldBase _activeWorld;

        public static void CaptureFromCommandLine()
        {
            Directory.CreateDirectory(OutDir);
            BuildPlan();

            // Statics must survive the play-mode transition or the state machine below dies.
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload
                                                | EnterPlayModeOptions.DisableSceneReload;

            EditorSceneManager.OpenScene(PrismSceneBuilder.ScenePath);
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void BuildPlan()
        {
            Plan.Clear();

            // The atrium, from where a learner actually stands. Head height is 1.6 m; the
            // constellation is authored around the origin of the session's placement.
            Plan.Add(new Shot { Name = "01-atrium-horizon", Eye = new Vector3(0f, 1.6f, -0.2f),
                Look = new Vector3(0f, 1.5f, 6f), Fov = 70f,
                Caption = "The Knowledge Atrium at dawn. Sky from real Rayleigh and Mie scattering; " +
                          "the plateau, valley mist and distant ranges are all procedural." });

            Plan.Add(new Shot { Name = "02-constellation", Eye = new Vector3(0.1f, 1.62f, -0.6f),
                Look = new Vector3(0f, 1.55f, 1.4f), Fov = 55f,
                Caption = "The learner's constellation. Hue is the concept's DOMAIN; structure is how " +
                          "well it is understood. Currents between concepts brighten as both ends are learned." });

            Plan.Add(new Shot { Name = "03-constellation-wide", Eye = new Vector3(-1.6f, 2.2f, -2.2f),
                Look = new Vector3(0f, 1.5f, 0.6f), Fov = 62f,
                Caption = "65 concepts across ten domains, with 87 authored relationships drawn as " +
                          "coloured currents. Gold marks an analogy — the same shape in different material." });

            Plan.Add(new Shot { Name = "04-orbital", WorldId = "orbital",
                Eye = new Vector3(0f, 1.35f, -0.15f), Look = new Vector3(0f, 1.10f, 0.6f), Fov = 55f,
                Caption = "Orbital Mechanics. mu is derived so a circular orbit at 20 cm takes 6 s — " +
                          "which puts the boundary between orbiting and escaping at 8.7 cm/s of wrist." });

            Plan.Add(new Shot { Name = "05-living-cell", WorldId = "living-cell",
                Eye = new Vector3(0f, 1.35f, -0.1f), Look = new Vector3(0f, 1.10f, 0.6f), Fov = 60f,
                Caption = "Living Cell — a chemical economy under demand. Real coupled rate equations " +
                          "with mass conserved structurally, not asserted." });

            Plan.Add(new Shot { Name = "06-quantum-garden", WorldId = "quantum-garden",
                Eye = new Vector3(0f, 1.4f, -0.2f), Look = new Vector3(0f, 1.15f, 0.7f), Fov = 60f,
                Caption = "Quantum Garden. Real complex amplitudes summed over paths; fringe spacing is " +
                          "computed, and each detection is an honest sample from |psi|^2." });

            Plan.Add(new Shot { Name = "07-machine-cathedral", WorldId = "machine-cathedral",
                Eye = new Vector3(0f, 1.35f, -0.2f), Look = new Vector3(0f, 1.10f, 0.7f), Fov = 62f,
                Caption = "Machine Cathedral. Lever, gear train and block-and-tackle are all thin skins " +
                          "over one shared rig, so 'it is all the same idea' is true in the code." });

            Plan.Add(new Shot { Name = "08-planet-guardian", WorldId = "planet-guardian",
                Eye = new Vector3(0f, 1.35f, -0.15f), Look = new Vector3(0f, 1.12f, 0.6f), Fov = 58f,
                Caption = "Planet Guardian. Real Stefan-Boltzmann energy balance with genuine " +
                          "ice-albedo bistability — the planet keeps moving after you stop pushing." });

            Plan.Add(new Shot { Name = "09-evolution-engine", WorldId = "evolution-engine",
                Eye = new Vector3(0f, 1.35f, -0.15f), Look = new Vector3(0f, 1.10f, 0.6f), Fov = 60f,
                Caption = "Evolution Engine. The learner can change the environment but never touch an " +
                          "organism — the distribution shifts because some individuals leave more offspring." });

            Plan.Add(new Shot { Name = "10-algorithm-city", WorldId = "algorithm-city",
                Eye = new Vector3(0f, 1.38f, -0.2f), Look = new Vector3(0f, 1.10f, 0.7f), Fov = 62f,
                Caption = "Algorithm City. Two sorts really run, side by side, and the growth curve is " +
                          "measured from actual operation counts rather than drawn from a formula." });
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;

            // Let the scene actually come up: the atrium builds its constellation in Start, the
            // environment publishes its globals, and materials settle over a few frames.
            if (_settle < 90) { _settle++; return; }

            if (_cam == null && !Prepare()) { Finish("could not prepare a capture camera"); return; }

            if (_index >= Plan.Count) { Finish(null); return; }

            var shot = Plan[_index];

            // Switching world costs its own settling time, since worlds build lazily on entry.
            if (!EnsureWorld(shot.WorldId)) return;

            Render(shot);
            _index++;
            _settle = 60;      // settle again before the next framing
        }

        static bool Prepare()
        {
            _session = Object.FindFirstObjectByType<PrismSession>();
            if (_session == null) return false;

            // Find the head camera BEFORE creating ours — FindHeadCamera picks the enabled,
            // last-drawn camera, so a shot camera created first gets picked as its own "head",
            // disables itself, and copies its own defaults.
            var head = Worlds.Orbital.OrbitalWorld.FindHeadCamera();

            var go = new GameObject("~PrismShotCam");
            _cam = go.AddComponent<Camera>();
            if (head != null)
            {
                _cam.clearFlags = head.clearFlags;
                _cam.backgroundColor = head.backgroundColor;
                _cam.nearClipPlane = head.nearClipPlane;
                _cam.farClipPlane = head.farClipPlane;
                _cam.cullingMask = head.cullingMask;
                head.enabled = false;          // one drawing camera at a time
            }
            _cam.allowMSAA = true;

            // Re-centre the world on the capture camera.
            //
            // In editor play mode there is no XR runtime, so the head camera sits at exactly
            // (0,0,0) with identity rotation — which KnowledgeAtrium.TryPlace deliberately reads as
            // "not tracked yet", so the constellation never places itself. The first render showed
            // an empty sky for precisely this reason: the concepts were 1.6 m below the framing.
            // Point every placement consumer at the CAPTURE camera.
            //
            // Atrium and worlds each hold a Head reference assigned by the scene builder, and their
            // placement code only falls back to FindHeadCamera when Head is null. Head is not null —
            // it is the real head camera, sitting disabled at the origin — so without this every
            // world placed itself around (0,0,0) and never appeared in any framing.
            if (_session.Atrium != null) _session.Atrium.Head = _cam;
            if (_session.World != null) _session.World.Head = _cam;
            foreach (var w in _session.Worlds) if (w != null) w.Head = _cam;

            if (_session.Atrium != null) _session.Atrium.PlaceAroundUser();

            DumpScene();
            return true;
        }

        /// <summary>
        /// Write what is actually in the scene to a file.
        ///
        /// Debug.Log does not reliably reach the -logFile under xvfb-run, and guessing at why a
        /// render looks wrong has already cost two round trips. This states the facts: where the
        /// camera is, whether the ground and sky exist and how big they are, and where the
        /// constellation actually put its concepts.
        /// </summary>
        static void DumpScene()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"camera        {_cam.transform.position:F2}  fov {_cam.fieldOfView}");

            var env = Object.FindFirstObjectByType<Prism.Scenery.PrismEnvironment>();
            sb.AppendLine($"environment   {(env != null ? "present" : "MISSING")}");
            if (env != null)
            {
                foreach (var mr in env.GetComponentsInChildren<MeshRenderer>(true))
                    sb.AppendLine($"  {mr.name,-10} active={mr.gameObject.activeInHierarchy} " +
                                  $"enabled={mr.enabled} pos={mr.transform.position:F1} " +
                                  $"scale={mr.transform.lossyScale.x:F1} " +
                                  $"bounds={mr.bounds.size:F0} shader={mr.sharedMaterial?.shader?.name}");
            }

            var atrium = _session.Atrium;
            sb.AppendLine($"atrium        {(atrium != null ? "present" : "MISSING")} " +
                          $"active={atrium?.gameObject.activeInHierarchy}");
            if (atrium != null)
            {
                sb.AppendLine($"  root pos    {atrium.transform.position:F2}");
                sb.AppendLine($"  nodes       {atrium.Nodes.Count}");
                int shown = 0;
                foreach (var n in atrium.Nodes)
                {
                    if (shown++ >= 5) break;
                    var mr = n.GetComponent<MeshRenderer>();
                    sb.AppendLine($"    {n.Concept?.id,-22} world={n.transform.position:F2} " +
                                  $"scale={n.transform.lossyScale.x:F3} " +
                                  $"rendererEnabled={(mr != null && mr.enabled)} " +
                                  $"visible={(mr != null && mr.isVisible)}");
                }
            }

            sb.AppendLine($"cameras enabled: ");
            foreach (var c in Camera.allCameras)
                sb.AppendLine($"  {c.name,-18} depth={c.depth} pos={c.transform.position:F2}");

            File.WriteAllText(Path.Combine(OutDir, "_scene-dump.txt"), sb.ToString());
        }

        /// <summary>Enter the requested world (or return to the atrium), and wait while it builds.</summary>
        static bool EnsureWorld(string worldId)
        {
            string current = _activeWorld != null ? _activeWorld.WorldId
                           : (_orbitalShown ? "orbital" : null);
            if (current == worldId) return true;

            // Leave whatever is showing.
            if (_activeWorld != null) { _activeWorld.Leave(); _activeWorld = null; }
            if (_orbitalShown) _session.World?.LeaveShell();
            _orbitalShown = false;
            if (_session.Atrium != null) _session.Atrium.gameObject.SetActive(worldId == null);

            if (worldId == "orbital")
            {
                if (_session.World != null)
                {
                    _session.World.gameObject.SetActive(true);
                    _session.World.PlaceInFrontOfUser();
                    // Orbital predates PrismWorldBase, so the session — and therefore this capture
                    // path — has to hand it its shell explicitly. Without this the deep-space sphere
                    // exists but the highland is still drawn on top of it.
                    _session.World.EnterShell();
                    _orbitalShown = true;
                }
            }
            else
            {
                // Orbital is only ever switched OFF on the way back to the atrium in the real
                // session, because a learner always returns home between worlds. This harness jumps
                // world to world, so it has to do it here — otherwise the planet, its ring and its
                // deep-space shell stay in the scene and appear in every subsequent shot. They did.
                if (_session.World != null) _session.World.gameObject.SetActive(false);

                if (worldId != null)
                {
                    var w = _session.Worlds.Find(x => x != null && x.WorldId == worldId);
                    if (w == null) { Debug.LogWarning($"[PRISM-SHOT] no world '{worldId}'; skipping."); _index++; return false; }
                    w.Enter();
                    _activeWorld = w;
                }
            }

            _settle = 30;      // give the new world frames to build and settle
            return false;
        }

        static bool _orbitalShown;

        static void Render(Shot shot)
        {
            _cam.transform.position = shot.Eye;
            _cam.transform.rotation = Quaternion.LookRotation((shot.Look - shot.Eye).normalized, Vector3.up);
            _cam.fieldOfView = shot.Fov;

            // Place the CONTENT on the camera AFTER it has moved into the shot pose. Placing first
            // centres everything on the PREVIOUS framing — which left the constellation 1.6 m below
            // the lens, and left every world sitting behind the camera entirely.
            if (shot.WorldId == null)
            {
                if (_session.Atrium != null) _session.Atrium.PlaceAroundUser();
            }
            else
            {
                // A Concept World is TABLE-TOP scale — the orbital planet is 4 cm across — and it
                // places itself relative to the learner's head, 0.6 m ahead and 0.5 m down. Framing
                // it from a standing eye pose puts it at the very bottom edge as a 3-degree speck,
                // which is exactly what the first world renders showed: an empty landscape.
                //
                // So: park the camera at a canonical head pose, let the world place itself off that,
                // compute where its anchor therefore landed, and then move in close and look AT it.
                float reach = 0.60f, drop = 0.50f;
                var head = new Vector3(0f, 1.55f, 0f);
                _cam.transform.position = head;
                _cam.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);

                if (shot.WorldId == "orbital" && _session.World != null)
                {
                    _session.World.PlaceInFrontOfUser();
                    reach = _session.World.Reach; drop = _session.World.EyeToTable;
                }
                else if (_activeWorld != null)
                {
                    _activeWorld.PlaceInFrontOfUser();
                    reach = _activeWorld.Reach; drop = _activeWorld.EyeToTable;
                }

                var anchor = head + Vector3.forward * reach + Vector3.down * drop;
                _cam.transform.position = anchor + new Vector3(0.20f, 0.26f, -0.40f);
                _cam.transform.rotation = Quaternion.LookRotation((anchor - _cam.transform.position).normalized, Vector3.up);
                _cam.fieldOfView = 52f;
            }

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
            { antiAliasing = 4 };
            _cam.targetTexture = rt;
            _cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            var path = Path.Combine(OutDir, shot.Name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());

            _cam.targetTexture = null;
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);

            // The caption travels with the image so the page can be assembled without guessing.
            File.WriteAllText(Path.Combine(OutDir, shot.Name + ".txt"), shot.Caption);
            Debug.Log($"[PRISM-SHOT] wrote {path}");
        }

        static void Finish(string error)
        {
            EditorApplication.update -= Tick;
            if (error != null) Debug.LogError("[PRISM-SHOT] " + error);
            else Debug.Log($"[PRISM-SHOT] DONE {_index} shots in {OutDir}/");
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
