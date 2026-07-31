using System;
using System.IO;
using System.Linq;
using Prism.Core;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Prism.EditorTools
{
    /// <summary>
    /// APK build.
    ///
    ///   PRISM &gt; 4. Build APK
    ///   Unity -batchmode -executeMethod Prism.EditorTools.PrismBuild.BuildFromCommandLine
    ///
    /// The first Android IL2CPP build compiles native code and every shader variant from scratch
    /// and takes tens of minutes. Later builds reuse Library/Bee and are minutes. Never switch the
    /// active build target away from Android and back — that invalidates the cache and you pay the
    /// full cost again.
    /// </summary>
    public static class PrismBuild
    {
        public const string DefaultOutput = "Build/PRISM.apk";

        [MenuItem("PRISM/4. Build APK", priority = 130)]
        public static void BuildApk()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[PRISM] No enabled scenes. Run PRISM > 3. Build Scene first.");
                return;
            }

            // Version BEFORE building, so the stamp the app reads matches the APK's manifest.
            var stamp = ApplyVersion();

            var output = Environment.GetEnvironmentVariable("PRISM_APK_OUT");
            if (string.IsNullOrEmpty(output))
                // Versioned filename so builds cannot silently overwrite each other, which is how
                // "which build were you running?" became unanswerable in the first place.
                output = $"Build/PRISM-{stamp.semantic}+{stamp.code}-{stamp.codename}.apk";

            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            Debug.Log("BUILD_VERSION=" + PrismVersion.Line);
            Debug.Log("BUILD_RESULT=" + summary.result);
            Debug.Log("BUILD_OUTPUT=" + summary.outputPath);
            Debug.Log("BUILD_SIZE=" + summary.totalSize);
            Debug.Log("BUILD_ERRORS=" + summary.totalErrors);
            Debug.Log("BUILD_WARNINGS=" + summary.totalWarnings);
            Debug.Log("BUILD_TIME=" + summary.totalTime);

            if (summary.result != BuildResult.Succeeded)
                foreach (var step in report.steps)
                    foreach (var msg in step.messages)
                        if (msg.type == LogType.Error || msg.type == LogType.Exception)
                            Debug.LogError($"BUILD_STEP_ERROR[{step.name}]: {msg.content}");
        }

        /// <summary>
        /// Set the version on the player and write the build's own record of itself.
        ///
        /// versionCode is incremented and never reused: the Quest Store rejects an upload whose
        /// versionCode is not strictly greater than the last, and more importantly a reused code
        /// makes two different builds indistinguishable on a device.
        ///
        /// The stamp is written as a Resources TEXT ASSET rather than generated C#, because a
        /// generated .cs cannot be compiled and then built in the same batch-mode invocation — the
        /// domain does not reload mid-run, so a code stamp would always describe the PREVIOUS build.
        /// </summary>
        public static PrismVersion.Stamp ApplyVersion()
        {
            int code = PlayerSettings.Android.bundleVersionCode + 1;
            PlayerSettings.Android.bundleVersionCode = code;
            PlayerSettings.bundleVersion = PrismVersion.Semantic;

            // Debug-signed until a real keystore is configured; the channel says so honestly rather
            // than letting a dev build masquerade as a release.
            bool release = !string.IsNullOrEmpty(PlayerSettings.Android.keystoreName)
                        && PlayerSettings.Android.useCustomKeystore;

            var stamp = new PrismVersion.Stamp
            {
                semantic = PrismVersion.Semantic,
                codename = PrismVersion.Codename,
                code = code,
                builtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss'Z'"),
                channel = release ? "release" : "dev",
                worlds = 1 + Prism.Worlds.WorldRegistry.WorldTypes.Count,   // orbital + modules
                concepts = CountConcepts(),
                demos = Prism.Demos.ConceptDemoRegistry.BespokeCount
            };

            const string dir = "Assets/Prism/Resources";
            Directory.CreateDirectory(dir);
            File.WriteAllText($"{dir}/{PrismVersion.StampResource}.json",
                              JsonUtility.ToJson(stamp, true));
            AssetDatabase.ImportAsset($"{dir}/{PrismVersion.StampResource}.json",
                                      ImportAssetOptions.ForceSynchronousImport);

            Debug.Log($"[PRISM] Version {stamp.semantic}+{stamp.code} ({stamp.channel}) — " +
                      $"{stamp.worlds} worlds, {stamp.concepts} concepts, {stamp.demos} demonstrations.");
            return stamp;
        }

        static int CountConcepts()
        {
            var graph = AssetDatabase.LoadAssetAtPath<ConceptGraph>(
                "Assets/Prism/Content/ConceptGraph.asset");
            return graph != null ? graph.concepts.Count : 0;
        }

        public static void BuildFromCommandLine()
        {
            BuildApk();
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).ToArray();
            EditorApplication.Exit(scenes.Length == 0 ? 1 : 0);
        }

        /// <summary>Configure, seed, scene, verify — everything short of the APK, in one invocation.</summary>
        public static void BootstrapFromCommandLine()
        {
            PrismConfigure.Configure();
            // Before the scene: labels are created in Awake and need a font asset to exist.
            PrismTextResources.Ensure();
            PrismConceptSeed.Seed();
            PrismSceneBuilder.BuildScene();
            EditorApplication.Exit(0);
        }
    }
}
