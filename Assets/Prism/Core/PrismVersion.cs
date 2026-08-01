using UnityEngine;

namespace Prism.Core
{
    /// <summary>
    /// What build this is, and how to find that out from inside a headset.
    ///
    /// This exists because of a concrete failure mode this project has already hit repeatedly: a
    /// build is sideloaded, someone reports a symptom, and there is no way to tell WHICH build they
    /// ran. Every APK declared versionCode 1 / versionName 1.0 and overwrote the same file, so
    /// "it still does the white thing" could have meant any of six builds. A version that is not
    /// visible from the device is not a version, it is a number in a settings file.
    ///
    /// Three things are therefore true of every build from now on:
    ///
    ///   1. <see cref="Semantic"/> is the single declared source of truth, in code, in this file.
    ///   2. The build stamps an immutable record (code, UTC time, channel) into a Resources text
    ///      asset, so the running app can report exactly what it is.
    ///   3. PrismDiagnostics logs the full version line at boot, so
    ///      `adb logcat -s Unity | grep PRISM` identifies the build before anything else.
    ///
    /// The stamp is DATA rather than generated code on purpose. A generated .cs file cannot be
    /// compiled and then built in the same batch-mode invocation — the domain does not reload
    /// mid-run — so a code stamp would always be one build stale, which is worse than none.
    /// </summary>
    public static class PrismVersion
    {
        /// <summary>
        /// Semantic version. Bump this by hand; it is a statement about the product, not a counter.
        ///   major - the learner's saved profile is no longer compatible
        ///   minor - worlds or capabilities added
        ///   patch - fixes only
        /// </summary>
        public const string Semantic = "0.5.0";

        /// <summary>Human-readable name for this milestone. Shows up in logs and build filenames.</summary>
        public const string Codename = "eleven-places";

        /// <summary>Where the build writes its stamp. Must live under a Resources folder.</summary>
        public const string StampResource = "prism-build";

        [System.Serializable]
        public class Stamp
        {
            public string semantic = Semantic;
            public string codename = Codename;
            public int code;                 // Android versionCode: strictly increasing, never reused
            public string builtUtc = "";
            public string channel = "dev";   // dev = debug-signed, release = release keystore
            public int worlds;
            public int concepts;
            public int demos;
        }

        static Stamp _stamp;

        /// <summary>The build's own record of itself. Never null — falls back to an unstamped dev build.</summary>
        public static Stamp Current
        {
            get
            {
                if (_stamp != null) return _stamp;

                var text = Resources.Load<TextAsset>(StampResource);
                if (text != null)
                {
                    try { _stamp = JsonUtility.FromJson<Stamp>(text.text); }
                    catch { /* fall through to the default below */ }
                }

                // An unstamped build is a real situation (someone pressed Play in the editor), and
                // it must be identifiable as such rather than silently claiming to be a real build.
                if (_stamp == null) _stamp = new Stamp { code = 0, channel = "unstamped", builtUtc = "" };
                return _stamp;
            }
        }

        /// <summary>One line, safe for a log or a label. e.g. "PRISM 0.4.0+12 eleven-worlds (dev)"</summary>
        public static string Line =>
            $"PRISM {Current.semantic}+{Current.code} {Current.codename} ({Current.channel})";

        /// <summary>The fuller line, including when it was built and what is in it.</summary>
        public static string Detail =>
            $"{Line} built {(string.IsNullOrEmpty(Current.builtUtc) ? "unknown" : Current.builtUtc)} " +
            $"| {Current.worlds} worlds, {Current.concepts} concepts, {Current.demos} demonstrations";
    }
}
