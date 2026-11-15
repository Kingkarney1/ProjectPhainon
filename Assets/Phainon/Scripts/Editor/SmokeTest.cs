using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Phainon.Cockpit;
using Phainon.Displays;
using Phainon.Simulation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Phainon.EditorTools
{
    /// <summary>
    /// A short automated sitting in the real scene, in Play mode: it starts the story mission at the
    /// seat, works one control of each kind through the same calls the mouse makes, checks that the
    /// three displays are alive, and saves a picture of every view. It runs from the Phainon menu, or
    /// by itself when the file Library/PhainonSmokeRequest appears.
    /// Results go to the Console as "[Phainon] SMOKE ..." lines and to Logs/PhainonShots.
    /// </summary>
    // shortcut: one control of each kind, not all of them; widen it when individual controls start to regress.
    [InitializeOnLoad]
    public static class SmokeTest
    {
        private const string RequestPath = "Library/PhainonSmokeRequest";
        private const string RunningKey = "Phainon.SmokeRunning";
        private const string ShotFolder = "Logs/PhainonShots";
        private const int Width = 1152, Height = 648;

        private static IEnumerator routine;
        private static double waitUntil, deadline;
        private static int passed, failed;
        private static readonly List<string> errors = new List<string>();
        private static readonly StringBuilder report = new StringBuilder();

        private static double nextPoll;
        private static bool reloadLocked;
        private static float worstFrame;

        static SmokeTest()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += PollRequest;
        }

        /// <summary>Starts a requested run once the editor is idle and the scene has been rebuilt for the current builders.</summary>
        private static void PollRequest()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;

            // A run whose scripts were reloaded under it is dead: the game is still playing but nothing in
            // it works. End it, so the editor is never left like that.
            if (EditorApplication.isPlaying && routine == null && SessionState.GetBool(RunningKey, false))
            {
                SessionState.SetBool(RunningKey, false);
                Debug.LogWarning("[Phainon] SMOKE ABORTED: scripts were reloaded during the run. Leaving Play mode.");
                EditorApplication.ExitPlaymode();
                return;
            }
            if (!EditorApplication.isPlayingOrWillChangePlaymode) UnlockReload();

            if (!File.Exists(RequestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode || !ProjectBuilder.SceneIsCurrent) return;
            Run();
        }

        [MenuItem("Phainon/Run Smoke Test", priority = 21)]
        public static void Run()
        {
            if (File.Exists(RequestPath)) File.Delete(RequestPath);
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[Phainon] Leave Play mode before running the smoke test.");
                return;
            }
            if (!ProjectBuilder.SceneIsCurrent)
            {
                Debug.LogError("[Phainon] SMOKE ABORTED: the scene is not current, so the last build must have failed.");
                return;
            }
            if (SceneManager.GetActiveScene().path != BuildPaths.MissionScene)
                EditorSceneManager.OpenScene(BuildPaths.MissionScene, OpenSceneMode.Single);
            SessionState.SetBool(RunningKey, true);
            // Scripts changed during the run wait until it is over; a reload in Play mode would wreck it.
            EditorApplication.LockReloadAssemblies();
            reloadLocked = true;
            EditorApplication.EnterPlaymode();
        }

        private static void UnlockReload()
        {
            if (!reloadLocked) return;
            reloadLocked = false;
            EditorApplication.UnlockReloadAssemblies();
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(RunningKey, false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                routine = Sitting();
                waitUntil = 0.0;
                deadline = EditorApplication.timeSinceStartup + 120.0;
                passed = failed = 0;
                errors.Clear();
                report.Length = 0;
                Application.logMessageReceived += OnLog;
                EditorApplication.update += Step;
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(RunningKey, false);
                routine = null;
                Detach();
                UnlockReload();
            }
        }

        private static void Detach()
        {
            Application.logMessageReceived -= OnLog;
            EditorApplication.update -= Step;
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message);
        }

        private static void Step()
        {
            if (!EditorApplication.isPlaying) return;
            worstFrame = Mathf.Max(worstFrame, Time.unscaledDeltaTime);
            if (EditorApplication.timeSinceStartup < waitUntil) return;
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("the run took more than two minutes");
                if (routine.MoveNext())
                {
                    waitUntil = EditorApplication.timeSinceStartup + (routine.Current is float ? (float)routine.Current : 0f);
                    return;
                }
            }
            catch (Exception e)
            {
                Check("the run finished without an exception", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
            }
            Finish();
        }

        private static void Finish()
        {
            Detach();
            StorySave.Suspended = false;
            GameSession.SkipSequences = false;
            string summary = "[Phainon] SMOKE DONE: " + passed + " passed, " + failed + " failed. Pictures in " + ShotFolder;
            report.AppendLine(summary);
            // Which code made this result: a run can start before the editor has compiled the latest change.
            string stamp = "[Phainon] SMOKE RUN build " + ProjectBuilder.BuildVersion + ", editor scripts compiled "
                           + File.GetLastWriteTime(typeof(SmokeTest).Assembly.Location).ToString("HH:mm:ss");
            Directory.CreateDirectory(ShotFolder);
            File.WriteAllText(ShotFolder + "/result.txt", stamp + "\n" + report);
            Debug.Log(summary);
            EditorApplication.ExitPlaymode();
        }

        private static void Check(string what, bool ok, string detail = null)
        {
            if (ok) passed++; else failed++;
            string line = "[Phainon] SMOKE " + (ok ? "PASS " : "FAIL ") + what + (string.IsNullOrEmpty(detail) ? "" : " - " + detail);
            report.AppendLine(line);
            // Plain log either way: a failed check must not be counted again as a logged error.
            Debug.Log(line);
        }

        private static void Note(string text)
        {
            report.AppendLine("[Phainon] SMOKE NOTE " + text);
            Debug.Log("[Phainon] SMOKE NOTE " + text);
        }

        // ---------------------------------------------------------------- the sitting

        private static IEnumerator Sitting()
        {
            // The editor stops the game when its window loses focus; this run must not depend on that.
            Application.runInBackground = true;
            // The story autosaves as objectives fall. This run must never replace the player's save.
            StorySave.Suspended = true;
            GameSession.SkipSequences = true;
            GameManager.Load(GameMode.Story, false, true);
            yield return 2.5f;

            CockpitControlRouter router = Object.FindAnyObjectByType<CockpitControlRouter>();
            Camera camera = Camera.main;
            if (router == null || camera == null)
            {
                Check("the scene has a cockpit and a crew camera", false, "router " + (router != null) + ", camera " + (camera != null));
                yield break;
            }
            GameManager game = router.game;
            Spacecraft craft = router.spacecraft.Craft;
            CockpitCameraController head = camera.GetComponent<CockpitCameraController>();
            Check("the story mission starts at the seat", game.State == GameState.Flying && craft.Story != null, "state " + game.State);

            // Reach: the same test the mouse makes, from where the eye really is. The cabin model has no
            // colliders, so for this check it is given some: a control behind a wall can be clicked but not seen.
            var walls = new List<MeshCollider>();
            foreach (MeshFilter part in router.GetComponentsInChildren<MeshFilter>())
                if (part.sharedMesh != null && part.GetComponent<Collider>() == null && IsModelPart(part.transform))    // the shell, seat and screens; not the thin detail on them
                    walls.Add(part.gameObject.AddComponent<MeshCollider>());
            bool hitBackfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            Physics.SyncTransforms();
            var blocked = new List<string>();
            foreach (Interactable control in router.controls)
                if (!Reaches(camera.transform.position, control, router.settings, walls)) blocked.Add(control.Label.Replace("\n", " "));
            CockpitDisplay[] displays = router.GetComponentsInChildren<CockpitDisplay>(true);
            var hiddenDisplays = new List<string>();
            foreach (CockpitDisplay display in displays)
            {
                Vector3 to = display.transform.position - camera.transform.position;
                RaycastHit wallHit;
                foreach (MeshCollider wall in walls)
                    if (wall.Raycast(new Ray(camera.transform.position, to.normalized), out wallHit, to.magnitude - 0.01f)) { hiddenDisplays.Add(display.unitName); break; }
            }
            Physics.queriesHitBackfaces = hitBackfaces;
            foreach (MeshCollider wall in walls) Object.Destroy(wall);
            Check("the cabin model was found for the sight-line check", walls.Count > 0, walls.Count + " parts");
            Check("no wall of the model stands between the eye and a display", hiddenDisplays.Count == 0, string.Join(", ", hiddenDisplays.ToArray()));
            Check("every control can be reached from the seat (" + (router.controls.Length - blocked.Count) + " of " + router.controls.Length + ")",
                blocked.Count == 0, string.Join(", ", blocked.ToArray()));

            Check("three display units are in the cockpit", displays.Length == 3, displays.Length + " found");
            var before = new string[displays.Length];
            for (int i = 0; i < displays.Length; i++) before[i] = Text(displays[i]);

            // One control of each kind, driven through the calls CockpitInteractionManager makes.
            var handled = new List<ControlId>();
            Action<ControlId, CommandResult> onHandled = (id, result) => handled.Add(id);
            craft.CommandHandled += onHandled;

            Interactable alarm = router.Find(ControlId.MasterAlarm);
            alarm.OnPress();
            alarm.OnRelease();
            Check("button: MASTER ALARM reaches the spacecraft", handled.Contains(ControlId.MasterAlarm));

            Interactable lights = router.Find(ControlId.CabinLights);
            double was = craft.GetControlValue(ControlId.CabinLights);
            lights.OnPress();
            lights.OnRelease();
            Check("switch: CABIN LTS switches the cabin lights", craft.GetControlValue(ControlId.CabinLights) != was);
            lights.OnPress();
            lights.OnRelease();

            Interactable navMode = router.Find(ControlId.NavMode);
            was = craft.GetControlValue(ControlId.NavMode);
            navMode.OnScroll(1f);
            Check("dial: NAV MODE changes the navigation mode", craft.GetControlValue(ControlId.NavMode) != was,
                was + " to " + craft.GetControlValue(ControlId.NavMode));
            navMode.OnScroll(-1f);

            was = craft.GetControlValue(ControlId.Throttle);
            router.throttle.OnScroll(6f);
            Check("throttle: the lever sets the engine throttle command", craft.GetControlValue(ControlId.Throttle) > was + 0.05,
                was.ToString("0.00") + " to " + craft.GetControlValue(ControlId.Throttle).ToString("0.00"));
            craft.CommandHandled -= onHandled;

            // Displays: lit, and showing something different a moment later (the throttle has moved, the clock has run).
            // Performance over five seconds at the Displays view: frame times, what the renderer was asked to draw, and texture memory.
            int frames = Time.frameCount;
            float started = Time.realtimeSinceStartup;
            worstFrame = 0f;
            yield return 5.0f;
            float seconds = Time.realtimeSinceStartup - started;
            int drawn = Mathf.Max(1, Time.frameCount - frames);
            Note("frames: " + (drawn / seconds).ToString("0") + " fps average (" + (seconds / drawn * 1000f).ToString("0.0") + " ms), worst frame "
                 + (worstFrame * 1000f).ToString("0") + " ms, at " + Screen.width + "x" + Screen.height + (UnityEditorInternal.InternalEditorUtility.isApplicationActive ? "" : ", editor not focused (throttled)"));
            Note("renderer: " + UnityStats.drawCalls + " draw calls, " + UnityStats.setPassCalls + " SetPass calls, "
                 + (System.Convert.ToInt64(UnityStats.triangles) / 1000) + "k triangles, render textures "
                 + (System.Convert.ToInt64(UnityStats.renderTextureBytes) >> 20) + " MB");
            long allTextures = 0, cockpitTextures = 0;
            int cockpitCount = 0;
            foreach (Texture texture in Resources.FindObjectsOfTypeAll<Texture>())
            {
                long bytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture);
                allTextures += bytes;
                if (!AssetDatabase.GetAssetPath(texture).StartsWith(BuildPaths.Textures + "/Surface") && !AssetDatabase.GetAssetPath(texture).StartsWith(BuildPaths.Textures + "/Cockpit")) continue;
                cockpitTextures += bytes;
                cockpitCount++;
            }
            Note("texture memory: " + (allTextures >> 20) + " MB in all, of which " + cockpitCount + " cockpit surface textures take " + (cockpitTextures >> 20) + " MB");
            for (int i = 0; i < displays.Length; i++)
            {
                bool lit = displays[i].content.alpha > 0.5f;
                string now = Text(displays[i]);
                Check("display '" + displays[i].unitName + "' is lit and updating", lit && now.Length > 0 && now != before[i],
                    !lit ? "dark" : (now.Length == 0 ? "no text" : (now == before[i] ? "nothing changed in two seconds" : null)));
            }
            router.throttle.SetValue(0f);

            // A picture of every view the number keys give.
            Directory.CreateDirectory(ShotFolder);
            foreach (string old in Directory.GetFiles(ShotFolder, "*.jpg")) File.Delete(old);
            head.Directed = true;
            head.DirectedRoll = 0f;
            for (int i = 0; i < head.presets.Length; i++)
            {
                head.DirectedYaw = head.presets[i].yaw;
                head.DirectedPitch = head.presets[i].pitch;
                yield return 0.3f;
                Capture(camera, (i + 1) + "_" + head.presets[i].name.Replace(' ', '_'));
            }
            // And some the keys do not give: each forward pane, level ahead, and back over the shoulder.
            float[][] extra = { new[] { -30f, -20f }, new[] { 30f, -20f }, new[] { 0f, 0f }, new[] { 150f, 5f },
                                new[] { -45f, 28f }, new[] { 45f, 28f }, new[] { -20f, 52f }, new[] { 55f, 20f } };
            string[] extraNames = { "7_Port_pane", "8_Starboard_pane", "9_Level_ahead", "10_Aft",
                                    "17_Port_shelf", "18_Starboard_shelf", "19_Main_panel_close", "20_Starboard_housing_close" };
            for (int i = 0; i < extra.Length; i++)
            {
                head.DirectedYaw = extra[i][0];
                head.DirectedPitch = extra[i][1];
                yield return 0.3f;
                // The close-ups are taken zoomed in, as the Z key does, to judge the surface finishes.
                // Set at the last moment: the head puts the field of view back every frame.
                if (extraNames[i].EndsWith("close")) camera.fieldOfView = router.settings.zoomFov;
                Capture(camera, extraNames[i]);
            }
            // The sky the windows do not happen to face: the spacecraft is turned for the pictures only.
            // Towards the galactic centre and away from it.
            float[][] turned = { new[] { -10f, 61f }, new[] { -10f, -119f } };
            string[] turnedNames = { "11_Milky_Way_centre", "12_Milky_Way_far_side" };
            head.DirectedYaw = head.presets[0].yaw;
            head.DirectedPitch = head.presets[0].pitch;
            for (int i = 0; i < turned.Length; i++)
            {
                craft.Attitude.SetAttitude(turned[i][0], turned[i][1], 0.0);
                yield return 1.0f;
                Capture(camera, turnedNames[i]);
            }

            // The sky by itself, from outside the cabin, towards each nebula and the galactic centre. The
            // directions are the ones the sky shader gives them among the stars.
            Vector3[] among = { new Vector3(-0.62f, 0.31f, 0.72f), new Vector3(0.48f, -0.55f, -0.68f), new Vector3(0.10f, 0.80f, -0.59f),
                                new Vector3(-0.70f, -0.60f, -0.38f), new Vector3(0.86f, -0.35f, 0f) };
            string[] amongNames = { "21_Sky_pink_nebula", "22_Sky_blue_nebula", "23_Sky_purple_nebula", "24_Sky_red_nebula", "25_Sky_galactic_centre" };
            Matrix4x4 fromStars = Shader.GetGlobalMatrix("_PhainonWorldToInertial").inverse;
            Vector3 seatPosition = camera.transform.position;
            Quaternion seatRotation = camera.transform.rotation;
            for (int i = 0; i < among.Length; i++)
            {
                Vector3 towards = fromStars.MultiplyVector(among[i].normalized);
                camera.transform.SetPositionAndRotation(seatPosition + towards * 80f, Quaternion.LookRotation(towards));
                float skyMean = Capture(camera, amongNames[i]);
                // Not blank (checked by the capture) means the sky is drawn; this is the other half.
                Check("the sky in '" + amongNames[i] + "' stays dark between the stars", skyMean < 0.16f, "mean brightness " + skyMean.ToString("0.000"));
            }
            camera.transform.SetPositionAndRotation(seatPosition, seatRotation);

            // Then with the Sun in the starboard forward window. Where the Sun is comes from the scene itself.
            float tilt = (float)craft.VehicleSpec.windowTiltDegrees;
            Quaternion attitudeNow = Quaternion.Euler(-(float)craft.Attitude.PitchDegrees, (float)craft.Attitude.YawDegrees, -(float)craft.Attitude.RollDegrees)
                             * Quaternion.Euler(tilt, 0f, 0f);
            Vector3 sunLevel = attitudeNow * (Vector3)Shader.GetGlobalVector("_PhainonSunDir");
            Vector3 wanted = Quaternion.Euler(-20f, 29f, 0f) * Vector3.forward;
            float bestPitch = 0f, bestYaw = 0f, bestDot = -2f;
            for (float pitch = -180f; pitch < 180f; pitch += 2f)
                for (float yaw = -180f; yaw < 180f; yaw += 2f)
                {
                    float dot = Vector3.Dot(Quaternion.Inverse(Quaternion.Euler(-pitch, yaw, 0f) * Quaternion.Euler(tilt, 0f, 0f)) * sunLevel, wanted);
                    if (dot > bestDot) { bestDot = dot; bestPitch = pitch; bestYaw = yaw; }
                }
            craft.Attitude.SetAttitude(bestPitch, bestYaw, 0.0);
            yield return 1.0f;
            Vector3 sunSeen = Shader.GetGlobalVector("_PhainonSunDir");
            Check("the Sun is where the spacecraft's attitude puts it", Vector3.Angle(sunSeen, wanted) < 4f,
                Vector3.Angle(sunSeen, wanted).ToString("0.0") + " degrees from the middle of the starboard window");
            Capture(camera, "13_Sun_in_the_window");

            // The cabin lit by that sunlight alone.
            Light sun = RenderSettings.sun;
            Light bounce = null;
            foreach (Light light in router.GetComponentsInChildren<Light>(true)) if (light.name == "Sun Bounce") bounce = light;
            Check("the Sun and its bounce light the cabin", sun != null && sun.intensity > 0.3f && bounce != null && bounce.intensity > 0.2f,
                "sun " + (sun != null ? sun.intensity.ToString("0.00") : "missing") + ", bounce " + (bounce != null ? bounce.intensity.ToString("0.00") : "missing"));
            lights.OnPress();
            lights.OnRelease();
            head.DirectedYaw = 0f;
            head.DirectedPitch = 21f;
            yield return 1.5f;
            Capture(camera, "14_Sunlit_cabin_lights_off");
            head.DirectedYaw = -61f;
            head.DirectedPitch = 39f;
            yield return 0.4f;
            Capture(camera, "15_Sunlit_port_console_lights_off");
            lights.OnPress();
            lights.OnRelease();
            head.Directed = false;

            // The launch mission shares the cockpit: on the pad, power it up and see the displays come on.
            GameManager.Load(GameMode.Standard, false, true);
            yield return 2.5f;
            router = Object.FindAnyObjectByType<CockpitControlRouter>();
            camera = Camera.main;
            craft = router.spacecraft.Craft;
            Check("the launch mission starts on the pad", router.game.State == GameState.Flying && craft.Story == null && !craft.Mission.HasLiftedOff);
            foreach (ControlId id in new[] { ControlId.BatteryA, ControlId.BatteryB, ControlId.MainPower })
            {
                if (craft.GetControlValue(id) > 0.5) continue;
                Interactable control = router.Find(id);
                control.OnPress();
                control.OnRelease();
            }
            yield return 3.5f;
            int alight = 0;
            foreach (CockpitDisplay display in router.GetComponentsInChildren<CockpitDisplay>(true)) if (display.content.alpha > 0.5f) alight++;
            Check("on the pad the batteries and MAIN POWER bring up all three displays", craft.Power.IsBusPowered && alight == 3, alight + " lit");
            head = camera.GetComponent<CockpitCameraController>();
            head.Directed = true;
            head.DirectedYaw = 0f;
            head.DirectedPitch = 5f;
            yield return 0.4f;
            Capture(camera, "16_Launch_pad");
            head.Directed = false;

            Check("nothing was logged as an error during the run", errors.Count == 0,
                errors.Count + " logged; first: " + (errors.Count > 0 ? errors[0] : ""));
        }

        private static bool IsModelPart(Transform part)
        {
            for (Transform t = part; t != null; t = t.parent) if (t.name == "Cabin Model") return true;
            return false;
        }

        private static bool Reaches(Vector3 eye, Interactable control, InteractionSettings settings, List<MeshCollider> walls)
        {
            Collider collider = control.GetComponent<Collider>();
            if (collider == null) collider = control.GetComponentInChildren<Collider>();
            if (collider == null) return false;
            Vector3 direction = collider.bounds.center - eye;
            float distance = direction.magnitude;
            if (distance > settings.reachDistance) return false;
            var ray = new Ray(eye, direction / distance);
            RaycastHit hit;
            foreach (MeshCollider wall in walls)
                if (wall.Raycast(ray, out hit, distance - 0.03f)) return false;
            return Physics.Raycast(ray, out hit, settings.reachDistance, settings.raycastMask, QueryTriggerInteraction.Collide)
                   && hit.collider.GetComponentInParent<Interactable>() == control;
        }

        private static string Text(CockpitDisplay display)
        {
            var all = new StringBuilder();
            foreach (TMP_Text text in display.GetComponentsInChildren<TMP_Text>(false)) all.Append(text.text).Append('|');
            return all.ToString();
        }

        private static float Capture(Camera camera, string name)
        {
            RenderTexture target = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
            RenderTexture previous = camera.targetTexture;
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = previous;

            RenderTexture.active = target;
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(target);

            // A black frame means the capture did not work, which must not pass for a dark cockpit.
            Color32[] pixels = image.GetPixels32();
            long sum = 0;
            for (int i = 0; i < pixels.Length; i += 16) sum += pixels[i].r + pixels[i].g + pixels[i].b;
            float mean = sum / (pixels.Length / 16f * 3f * 255f);
            File.WriteAllBytes(ShotFolder + "/" + name + ".jpg", image.EncodeToJPG(88));
            Object.Destroy(image);
            Check("picture '" + name + "' is not blank", mean > 0.004f, "mean brightness " + mean.ToString("0.000"));
            return mean;
        }
    }
}
