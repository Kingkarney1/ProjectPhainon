using System;
using System.Collections.Generic;
using System.IO;
using Phainon.Audio;
using Phainon.Cockpit;
using Phainon.Displays;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Phainon.EditorTools
{
    /// <summary>
    /// One-click construction of the whole playable project: generated assets, the mission scene
    /// with every reference wired, and the build settings. Run it from the Phainon menu, or let the
    /// first-open hook run it automatically when the scene does not exist yet.
    /// </summary>
    public static class ProjectBuilder
    {
        /// <summary>
        /// Raise this whenever the builders change what they generate. An open editor then rebuilds
        /// the scene by itself, so the scene on disk can never lag behind the code that makes it.
        /// </summary>
        public const int BuildVersion = 15;
        private const string StampPath = "Library/PhainonBuildVersion.txt";

        public static bool SceneIsCurrent
        {
            get
            {
                return File.Exists(BuildPaths.MissionScene) && File.Exists(StampPath)
                       && File.ReadAllText(StampPath).Trim() == BuildVersion.ToString();
            }
        }

        [MenuItem("Phainon/Build Project (assets + scene)", priority = 0)]
        public static void BuildAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[Phainon] Leave Play mode before rebuilding the project.");
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("Operation Phainon", "Creating assets", 0.1f);
                var assets = new AssetFactory();
                assets.CreateAll();

                EditorUtility.DisplayProgressBar("Operation Phainon", "Building the mission scene", 0.4f);
                BuildScene(assets);

                EditorUtility.DisplayProgressBar("Operation Phainon", "Saving", 0.9f);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                File.WriteAllText(StampPath, BuildVersion.ToString());
                Debug.Log("[Phainon] Project built. Open " + BuildPaths.MissionScene + " and press Play.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("Phainon/Open Mission Scene", priority = 20)]
        public static void OpenMissionScene()
        {
            if (!File.Exists(BuildPaths.MissionScene)) { BuildAll(); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(BuildPaths.MissionScene, OpenSceneMode.Single);
        }

        [MenuItem("Phainon/Build Playtest Package/Windows (.exe)", priority = 40)]
        public static void BuildWindowsPlayer() { BuildPlayer(BuildTarget.StandaloneWindows64, "Windows", "OperationPhainon.exe"); }

        [MenuItem("Phainon/Build Playtest Package/Linux", priority = 41)]
        public static void BuildLinuxPlayer() { BuildPlayer(BuildTarget.StandaloneLinux64, "Linux", "OperationPhainon.x86_64"); }

        [MenuItem("Phainon/Build Playtest Package/macOS (.app)", priority = 42)]
        public static void BuildMacPlayer()
        {
            // Unity on Linux cannot code-sign. An unsigned Apple Silicon binary is refused outright by
            // macOS, while an unsigned Intel one runs (through Rosetta on Apple Silicon), so build Intel only.
            SetMacArchitecture("x64");
            BuildPlayer(BuildTarget.StandaloneOSX, "macOS", "OperationPhainon.app");
        }

        private static void SetMacArchitecture(string architecture)
        {
            // The setting lives in the Mac build module, which may not be installed, so it is reached by name.
            Type settings = FindType("UnityEditor.OSXStandalone.UserBuildSettings");
            System.Reflection.PropertyInfo property = settings == null ? null : settings.GetProperty("architecture");
            try
            {
                if (property != null) property.SetValue(null, Enum.Parse(property.PropertyType, architecture, true), null);
                else Debug.LogWarning("[Phainon] Could not set the macOS architecture. Check Build Profiles: it must be Intel 64-bit for an unsigned build.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Phainon] Could not set the macOS architecture (" + e.Message + "). Check Build Profiles: it must be Intel 64-bit for an unsigned build.");
            }
        }

        /// <summary>
        /// Builds the player into Builds/&lt;platform&gt;/OperationPhainon, adds the playtester guide and
        /// zips the folder, so the result is one file that can be sent as it is.
        /// </summary>
        private static void BuildPlayer(BuildTarget target, string platform, string executable)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
            {
                string help = "[Phainon] " + platform + " build support is not installed. In Unity Hub open Installs, click the gear on "
                              + Application.unityVersion + ", choose Add modules and tick \"" + (platform == "macOS" ? "Mac" : platform) + " Build Support (Mono)\". Then restart Unity and try again.";
                Debug.LogError(help);
                if (!Application.isBatchMode) EditorUtility.DisplayDialog("Operation Phainon", help.Replace("[Phainon] ", ""), "OK");
                else EditorApplication.Exit(1);
                return;
            }

            if (!SceneIsCurrent) BuildAll();

            string folder = "Builds/" + platform + "/OperationPhainon";
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { BuildPaths.MissionScene },
                locationPathName = folder + "/" + executable,
                target = target,
                options = BuildOptions.None
            };
            UnityEditor.Build.Reporting.BuildSummary summary = BuildPipeline.BuildPlayer(options).summary;
            if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.LogError("[Phainon] " + platform + " build " + summary.result + " with " + summary.totalErrors + " errors. See the Console above.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            // Debug-symbol folders are large and of no use to a playtester.
            foreach (string extra in Directory.GetDirectories(folder))
                if (extra.Contains("DoNotShip") || extra.Contains("DontShip")) Directory.Delete(extra, true);
            const string guide = "PLAYTEST_GUIDE.txt";
            if (File.Exists(guide)) File.Copy(guide, folder + "/README.txt", true);

            // Windows testers get a zip. macOS and Linux get a .tar.gz, because the zip writer available
            // here drops the Unix executable permission and the game would then refuse to start.
            bool windows = target == BuildTarget.StandaloneWindows64;
            string zip = "Builds/OperationPhainon-" + platform + "-" + Application.version + (windows ? ".zip" : ".tar.gz");
            if (File.Exists(zip)) File.Delete(zip);
            if (windows ? TryZip(folder, zip) : TryTar(folder, zip))
            {
                Debug.Log("[Phainon] Playtest package ready: " + Path.GetFullPath(zip));
                if (!Application.isBatchMode) EditorUtility.RevealInFinder(zip);
            }
            else
            {
                Debug.LogWarning("[Phainon] Build succeeded but could not be zipped automatically. Compress the folder " + Path.GetFullPath(folder) + " yourself before sending it.");
                if (!Application.isBatchMode) EditorUtility.RevealInFinder(folder);
            }
        }

        private static bool TryTar(string folder, string archivePath)
        {
            try
            {
                var info = new System.Diagnostics.ProcessStartInfo("tar",
                    "-czf \"" + Path.GetFullPath(archivePath) + "\" -C \"" + Path.GetFullPath(Path.GetDirectoryName(folder)) + "\" \"" + Path.GetFileName(folder) + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(info))
                {
                    process.WaitForExit();
                    return process.ExitCode == 0 && File.Exists(archivePath);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Phainon] tar failed: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Zips a folder. The zip API sits in a different assembly depending on the project's .NET
        /// profile, so it is looked up by name; that keeps this file compiling under either profile.
        /// </summary>
        private static bool TryZip(string folder, string zipPath)
        {
            try
            {
                Type zipFile = Type.GetType("System.IO.Compression.ZipFile, System.IO.Compression.FileSystem")
                               ?? Type.GetType("System.IO.Compression.ZipFile, System.IO.Compression.ZipFile")
                               ?? FindType("System.IO.Compression.ZipFile");
                System.Reflection.MethodInfo create = zipFile == null ? null
                    : zipFile.GetMethod("CreateFromDirectory", new[] { typeof(string), typeof(string) });
                if (create == null) return false;
                create.Invoke(null, new object[] { folder, zipPath });
                return File.Exists(zipPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Phainon] Zip failed: " + (e.InnerException ?? e).Message);
                return false;
            }
        }

        // ---------------------------------------------------------------- scene assembly

        private static void BuildScene(AssetFactory assets)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            MeshFactory.ResetBoxes();

            // Core systems.
            var systems = new GameObject("Game Systems");
            var spacecraft = systems.AddComponent<SpacecraftController>();
            spacecraft.vehicleConfig = assets.Vehicle;
            spacecraft.systemsConfig = assets.Systems;
            spacecraft.missionProfile = assets.Mission;
            spacecraft.commsScript = assets.GroundControl;
            spacecraft.storyCampaign = assets.Story;
            var game = systems.AddComponent<GameManager>();
            game.spacecraft = spacecraft;

            // Cabin and controls.
            var cockpit = new CockpitBuilder(assets, spacecraft);
            cockpit.Build();

            // Crew viewpoint.
            GameObject head = SceneKit.Empty("Crew Head", cockpit.Root, CockpitBuilder.EyePosition);
            head.tag = "MainCamera";
            var camera = head.AddComponent<Camera>();
            camera.fieldOfView = assets.Interaction.defaultFov;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 4000f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.allowHDR = true;
            camera.cullingMask = ~(1 << BuildPaths.LayerExterior);
            head.AddComponent<AudioListener>();

            var cockpitCamera = head.AddComponent<CockpitCameraController>();
            cockpitCamera.viewCamera = camera;
            cockpitCamera.spacecraft = spacecraft;
            cockpitCamera.game = game;
            cockpitCamera.settings = assets.Interaction;
            cockpitCamera.startPitch = 8f;
            cockpitCamera.presets = new[]
            {
                new CockpitCameraController.ViewPreset { name = "Window", yaw = 29f, pitch = -20f },
                new CockpitCameraController.ViewPreset { name = "Displays", yaw = 0f, pitch = 21f },
                new CockpitCameraController.ViewPreset { name = "Main panel", yaw = 0f, pitch = 49f },
                new CockpitCameraController.ViewPreset { name = "Left console", yaw = -78f, pitch = 46f },
                new CockpitCameraController.ViewPreset { name = "Right console", yaw = 78f, pitch = 46f },
                new CockpitCameraController.ViewPreset { name = "Overhead panel", yaw = 0f, pitch = -54f }
            };

            var interaction = head.AddComponent<CockpitInteractionManager>();
            interaction.viewCamera = camera;
            interaction.spacecraft = spacecraft;
            interaction.game = game;
            interaction.settings = assets.Interaction;

            var router = cockpit.Root.gameObject.AddComponent<CockpitControlRouter>();
            router.spacecraft = spacecraft;
            router.game = game;
            router.settings = assets.Interaction;
            router.controls = cockpit.Controls.Controls.ToArray();
            router.throttle = cockpit.Controls.ThrottleLever;

            // Displays: left is flight and navigation, centre is systems, right is mission and external view.
            var displays = new DisplayBuilder(assets, spacecraft);
            displays.BuildFlightNav(cockpit.LeftDisplayMount);
            displays.BuildSystems(cockpit.CentreDisplayMount);
            displays.BuildMission(cockpit.RightDisplayMount);

            // World outside.
            var environment = new EnvironmentBuilder(assets, spacecraft, game);
            environment.Build();
            environment.Director.cabinBounce = cockpit.SunBounce;
            environment.Director.cabinProbe = cockpit.Probe;

            // Audio.
            var audioObject = new GameObject("Audio");
            var audio = audioObject.AddComponent<AudioManager>();
            audio.spacecraft = spacecraft;
            audio.game = game;
            audio.router = router;
            audio.cockpitCamera = cockpitCamera;
            audio.library = assets.Audio;

            // HUD and menus.
            var interfaceBuilder = new UiBuilder(assets);
            interfaceBuilder.Build(spacecraft, game, interaction, router, cockpitCamera, camera, audio);

            ConfigureRendering(camera, environment.ExternalCamera);
            ExportControlPrefabs(cockpit.Controls.Controls);
            ReportControlVisibility(camera.transform.position, cockpit.Controls.Controls, assets.Interaction);

            Directory.CreateDirectory(Path.GetDirectoryName(BuildPaths.MissionScene));
            EditorSceneManager.SaveScene(scene, BuildPaths.MissionScene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BuildPaths.MissionScene, true) };
        }

        private static void ConfigureRendering(Camera mainCamera, Camera externalCamera)
        {
            // The sky is drawn by the dome, so there is no skybox; ambient light is set at runtime.
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.02f, 0.02f, 0.03f);
            RenderSettings.fog = false;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.reflectionIntensity = 0.3f;

            // Post-processing volume. The Volume and camera-data types live in the render pipeline
            // packages, which this assembly does not reference directly so that it has no hard
            // dependency on a specific pipeline version; they are reached by name instead.
            var profile = AssetDatabase.LoadAssetAtPath<ScriptableObject>(BuildPaths.VolumeProfile);
            Type volumeType = FindType("UnityEngine.Rendering.Volume");
            if (profile != null && volumeType != null)
            {
                var volumeObject = new GameObject("Post Processing");
                Component volume = volumeObject.AddComponent(volumeType);
                var serialized = new SerializedObject(volume);
                SetProperty(serialized, "m_IsGlobal", p => p.boolValue = true);
                SetProperty(serialized, "sharedProfile", p => p.objectReferenceValue = profile);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning("[Phainon] Post-processing volume was not created (profile or Volume type missing). The game runs without bloom.");
            }

            Type cameraDataType = FindType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData");
            if (cameraDataType == null)
            {
                Debug.LogWarning("[Phainon] URP camera data type not found. Cameras use pipeline defaults.");
                return;
            }
            ConfigureCameraData(mainCamera, cameraDataType, true);
            ConfigureCameraData(externalCamera, cameraDataType, false);
        }

        private static void ConfigureCameraData(Camera camera, Type cameraDataType, bool postProcessing)
        {
            if (camera == null) return;
            Component data = camera.GetComponent(cameraDataType);
            if (data == null) data = camera.gameObject.AddComponent(cameraDataType);
            var serialized = new SerializedObject(data);
            SetProperty(serialized, "m_RenderPostProcessing", p => p.boolValue = postProcessing);
            // 1 = FXAA: cheap edge smoothing that suits thin panel markings.
            SetProperty(serialized, "m_Antialiasing", p => p.intValue = postProcessing ? 1 : 0);
            SetProperty(serialized, "m_RenderShadows", p => p.boolValue = postProcessing);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetProperty(SerializedObject serialized, string name, Action<SerializedProperty> apply)
        {
            SerializedProperty property = serialized.FindProperty(name);
            if (property != null) apply(property);
            else Debug.LogWarning("[Phainon] Property '" + name + "' not found on " + serialized.targetObject.GetType().Name + ".");
        }

        private static Type FindType(string fullName)
        {
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }

        // ---------------------------------------------------------------- prefabs and checks

        /// <summary>Saves one example of each control type as a prefab, ready to drop onto new panels.</summary>
        private static void ExportControlPrefabs(List<Interactable> controls)
        {
            var saved = new HashSet<string>();
            foreach (Interactable control in controls)
            {
                string kind = control.GetType().Name;
                var button = control as InteractiveButton;
                if (button != null && button.HasGuard) kind = "GuardedButton";
                var dial = control as InteractiveDial;
                if (dial != null && dial.IsDetented) kind = "SelectorDial";
                if (!saved.Add(kind)) continue;

                GameObject copy = UnityEngine.Object.Instantiate(control.gameObject);
                copy.name = kind;
                copy.transform.SetParent(null, false);
                copy.transform.position = Vector3.zero;
                copy.transform.rotation = Quaternion.identity;
                PrefabUtility.SaveAsPrefabAsset(copy, BuildPaths.Prefabs + "/" + kind + ".prefab");
                UnityEngine.Object.DestroyImmediate(copy);
            }
        }

        /// <summary>
        /// Casts a ray from the seat to every control and reports any that are hidden behind another
        /// collider, so a layout mistake shows up in the Console instead of as an unusable control.
        /// </summary>
        private static void ReportControlVisibility(Vector3 eye, List<Interactable> controls, InteractionSettings settings)
        {
            Physics.SyncTransforms();
            int blocked = 0;
            foreach (Interactable control in controls)
            {
                Collider collider = control.GetComponent<Collider>();
                if (collider == null) continue;
                Vector3 target = collider.bounds.center;
                Vector3 direction = target - eye;
                float distance = direction.magnitude;

                if (distance > settings.reachDistance)
                {
                    Debug.LogWarning("[Phainon] Control '" + control.Label + "' is beyond reach (" + distance.ToString("0.00") + " m).", control);
                    blocked++;
                    continue;
                }

                RaycastHit hit;
                if (Physics.Raycast(eye, direction / distance, out hit, distance + 0.05f, settings.raycastMask, QueryTriggerInteraction.Collide)
                    && hit.collider.GetComponentInParent<Interactable>() != control)
                {
                    Debug.LogWarning("[Phainon] Control '" + control.Label + "' is hidden from the seat by '" + hit.collider.name + "'.", control);
                    blocked++;
                }
            }
            Debug.Log(blocked == 0
                ? "[Phainon] Layout check: all " + controls.Count + " controls are reachable from the seat."
                : "[Phainon] Layout check: " + blocked + " of " + controls.Count + " controls need attention (see warnings above).");
        }
    }

    /// <summary>
    /// Builds the project automatically when the scene is missing or was generated by an older
    /// version of the builders, so the project is always playable without a manual step.
    /// </summary>
    [InitializeOnLoad]
    public static class FirstOpenBuild
    {
        static FirstOpenBuild()
        {
            // Command-line runs call the builder explicitly; do not race them.
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += TryBuild;
        }

        private static void TryBuild()
        {
            if (ProjectBuilder.SceneIsCurrent) return;
            // Wait for the editor to be idle and out of Play mode, then build. While Play mode is being left the
            // editor already says it is not playing, but the scene cannot be replaced until the game has gone.
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
            {
                EditorApplication.delayCall += TryBuild;
                return;
            }

            // One attempt per version per session: a failing build stays visible in the Console
            // instead of re-running on every recompile.
            string attemptedKey = "Phainon.BuildAttempted." + ProjectBuilder.BuildVersion;
            if (SessionState.GetBool(attemptedKey, false)) return;
            SessionState.SetBool(attemptedKey, true);
            Debug.Log("[Phainon] Scene is missing or out of date: generating assets and the mission scene.");
            ProjectBuilder.BuildAll();
        }
    }
}
