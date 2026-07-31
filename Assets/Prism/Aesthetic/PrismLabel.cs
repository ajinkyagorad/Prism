using System.Text;
using Prism.Core;
using TMPro;
using UnityEngine;

namespace Prism.Aesthetic
{
    /// <summary>
    /// Spatial text. A name floating in the air beside the thing it names — no panel, no plate,
    /// no rectangle.
    ///
    /// The brief says "minimal text until requested". Reaching toward a concept IS the request, so
    /// labels appear on approach and fade the moment attention moves on. Nothing is labelled
    /// permanently and there is no text anywhere in the resting state of the world.
    ///
    /// Three TextMeshPro traps are handled here, all previously paid for in holoshowcase:
    ///
    ///  1. WORLD-SPACE SIZING. TMP line height is roughly fontSize/10 in local units. The
    ///     convention is fontSize = size * 1000 with localScale 0.01, which makes `size` land in
    ///     METRES. Getting this wrong by a factor of ten produces text that is technically present
    ///     and effectively invisible.
    ///
    ///  2. PIVOT MUST FOLLOW ALIGNMENT. A RectTransform pivots at its centre, so left-aligned text
    ///     placed at x actually starts at x minus half the box width. Every label ends up displaced
    ///     unless the pivot matches the alignment.
    ///
    ///  3. ASCII ONLY. The default LiberationSans SDF has no arrows, checkmarks, media symbols or
    ///     box-drawing glyphs — they render as hollow boxes. Degree, middot and em dash are safe
    ///     (Latin-1); anything fancier is not. <see cref="Sanitise"/> enforces this rather than
    ///     trusting everyone to remember.
    /// </summary>
    [RequireComponent(typeof(TextMeshPro))]
    public class PrismLabel : MonoBehaviour
    {
        [Tooltip("Cap height in METRES, thanks to the sizing convention above.")]
        public float Size = 0.018f;

        [Tooltip("Seconds to fade in and out.")]
        public float Fade = 0.18f;

        public Camera Head;

        TextMeshPro _text;
        float _alpha;
        bool _want;
        Color _colour = PrismPalette.Warm;

        void Awake()
        {
            _text = GetComponent<TextMeshPro>();
            Configure();
        }

        void Configure()
        {
            _text.fontSize = Size * 1000f;                 // trap 1
            transform.localScale = Vector3.one * 0.01f;

            _text.alignment = TextAlignmentOptions.Center;
            _text.rectTransform.pivot = new Vector2(0.5f, 0.5f);   // trap 2: matches Center
            _text.rectTransform.sizeDelta = new Vector2(0.55f / 0.01f, 0.10f / 0.01f);

            _text.enableWordWrapping = true;
            _text.overflowMode = TextOverflowModes.Overflow;
            _text.raycastTarget = false;
            _text.color = new Color(_colour.r, _colour.g, _colour.b, 0f);

            // Text is the one thing in PRISM that must read against sky, land and passthrough
            // alike, so it carries its own contrast rather than relying on the backdrop.
            _text.fontMaterial.EnableKeyword("UNDERLAY_ON");
            _text.fontMaterial.SetFloat("_UnderlayOffsetX", 0f);
            _text.fontMaterial.SetFloat("_UnderlayOffsetY", 0f);
            _text.fontMaterial.SetFloat("_UnderlayDilate", 0.30f);
            _text.fontMaterial.SetFloat("_UnderlaySoftness", 0.35f);
            _text.fontMaterial.SetColor("_UnderlayColor", new Color(0.06f, 0.07f, 0.09f, 0.55f));
        }

        /// <summary>Strip anything the bundled font cannot draw. See trap 3.</summary>
        public static string Sanitise(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (c == '\n' || c == ' ') { sb.Append(c); continue; }
                // Printable ASCII, plus the Latin-1 punctuation the font does carry.
                if (c >= 32 && c <= 126) { sb.Append(c); continue; }
                if (c == '°' || c == '·') { sb.Append(c); continue; }   // degree, middot
                if (c == '—' || c == '–') { sb.Append('-'); continue; } // em/en dash
                if (c == '‘' || c == '’') { sb.Append('\''); continue; }
                if (c == '“' || c == '”') { sb.Append('"'); continue; }
                sb.Append(' ');
            }
            return sb.ToString();
        }

        public void SetText(string body, Color colour)
        {
            _colour = colour;
            var clean = Sanitise(body);
            if (_text.text != clean) _text.text = clean;
        }

        public void Show(bool on) => _want = on;

        /// <summary>Place the label a little above a point, facing the learner.</summary>
        public void PlaceAbove(Vector3 worldPoint, float offset)
        {
            transform.position = worldPoint + Vector3.up * offset;
        }

        void LateUpdate()
        {
            _alpha = Mathf.MoveTowards(_alpha, _want ? 1f : 0f, Time.deltaTime / Mathf.Max(Fade, 0.01f));

            bool visible = _alpha > 0.002f;
            if (_text.enabled != visible) _text.enabled = visible;
            if (!visible) return;

            _text.color = new Color(_colour.r, _colour.g, _colour.b, _alpha);

            var cam = Head != null ? Head : Worlds.Orbital.OrbitalWorld.FindHeadCamera();
            if (cam == null) return;

            // Yaw-and-pitch billboard with no roll. Rolling text with the head is the fastest way
            // to make a learner feel seasick.
            Vector3 to = transform.position - cam.transform.position;
            if (to.sqrMagnitude < 1e-6f) return;
            transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);

            // Hold a constant angular size so a label on a distant concept stays readable without
            // becoming a billboard in the sky when it is near.
            float dist = to.magnitude;
            float scale = 0.01f * Mathf.Clamp(dist / 1.2f, 0.75f, 3.5f);
            transform.localScale = Vector3.one * scale;
        }

        /// <summary>Create a label under a parent, ready to use.</summary>
        public static PrismLabel Create(string name, Transform parent, Camera head, float size = 0.018f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshPro>();
            var label = go.AddComponent<PrismLabel>();
            label.Size = size;
            label.Head = head;
            tmp.enabled = false;
            return label;
        }
    }
}
