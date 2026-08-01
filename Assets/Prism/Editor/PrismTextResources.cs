using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Prism.EditorTools
{
    /// <summary>
    /// Makes TextMeshPro usable in a project that nobody has opened by hand.
    ///
    /// TMP needs a font asset and a TMP_Settings object before any text will render, and those
    /// normally arrive through a menu item a human clicks ("Window > TextMeshPro > Import TMP
    /// Essential Resources"). This project is generated and built from a terminal, so that click
    /// never happens and the first text in the product would silently render nothing.
    ///
    /// The resources ship as a .unitypackage inside com.unity.ugui, which AssetDatabase can import
    /// non-interactively.
    ///
    ///   PRISM &gt; Ensure Text Resources
    /// </summary>
    public static class PrismTextResources
    {
        const string SettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        /// <summary>The font PRISM uses. ASCII only — see the glyph coverage note in CLAUDE.md.</summary>
        public const string FontAssetPath =
            "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        public static bool IsReady =>
            File.Exists(SettingsPath) && File.Exists(FontAssetPath);

        [MenuItem("PRISM/Ensure Text Resources", priority = 115)]
        public static void Ensure()
        {
            if (IsReady)
            {
                Debug.Log("[PRISM] Text resources already present.");
                return;
            }

            var pkg = FindEssentialsPackage();
            if (pkg == null)
            {
                Debug.LogError("[PRISM] Could not find 'TMP Essential Resources.unitypackage' in the " +
                               "ugui package. Text will not render.");
                return;
            }

            Debug.Log($"[PRISM] Unpacking text resources from {pkg}");

            // AssetDatabase.ImportPackage is ASYNCHRONOUS. In a -batchmode run that exits when the
            // method returns, the import never happens: it was tried, and the font simply was not
            // there afterwards. Unpacking the archive ourselves is deterministic and finishes
            // before this line does.
            //
            // A .unitypackage is a gzipped tar of <guid>/asset, <guid>/asset.meta and
            // <guid>/pathname. Writing the .meta files back out preserves the original GUIDs, which
            // is what keeps TMP's internal references (settings -> font -> material) intact.
            int written = Unpack(pkg);
            AssetDatabase.Refresh();

            Debug.Log($"[PRISM] Text resources unpacked: {written} files. Ready: {IsReady}");
            if (!IsReady)
                Debug.LogError("[PRISM] Text resources still missing after unpack — text will not render.");
        }

        /// <summary>
        /// Extract a .unitypackage into the project. Returns the number of asset files written.
        /// </summary>
        static int Unpack(string packagePath)
        {
            var assets = new System.Collections.Generic.Dictionary<string, byte[]>();
            var metas = new System.Collections.Generic.Dictionary<string, byte[]>();
            var paths = new System.Collections.Generic.Dictionary<string, string>();

            using (var file = File.OpenRead(packagePath))
            using (var gz = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Decompress))
            {
                var header = new byte[512];
                while (true)
                {
                    if (!ReadExactly(gz, header, 512)) break;

                    // Two consecutive zero blocks terminate a tar archive.
                    bool empty = true;
                    for (int i = 0; i < 512 && empty; i++) if (header[i] != 0) empty = false;
                    if (empty) break;

                    string name = System.Text.Encoding.UTF8.GetString(header, 0, 100).TrimEnd('\0', ' ');
                    string sizeOctal = System.Text.Encoding.UTF8.GetString(header, 124, 12).TrimEnd('\0', ' ').Trim();
                    char typeFlag = (char)header[156];

                    long size = 0;
                    foreach (var c in sizeOctal)
                        if (c >= '0' && c <= '7') size = size * 8 + (c - '0');

                    var data = new byte[size];
                    if (size > 0 && !ReadExactly(gz, data, (int)size)) break;

                    // Skip the padding to the next 512-byte boundary.
                    long pad = (512 - (size % 512)) % 512;
                    if (pad > 0) { var skip = new byte[pad]; ReadExactly(gz, skip, (int)pad); }

                    if (typeFlag == '5' || size == 0) continue;    // directory entry

                    int slash = name.IndexOf('/');
                    if (slash <= 0) continue;
                    string guid = name.Substring(0, slash);
                    string kind = name.Substring(slash + 1);

                    if (kind == "asset") assets[guid] = data;
                    else if (kind == "asset.meta") metas[guid] = data;
                    else if (kind == "pathname")
                        paths[guid] = System.Text.Encoding.UTF8.GetString(data)
                                      .Split('\n')[0].Trim().Replace('\\', '/');
                }
            }

            int count = 0;
            foreach (var kv in paths)
            {
                if (!assets.TryGetValue(kv.Key, out var bytes)) continue;   // folder-only entry
                string dest = kv.Value;
                if (string.IsNullOrEmpty(dest) || !dest.StartsWith("Assets/")) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.WriteAllBytes(dest, bytes);
                if (metas.TryGetValue(kv.Key, out var meta)) File.WriteAllBytes(dest + ".meta", meta);
                count++;
            }
            return count;
        }

        static bool ReadExactly(System.IO.Stream s, byte[] buffer, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = s.Read(buffer, read, count - read);
                if (n <= 0) return false;
                read += n;
            }
            return true;
        }

        public static void EnsureFromCommandLine()
        {
            Ensure();
            EditorApplication.Exit(0);
        }

        static string FindEssentialsPackage()
        {
            // Resolved from the package cache rather than hardcoded, because the folder carries a
            // content hash that changes with every package version.
            var roots = new[] { "Library/PackageCache", "Packages" };
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                var hit = Directory.GetDirectories(root)
                    .Where(d => Path.GetFileName(d).StartsWith("com.unity.ugui"))
                    .Select(d => Path.Combine(d, "Package Resources", "TMP Essential Resources.unitypackage"))
                    .FirstOrDefault(File.Exists);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}
