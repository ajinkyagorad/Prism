using System.Text;
using Prism.Atrium;
using Prism.Interaction;
using UnityEngine;

namespace Prism.Core
{
    /// <summary>
    /// Once-per-second state dump to the Android log.
    ///
    /// Added after a build reached a headset and the only report possible was "white blobs, nothing
    /// happens" — which was accurate but could have meant a dozen different things: head untracked,
    /// hands untracked, pinch never registering, nothing selectable, materials invisible, or the
    /// session stuck in a transition. Each of those has a different fix and none of them could be
    /// distinguished from inside the headset.
    ///
    /// Read it with:
    ///     adb logcat -s Unity | grep PRISM-DIAG
    ///
    /// Every line answers one question that would otherwise need a guess.
    /// </summary>
    public class PrismDiagnostics : MonoBehaviour
    {
        public PrismHands Hands;
        public Camera Head;
        public KnowledgeAtrium Atrium;
        public Worlds.Orbital.OrbitalWorld World;
        public PrismSession Session;

        [Tooltip("Seconds between reports. Cheap, but there is no reason to spam.")]
        public float Interval = 1f;

        float _next;
        readonly StringBuilder _sb = new StringBuilder();

        void Start()
        {
            // WHICH BUILD IS THIS. First line, before anything else, because every other
            // diagnostic is worthless if we cannot tell which APK produced it.
            Debug.Log($"[PRISM-DIAG] {PrismVersion.Detail}");

            // Report the environment once, up front: half of all XR faults are visible here.
            Debug.Log($"[PRISM-DIAG] boot device='{SystemInfo.deviceModel}' gfx='{SystemInfo.graphicsDeviceType}' " +
                      $"api='{SystemInfo.graphicsDeviceVersion}' shaderLevel={SystemInfo.graphicsShaderLevel} " +
                      $"maxTexture={SystemInfo.maxTextureSize}");

            var loaderActive = UnityEngine.XR.Management.XRGeneralSettings.Instance != null
                            && UnityEngine.XR.Management.XRGeneralSettings.Instance.Manager != null
                            && UnityEngine.XR.Management.XRGeneralSettings.Instance.Manager.activeLoader != null;
            Debug.Log($"[PRISM-DIAG] boot xrLoaderActive={loaderActive} " +
                      $"ovrManager={(OVRManager.instance != null)} " +
                      $"handComponents=L:{(Hands != null && Hands.LeftHandTracking != null)}" +
                      $"/R:{(Hands != null && Hands.RightHandTracking != null)} " +
                      $"controllers={OVRInput.GetConnectedControllers()}");

            foreach (var name in Aesthetic.PrismMaterials.AllShaders)
            {
                var sh = Shader.Find(name);
                Debug.Log($"[PRISM-DIAG] boot shader '{name}' found={sh != null} " +
                          $"supported={(sh != null && sh.isSupported)}");
            }
        }

        void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + Mathf.Max(0.25f, Interval);

            _sb.Clear();
            _sb.Append("[PRISM-DIAG] ");

            if (Head != null)
                _sb.Append($"head={Head.transform.position:F2} ");
            else
                _sb.Append("head=NULL ");

            AppendHand("L", Hands?.Left);
            AppendHand("R", Hands?.Right);

            if (Atrium != null)
            {
                int hovered = 0;
                float nearest = float.MaxValue;
                var point = Hands != null && Hands.Right.IsTracked
                          ? PrismHands.PointOf(Hands.Right)
                          : (Head != null ? Head.transform.position : Vector3.zero);
                foreach (var n in Atrium.Nodes)
                {
                    float d = Vector3.Distance(n.transform.position, point);
                    if (d < nearest) nearest = d;
                    if (n.Summoned) hovered++;
                }
                _sb.Append($"nodes={Atrium.Nodes.Count} held={hovered} nearest={nearest:F2}m ");
            }
            else _sb.Append("atrium=NULL ");

            if (World != null && World.Loop != null)
                _sb.Append($"stage={World.Loop.Stage} worldActive={World.gameObject.activeInHierarchy} ");

            Debug.Log(_sb.ToString());
        }

        void AppendHand(string tag, PrismHands.Hand h)
        {
            if (h == null) { _sb.Append($"{tag}=NULL "); return; }
            _sb.Append($"{tag}[trk={(h.IsTracked ? 1 : 0)} ht={(h.UsingHandTracking ? 1 : 0)} " +
                       $"pinch={h.Pinch:F2} grip={h.Grip:F2} tips={(h.TipsValid ? 1 : 0)} " +
                       $"reach={(h.ReachValid ? 1 : 0)} p={h.Position:F2}] ");
        }
    }
}
