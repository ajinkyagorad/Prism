using System.Collections.Generic;
using System.Linq;
using Prism.Aesthetic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;            // XRGeneralSettingsPerBuildTarget
using UnityEditor.XR.Management.Metadata;   // XRPackageMetadataStore
using UnityEditor.XR.OpenXR.Features;       // OpenXRFeatureSetManager
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace Prism.EditorTools
{
    /// <summary>
    /// Everything about this project that is a setting rather than a script.
    ///
    /// It is all here, in code, because a setting configured by hand in an editor GUI is a setting
    /// that cannot be reproduced on a machine nobody is sitting at — and this project is built
    /// headlessly from a terminal.
    ///
    ///   PRISM &gt; 1. Configure Project
    ///   Unity -batchmode -executeMethod Prism.EditorTools.PrismConfigure.ConfigureFromCommandLine
    /// </summary>
    public static class PrismConfigure
    {
        public const string CompanyName = "PrismLearning";
        public const string ProductName = "PRISM";

        [MenuItem("PRISM/1. Configure Project", priority = 100)]
        public static void Configure()
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;

            // The declared version belongs here, not only in the build step: the project should
            // ALWAYS state the version its code believes it is, whether or not an APK is being
            // produced. The build step separately increments the Android versionCode, which is the
            // monotonic counter the store cares about.
            PlayerSettings.bundleVersion = Prism.Core.PrismVersion.Semantic;
            if (PlayerSettings.Android.bundleVersionCode < 1) PlayerSettings.Android.bundleVersionCode = 1;

            ConfigurePlayer();
            ConfigureQuality();
            EnableOpenXRForAndroid();
            EnableMetaXRFeature();
            EnableInteractionProfiles();
            ConfigureStereoRendering();
            ConfigureMetaProjectConfig();
            SetInputHandlingToBoth();
            RegisterShadersForBuild();

            AssetDatabase.SaveAssets();
            Debug.Log("[PRISM] Configure complete.");
        }

        public static void ConfigureFromCommandLine()
        {
            Configure();
            EditorApplication.Exit(0);
        }

        // -----------------------------------------------------------------

        static void ConfigurePlayer()
        {
            var android = NamedBuildTarget.Android;

            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.colorSpace = ColorSpace.Linear;

            // Vulkan only. The PRISM shaders are written once for it, and leaving GLES in the list
            // means the first device that picks it renders a different-looking product.
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });

            PlayerSettings.SetApplicationIdentifier(android, $"com.{CompanyName}.{ProductName}");
            PlayerSettings.SetMobileMTRendering(android, true);
            PlayerSettings.gpuSkinning = true;
            PlayerSettings.Android.forceInternetPermission = false;

            // The learner profile is a local file. Nothing in this build needs the network.
            PlayerSettings.SetIl2CppCompilerConfiguration(android, Il2CppCompilerConfiguration.Release);
        }

        static void ConfigureQuality()
        {
            // MSAA is the right antialiasing on this hardware — the alternative is a post pass,
            // and PRISM has no post stack because everything luminous is done in-material.
            for (int i = 0; i < QualitySettings.count; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.antiAliasing = 4;
                QualitySettings.vSyncCount = 0;             // the XR runtime paces frames
                QualitySettings.shadows = ShadowQuality.Disable;
                QualitySettings.shadowDistance = 0f;
                QualitySettings.skinWeights = SkinWeights.TwoBones;
                QualitySettings.realtimeReflectionProbes = false;
                QualitySettings.softParticles = false;
            }
            Debug.Log("[PRISM] Quality: MSAA 4x, no shadows, no vsync (XR runtime paces).");
        }

        /// <summary>Mirrors planetpool's working OpenXR setup; see that project for the history.</summary>
        static void EnableOpenXRForAndroid()
        {
            var group = BuildTargetGroup.Android;

            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey,
                                                   out XRGeneralSettingsPerBuildTarget perTarget);
            if (perTarget == null)
            {
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                System.IO.Directory.CreateDirectory("Assets/XR/Settings");
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/Settings/XRGeneralSettingsPerBuildTarget.asset");
                AssetDatabase.SaveAssets();
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, perTarget, true);
            }

            var settings = perTarget.SettingsForBuildTarget(group);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<XRGeneralSettings>();
                settings.name = group + " Settings";
                perTarget.SetSettingsForBuildTarget(group, settings);
                AssetDatabase.AddObjectToAsset(settings, perTarget);
            }

            var manager = settings.Manager;
            if (manager == null)
            {
                manager = ScriptableObject.CreateInstance<XRManagerSettings>();
                manager.name = group + " Providers";
                AssetDatabase.AddObjectToAsset(manager, perTarget);
                settings.Manager = manager;
            }

            bool assigned = XRPackageMetadataStore.AssignLoader(
                manager, "UnityEngine.XR.OpenXR.OpenXRLoader", group);
            settings.InitManagerOnStart = true;

            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(perTarget);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PRISM] OpenXR loader assigned for Android: {assigned}");

            foreach (var fs in OpenXRFeatureSetManager.FeatureSetsForBuildTarget(group))
            {
                if (fs.featureSetId == "com.meta.openxr.featureset.metaxr")
                {
                    fs.isEnabled = true;
                    Debug.Log("[PRISM] Enabled OpenXR feature set: " + fs.featureSetId);
                }
            }
            OpenXRFeatureSetManager.SetFeaturesFromEnabledFeatureSets(group);
        }

        /// <summary>
        /// Enable Meta's own OpenXR feature, and shout with the full feature list if it is missing.
        ///
        /// This is not optional garnish on top of enabling the feature SET. Meta's
        /// OVRGradleGeneration only strips Unity's duplicate openxr_loader.aar when MetaXRFeature
        /// is enabled:
        ///
        ///     if (metaXRFeature != null &amp;&amp; metaXRFeature.enabled)
        ///         ... SetIncludeInBuildDelegate(path =&gt; false)   // for openxr_loader.aar
        ///
        /// With it off, BOTH Unity's openxr_loader.aar and Meta's OVRPlugin.aar ship a
        /// lib/arm64-v8a/libopenxr_loader.so, and the Gradle build dies at
        /// mergeReleaseNativeLibs with "2 files found with path". Enabling the feature set alone
        /// did not reliably turn this on in a batch-mode run, so it is set explicitly.
        ///
        /// Matched on type name rather than by referencing Meta.XR.MetaXRFeature directly, so a
        /// package reshuffle degrades to a loud warning instead of a compile error.
        /// </summary>
        static void EnableMetaXRFeature()
        {
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings == null) { Debug.LogError("[PRISM] No OpenXR settings for Android."); return; }

            bool found = false;
            foreach (var feature in settings.GetFeatures<OpenXRFeature>())
            {
                if (feature == null || feature.GetType().Name != "MetaXRFeature") continue;
                feature.enabled = true;
                found = true;
                Debug.Log("[PRISM] MetaXRFeature enabled (this is what strips Unity's duplicate " +
                          "openxr_loader.aar).");
            }

            if (!found)
                Debug.LogError("[PRISM] MetaXRFeature NOT FOUND. The Android build will fail at " +
                               "mergeReleaseNativeLibs with a duplicate libopenxr_loader.so.");

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            // Print the whole feature list: when XR configuration goes wrong it is almost always
            // "a feature I assumed was on is off", and this is the cheapest way to see that.
            foreach (var f in settings.GetFeatures<OpenXRFeature>())
                if (f != null && f.enabled)
                    Debug.Log($"[PRISM]   openxr feature ON: {f.GetType().Name} ({f.name})");
        }

        /// <summary>
        /// Unity's OpenXR plugin only binds controller poses, sticks and buttons for interaction
        /// profiles that are explicitly enabled, and the BASE Oculus Touch profile is required —
        /// without it OVRInput reports no pose and no buttons at all, and every controller in the
        /// build is silently dead. (Learned the hard way in planetpool; do not remove.)
        /// </summary>
        static void EnableInteractionProfiles()
        {
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings == null) { Debug.LogError("[PRISM] No OpenXR settings for Android."); return; }

            foreach (var feature in settings.GetFeatures<OpenXRFeature>())
            {
                bool wanted = feature is OculusTouchControllerProfile
                           || feature is MetaQuestTouchPlusControllerProfile
                           || feature is MetaQuestTouchProControllerProfile;
                if (!wanted) continue;
                feature.enabled = true;
                Debug.Log("[PRISM] Interaction profile enabled: " + feature.GetType().Name);
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Single-pass instanced. Every PRISM shader carries the stereo macros for it, so anything
        /// else both halves the frame rate and renders the wrong eye.
        /// </summary>
        static void ConfigureStereoRendering()
        {
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings == null) return;
            settings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            EditorUtility.SetDirty(settings);
            Debug.Log("[PRISM] Stereo render mode: SinglePassInstanced");
        }

        static void ConfigureMetaProjectConfig()
        {
            var config = OVRProjectConfig.CachedProjectConfig;
            if (config == null) { Debug.LogWarning("[PRISM] No OVRProjectConfig."); return; }

            config.targetDeviceTypes = new System.Collections.Generic.List<OVRProjectConfig.DeviceType>
            {
                OVRProjectConfig.DeviceType.Quest3,
                OVRProjectConfig.DeviceType.Quest3S
            };

            // Controllers AND hands. This only writes the manifest entries; OVRManager in the
            // scene needs launchSimultaneousHandsControllersOnStartup as well, or the runtime
            // picks one modality at startup and the other never tracks. PrismSceneBuilder does that.
            config.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
            config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;
            config.anchorSupport = OVRProjectConfig.AnchorSupport.Enabled;

            OVRProjectConfig.CommitProjectConfig(config);
            Debug.Log("[PRISM] Meta project config: Quest3/3S, controllers+hands, passthrough supported.");
        }

        /// <summary>
        /// Set the input backend to Both.
        ///
        /// com.unity.inputsystem arrives transitively with the Meta XR SDK, and whichever single
        /// backend is active, half the obvious ways of reading a key throw at runtime — legacy
        /// UnityEngine.Input raises InvalidOperationException every frame under the new backend,
        /// and Keyboard.current is null under the old one. Both removes the whole class of bug.
        /// Unity applies this at the next editor launch, which is why configure is its own step.
        /// </summary>
        static void SetInputHandlingToBoth()
        {
            const string path = "ProjectSettings/ProjectSettings.asset";
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            if (assets == null || assets.Length == 0)
            {
                Debug.LogWarning("[PRISM] Could not open ProjectSettings.asset to set the input backend.");
                return;
            }

            var so = new SerializedObject(assets[0]);
            var prop = so.FindProperty("activeInputHandler");
            if (prop == null)
            {
                Debug.LogWarning("[PRISM] activeInputHandler not found; leaving the input backend alone.");
                return;
            }

            if (prop.intValue == 2) { Debug.Log("[PRISM] Input backend already Both."); return; }
            prop.intValue = 2;                     // 0 = legacy, 1 = new, 2 = both
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log("[PRISM] Input backend set to Both (takes effect next editor launch).");
        }

        /// <summary>
        /// Every PRISM material is created at runtime by Shader.Find, and Unity strips shaders no
        /// scene references. Without this, a player build renders the entire product invisible —
        /// and because these shaders are alpha-blended rather than opaque, it fails to nothing at
        /// all rather than to magenta, which is far harder to recognise.
        /// </summary>
        [MenuItem("PRISM/Register Shaders For Build", priority = 200)]
        public static void RegisterShadersForBuild()
        {
            const string path = "ProjectSettings/GraphicsSettings.asset";
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            if (assets == null || assets.Length == 0)
            {
                Debug.LogError("[PRISM] Could not open GraphicsSettings.asset.");
                return;
            }

            var so = new SerializedObject(assets[0]);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            if (list == null) { Debug.LogError("[PRISM] m_AlwaysIncludedShaders not found."); return; }

            var existing = Enumerable.Range(0, list.arraySize)
                .Select(i => list.GetArrayElementAtIndex(i).objectReferenceValue as Shader)
                .Where(s => s != null)
                .Select(s => s.name)
                .ToHashSet();

            // Core shaders plus everything the world modules declare. Worlds are built in
            // isolation and cannot edit PrismMaterials, so they publish their shader names through
            // PrismWorldBase.Shaders and they are unioned in here. An unregistered runtime shader is
            // stripped from the build and its material renders as NOTHING, which is close to
            // undiagnosable on a device.
            var required = new HashSet<string>(PrismMaterials.AllShaders);
            foreach (var s in Prism.Worlds.WorldRegistry.AllWorldShaders()) required.Add(s);
            int requiredCount = required.Count;
            int coreCount = PrismMaterials.AllShaders.Length;

            int added = 0, missing = 0;
            foreach (var name in required)
            {
                if (existing.Contains(name)) continue;

                var shader = Shader.Find(name);
                if (shader == null)
                {
                    Debug.LogError($"[PRISM] Shader '{name}' does not compile or does not exist — " +
                                   "it cannot be registered, and anything using it will be invisible.");
                    missing++;
                    continue;
                }

                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
                added++;
            }

            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log($"[PRISM] Always-included shaders: {added} added, {missing} missing, " +
                      $"{requiredCount} required ({coreCount} core, " +
                      $"{requiredCount - coreCount} from world modules).");
        }
    }
}
