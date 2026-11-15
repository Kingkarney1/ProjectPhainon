using System.Collections.Generic;
using Phainon.Cockpit;
using Phainon.Simulation;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Phainon.EditorTools
{
    /// <summary>
    /// Builds the crew cabin: the modelled shell, seat and screens (Art/Models/Hackathon.obj), the
    /// window glass, the instrument panels with all their controls, and the cabin lighting.
    /// Dimensions are in metres with the crew member's eye at (0, 1.22, 0) looking along +Z.
    ///
    /// Panels are the unit of organisation. Each panel is a transform whose local XY plane is the
    /// panel face, so a new panel or control is positioned in simple 2D panel coordinates.
    /// </summary>
    public class CockpitBuilder
    {
        public static readonly Vector3 EyePosition = new Vector3(0f, 1.22f, 0f);

        // Display mounting points, shared with DisplayBuilder.
        public Transform CentreDisplayMount { get; private set; }
        public Transform LeftDisplayMount { get; private set; }
        public Transform RightDisplayMount { get; private set; }
        public static readonly Vector2 ScreenSize = new Vector2(0.44f, 0.275f);

        public Transform Root { get; private set; }
        public CockpitLighting Lighting { get; private set; }
        /// <summary>Sunlight bounced around the cabin; the environment sets its strength from where the Sun is.</summary>
        public Light SunBounce { get; private set; }
        public ReflectionProbe Probe { get; private set; }
        public ControlFactory Controls { get; private set; }

        private readonly AssetFactory assets;
        private readonly SpacecraftController spacecraft;

        private Material shellMat, hullDark, structure, panel, panelBlack, bezel, glassBlack, fabric, fixture, accent, glass;
        private Material marking, hazard;

        public CockpitBuilder(AssetFactory assets, SpacecraftController spacecraft)
        {
            this.assets = assets;
            this.spacecraft = spacecraft;
        }

        public void Build()
        {
            CreateMaterials();

            Root = SceneKit.Empty("Cockpit", null, Vector3.zero).transform;
            Root.gameObject.layer = BuildPaths.LayerCockpit;
            Controls = new ControlFactory(assets, spacecraft);

            BuildModel();
            BuildWindows();
            BuildConsoleStructure();
            BuildDisplayMounts();
            BuildAnnunciators();
            BuildMainPanel();
            BuildOverheadPanel();
            BuildLeftConsole();
            BuildRightConsole();
            BuildLighting();

            var backlight = Root.gameObject.AddComponent<PanelBacklight>();
            backlight.spacecraft = spacecraft;
            backlight.labels = Controls.Labels.ToArray();
        }

        private void CreateMaterials()
        {
            // The shell is one surface seen from inside, so it is drawn on both sides. The model's texture
            // coordinates are not laid out for a tiled finish, so the modelled parts are plain colours.
            shellMat = assets.Lit("CabinShell", new Color(0.21f, 0.22f, 0.23f), 0.0f, 0.3f);
            shellMat.SetFloat("_Cull", 0f);
            hullDark = assets.Lit("HullLiningDark", new Color(0.17f, 0.18f, 0.19f), 0.0f, 0.2f, assets.Grain, 5f);
            structure = assets.Lit("StructureGrey", new Color(0.30f, 0.31f, 0.33f), 0.75f, 0.5f, assets.Brushed, 4f, false, 0.12f);
            panel = assets.Lit("PanelCharcoal", new Color(0.060f, 0.063f, 0.068f), 0.15f, 0.32f, assets.Grain, 9f);
            panelBlack = assets.Lit("PanelBlack", new Color(0.022f, 0.023f, 0.025f), 0.1f, 0.28f, assets.Grain, 9f);
            bezel = assets.Lit("DisplayBezel", new Color(0.03f, 0.031f, 0.034f), 0.4f, 0.55f);
            glassBlack = assets.Lit("ScreenGlass", new Color(0.004f, 0.005f, 0.007f), 0.0f, 0.92f);
            fabric = assets.Lit("SeatFabric", new Color(0.075f, 0.085f, 0.11f), 0.0f, 0.12f);
            accent = assets.Lit("AccentBlue", new Color(0.06f, 0.20f, 0.42f), 0.2f, 0.45f);
            fixture = assets.Emissive("LightFixture", new Color(0.9f, 0.9f, 0.86f), new Color(1.6f, 1.5f, 1.3f));
            glass = assets.Custom("WindowGlass", "Phainon/WindowGlass");
            marking = assets.Emissive("PanelMarking", new Color(0.8f, 0.82f, 0.84f), new Color(0.16f, 0.17f, 0.18f));
            hazard = assets.Lit("HazardAmber", new Color(0.85f, 0.55f, 0.04f), 0.0f, 0.35f);
        }

        // ---------------------------------------------------------------- modelled cabin

        // The model is authored at about four times life size with Z up. These place it so its floor
        // is at y = 0 and the point its author put the camera at is the crew member's eye.
        private const float ModelScale = 0.25828f;
        private const float ModelFloor = -3.470f;       // model units, height of the floor
        private const float ModelEyeForward = -0.0789f; // model units, how far forward the eye sits

        /// <summary>A point given in the model's own (Blender) axes, as a cockpit position.</summary>
        private static Vector3 FromModel(float right, float forward, float up)
        {
            return new Vector3(right, up - ModelFloor, forward - ModelEyeForward) * ModelScale;
        }

        /// <summary>
        /// The cabin shell, seat and three screen housings come from the artist's model. It carries no
        /// materials of its own, so each part is given one of the project's; and no colliders, so it can
        /// never come between the crew and a control.
        /// </summary>
        private void BuildModel()
        {
            var importer = AssetImporter.GetAtPath(BuildPaths.CockpitModel) as ModelImporter;
            if (importer == null)
            {
                // Nothing stands in for a missing model: the cabin is visibly absent and the log says why.
                Debug.LogError("[Phainon] Cockpit model not found at " + BuildPaths.CockpitModel + ". The cabin shell, seat and screens are missing.");
                return;
            }
            if (importer.globalScale != 1f || importer.useFileScale || importer.importNormals != ModelImporterNormals.Calculate
                || importer.normalSmoothingAngle != 30f || importer.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                importer.globalScale = 1f;
                importer.useFileScale = false;
                // The file has no normals. A hard-surface blockout wants its edges kept crisp.
                importer.importNormals = ModelImporterNormals.Calculate;
                importer.normalSmoothingAngle = 30f;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.SaveAndReimport();
            }

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(BuildPaths.CockpitModel);
            GameObject model = Object.Instantiate(asset);
            model.name = "Cabin Model";
            Transform t = model.transform;
            t.SetParent(Root, false);

            // The importer decides which way the file's forward axis ends up. The centre screen is ahead
            // of the seat in the model, so it says which way that is.
            Bounds? centreScreen = null;
            Bounds all = new Bounds();
            bool any = false;
            foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>())
            {
                string part = renderer.name.ToLowerInvariant();
                bool seat = part.Contains("seat"), screen = part.Contains("screen");
                Material material = seat ? fabric : (screen ? bezel : shellMat);
                var materials = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
                // The shell must stop sunlight whichever way its faces point, so light only enters by the windows.
                renderer.shadowCastingMode = seat || screen ? ShadowCastingMode.On : ShadowCastingMode.TwoSided;
                if (screen && !part.Contains(".001") && !part.Contains("001")) centreScreen = renderer.bounds;
                if (any) all.Encapsulate(renderer.bounds); else { all = renderer.bounds; any = true; }
            }
            bool reversed = centreScreen.HasValue && centreScreen.Value.center.z < 0f;
            t.localRotation = Quaternion.Euler(0f, reversed ? 180f : 0f, 0f);
            t.localScale = Vector3.one * ModelScale;
            t.localPosition = new Vector3(0f, -ModelFloor, -ModelEyeForward) * ModelScale;
            SceneKit.SetLayer(model, BuildPaths.LayerCockpit);
            Debug.Log("[Phainon] Cockpit model placed: " + model.GetComponentsInChildren<MeshRenderer>().Length + " parts, model bounds "
                      + all.min.ToString("0.00") + " to " + all.max.ToString("0.00") + (centreScreen.HasValue ? "" : ", centre screen not identified")
                      + (reversed ? ", turned to face forward" : ""));

            // The model is open behind the seat. A plain bulkhead closes the cabin.
            SceneKit.Box("Rear Bulkhead", Root, new Vector3(0f, 1.01f, -1.34f), new Vector3(3.0f, 2.1f, 0.05f), hullDark, true);
        }

        // ---------------------------------------------------------------- windows

        /// <summary>Glass in the model's window openings: two forward panes either side of the centre post, and one each side.</summary>
        private void BuildWindows()
        {
            Transform windows = SceneKit.Empty("Windows", Root, Vector3.zero).transform;
            for (int side = -1; side <= 1; side += 2)
            {
                // Corners of each opening, read from the model: inner and outer at the sill, then at the head.
                Pane("Forward Pane", windows, side, FromModel(0.568f, 3.356f, 1.474f), FromModel(2.987f, 2.756f, 0.859f),
                    FromModel(2.428f, 2.167f, 3.359f), FromModel(0.825f, 2.415f, 3.356f));
                Pane("Side Pane", windows, side, FromModel(3.319f, 2.576f, 0.459f), FromModel(4.081f, 1.361f, 0.162f),
                    FromModel(3.175f, 1.717f, 3.357f), FromModel(2.804f, 2.031f, 3.357f));
            }
        }

        /// <summary>
        /// One pane through four corners given for the starboard side (sill inner, sill outer, head outer,
        /// head inner); side -1 mirrors it to port. It sits just outside the shell and a little oversize,
        /// so its edges are hidden behind the wall around the opening.
        /// </summary>
        private void Pane(string name, Transform parent, int side, Vector3 sillInner, Vector3 sillOuter, Vector3 headOuter, Vector3 headInner)
        {
            if (side < 0)
            {
                sillInner.x = -sillInner.x; sillOuter.x = -sillOuter.x; headOuter.x = -headOuter.x; headInner.x = -headInner.x;
            }
            Vector3 centre = (sillInner + sillOuter + headOuter + headInner) * 0.25f;
            Vector3 across = ((sillOuter - sillInner) + (headOuter - headInner)).normalized;
            Vector3 up = ((headInner - sillInner) + (headOuter - sillOuter)).normalized;
            Vector3 outward = Vector3.Cross(across, up).normalized;
            // Away from the crew member's eye, whichever way the corners ran.
            if (Vector3.Dot(outward, centre - EyePosition) < 0f) outward = -outward;
            float width = Mathf.Max((sillOuter - sillInner).magnitude, (headOuter - headInner).magnitude) * 1.2f;
            float height = Mathf.Max((headInner - sillInner).magnitude, (headOuter - sillOuter).magnitude) * 1.2f;

            GameObject pane = SceneKit.Quad(name, parent, centre + outward * 0.04f, new Vector2(width, height), glass);
            pane.transform.localRotation = Quaternion.LookRotation(outward, up);
            pane.GetComponent<Renderer>().receiveShadows = false;
        }

        // ---------------------------------------------------------------- console structure

        private void BuildConsoleStructure()
        {
            Transform console = SceneKit.Empty("Console Structure", Root, Vector3.zero).transform;

            // Pedestals under the left and right panels. They sit strictly below the panel faces, so no structure can cover a control.
            for (int side = -1; side <= 1; side += 2)
            {
                SceneKit.Box("Side Pedestal", console, new Vector3(side * 0.70f, 0.26f, 0.30f), new Vector3(0.46f, 0.52f, 0.78f), panelBlack, true, new Vector3(0f, side * 25f, 0f));
                SceneKit.Box("Pedestal Trim", console, new Vector3(side * 0.50f, 0.02f, 0.24f), new Vector3(0.04f, 0.04f, 0.86f), accent, false, new Vector3(0f, side * 25f, 0f), false);
            }
        }

        /// <summary>
        /// Creates a panel: a transform whose local XY plane is the panel face. The optional body is a
        /// slab filling the space behind the face, which closes the gap between a tilted panel and
        /// the structure under it.
        /// </summary>
        private Transform Panel(string name, Vector3 position, Vector3 euler, Vector2 size, bool plate = true, Vector3 body = default(Vector3))
        {
            Transform root = SceneKit.Empty(name, Root, position, euler).transform;
            if (plate)
            {
                // The plate's front face lies exactly on the panel plane (local z = 0).
                SceneKit.Box("Plate", root, new Vector3(0f, 0f, 0.011f), new Vector3(size.x, size.y, 0.022f), panel, true);
                const float edge = 0.008f;
                SceneKit.Box("Edge Top", root, new Vector3(0f, size.y * 0.5f - edge * 0.5f, -0.001f), new Vector3(size.x, edge, 0.004f), structure, false, default(Vector3), false);
                SceneKit.Box("Edge Bottom", root, new Vector3(0f, -size.y * 0.5f + edge * 0.5f, -0.001f), new Vector3(size.x, edge, 0.004f), structure, false, default(Vector3), false);
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sy = -1; sy <= 1; sy += 2)
                        SceneKit.Cylinder("Fastener", root, new Vector3(sx * (size.x * 0.5f - 0.016f), sy * (size.y * 0.5f - 0.02f), -0.001f), 0.005f, 0.003f, structure, Axis.Z);
            }
            if (body.z > 0f)
            {
                // Starts 4 mm behind the face so it can never z-fight with the plate.
                SceneKit.Box("Body", root, new Vector3(0f, 0f, 0.004f + 0.022f + body.z * 0.5f), body, panelBlack, true);
            }
            return root;
        }

        // ---------------------------------------------------------------- displays

        private void BuildDisplayMounts()
        {
            // The live displays sit in the recesses of the model's three screens: centre head-on, the outer
            // two turned in by fifteen degrees. The outer recesses are smaller, so those displays are scaled to fit.
            CentreDisplayMount = DisplayUnit("Display Unit 2 (Centre)", FromModel(0f, 2.940f, 0.022f), 0f, 0.98f, new Vector2(0.438f, 0.336f));
            LeftDisplayMount = DisplayUnit("Display Unit 1 (Left)", FromModel(-1.979f, 2.697f, -0.063f), -15f, 0.87f, new Vector2(0.390f, 0.299f));
            RightDisplayMount = DisplayUnit("Display Unit 3 (Right)", FromModel(1.979f, 2.697f, -0.063f), 15f, 0.87f, new Vector2(0.390f, 0.299f));
        }

        /// <summary>
        /// A mount on a screen recess. The recess face gets black glass, and the display is drawn at the
        /// mount in the middle of it, scaled to fit.
        /// </summary>
        private Transform DisplayUnit(string name, Vector3 recessCentre, float yaw, float scale, Vector2 recessSize)
        {
            Transform recess = SceneKit.Empty(name + " Recess", Root, recessCentre, new Vector3(0f, yaw, 0f)).transform;
            SceneKit.Quad("Glass", recess, new Vector3(0f, 0f, -0.001f), recessSize, glassBlack);
            Transform mount = SceneKit.Empty(name, recess, new Vector3(0f, 0f, -0.002f)).transform;
            mount.localScale = Vector3.one * scale;
            return mount;
        }

        private void BuildAnnunciators()
        {
            Transform strip = Panel("Annunciator Panel", new Vector3(0f, 1.135f, 0.775f), Vector3.zero, new Vector2(0.56f, 0.044f), false);
            SceneKit.Box("Backing", strip, new Vector3(0f, 0f, 0.006f), new Vector3(0.56f, 0.044f, 0.012f), panelBlack, false, default(Vector3), false);

            Vector2 size = new Vector2(0.080f, 0.026f);
            float pitch = 0.090f;
            float x = -pitch * 2.5f;
            Controls.Annunciator(strip, new Vector2(x, 0f), IndicatorId.MasterCaution, "CAUTION", ControlFactory.Amber, size); x += pitch;
            Controls.Annunciator(strip, new Vector2(x, 0f), IndicatorId.LaunchGo, "LAUNCH GO", ControlFactory.Green, size); x += pitch;
            Controls.Annunciator(strip, new Vector2(x, 0f), IndicatorId.EngineRunning, "ENGINE", ControlFactory.Green, size); x += pitch;
            Controls.Annunciator(strip, new Vector2(x, 0f), IndicatorId.StageSepReady, "STAGE SEP", ControlFactory.Amber, size); x += pitch;
            Controls.Annunciator(strip, new Vector2(x, 0f), IndicatorId.OrbitGo, "ORBIT GO", ControlFactory.Green, size); x += pitch;
            Controls.Annunciator(strip, new Vector2(x, 0f), IndicatorId.FuelLow, "FUEL LOW", ControlFactory.Amber, size);
        }

        // ---------------------------------------------------------------- main control panel

        private void BuildMainPanel()
        {
            Vector2 size = new Vector2(1.02f, 0.34f);
            Transform p = Panel("Main Control Panel", new Vector3(0f, 0.555f, 0.56f), new Vector3(55f, 0f, 0f), size, true, new Vector3(1.06f, 0.36f, 0.22f));

            Controls.AddLabel("SYSTEMS", p, new Vector2(-0.215f, 0.148f), 7f, false);
            Controls.AddLabel("PROPELLANT  /  COMMUNICATIONS", p, new Vector2(0f, -0.152f), 6f, false);

            // Upper row: system push buttons, flanked by two dials.
            const float top = 0.062f;
            float pitch = 0.086f;
            float x = -pitch * 2.5f;
            Controls.Button(p, new Vector2(x, top), ControlId.MainPower, "MAIN\nPOWER", ControlFactory.Green); x += pitch;
            Controls.Button(p, new Vector2(x, top), ControlId.NavSystem, "NAV\nSYSTEM", ControlFactory.Green); x += pitch;
            Controls.Button(p, new Vector2(x, top), ControlId.Comms, "COMMS", ControlFactory.Green); x += pitch;
            Controls.Button(p, new Vector2(x, top), ControlId.CabinSystems, "CABIN\nSYS", ControlFactory.Green); x += pitch;
            Controls.Button(p, new Vector2(x, top), ControlId.LaunchSequence, "LAUNCH\nSEQ", ControlFactory.Amber); x += pitch;
            Controls.Button(p, new Vector2(x, top), ControlId.MasterAlarm, "MASTER\nALARM", ControlFactory.Red, false, true);

            Controls.Dial(p, new Vector2(-0.405f, 0.035f), ControlId.DisplayBrightness, "DISPLAY BRT", 0.85f, "{0:0}%", 0f, 100f);
            Controls.Dial(p, new Vector2(0.405f, 0.035f), ControlId.CabinTemperature, "CABIN TEMP", 0.4f, "{0:0.0} C", 17f, 27f);

            // Engraved rules that group the controls.
            SceneKit.Box("Rule", p, new Vector3(0f, 0.006f, -0.0005f), new Vector3(0.62f, 0.0014f, 0.001f), marking, false, default(Vector3), false);
            for (int side = -1; side <= 1; side += 2)
                SceneKit.Box("Rule", p, new Vector3(side * 0.335f, 0.03f, -0.0005f), new Vector3(0.0014f, 0.16f, 0.001f), marking, false, default(Vector3), false);

            // Lower row: propellant and communications switches. Electrical and utility switches are overhead.
            const float bottom = -0.082f;
            pitch = 0.11f;
            Controls.Switch(p, new Vector2(-pitch, bottom), ControlId.FuelPump, "FUEL PUMP");
            Controls.Switch(p, new Vector2(0f, bottom), ControlId.TankPress, "TANK PRESS");
            Controls.Switch(p, new Vector2(pitch, bottom), ControlId.CommAntenna, "S-BAND XMIT");

            // Science payload, either side of them: power and instrument choice on the left, observe and send on the right.
            Controls.AddLabel("SCIENCE", p, new Vector2(-0.235f, -0.152f), 6f, false);
            Controls.AddLabel("DATA", p, new Vector2(0.245f, -0.152f), 6f, false);
            Controls.Switch(p, new Vector2(-0.19f, bottom), ControlId.SciencePower, "SCIENCE PWR");
            Controls.Dial(p, new Vector2(-0.275f, bottom - 0.012f), ControlId.InstrumentSelect, "INSTRUMENT", 0f, "{0}", 0f, 2f, new[] { "CAM", "SPEC", "RAD" });
            Controls.Button(p, new Vector2(0.20f, bottom), ControlId.Observe, "OBSERVE", ControlFactory.Blue);
            Controls.Button(p, new Vector2(0.29f, bottom), ControlId.TransmitData, "TRANSMIT", ControlFactory.Blue);
        }

        // ---------------------------------------------------------------- overhead panel

        /// <summary>
        /// Electrical and utility switches on a panel above the forward windows, angled down at the
        /// seat. It sits above the line of sight to the top of the glass.
        /// </summary>
        private void BuildOverheadPanel()
        {
            Vector2 size = new Vector2(0.58f, 0.20f);
            Transform p = Panel("Overhead Panel", new Vector3(0f, 1.705f, 0.353f), new Vector3(-54f, 0f, 0f), size, true, new Vector3(0.62f, 0.24f, 0.12f));

            Controls.AddLabel("ELECTRICAL", p, new Vector2(-0.135f, 0.083f), 7f, false);
            Controls.AddLabel("UTILITY", p, new Vector2(0.125f, 0.083f), 7f, false);
            SceneKit.Box("Rule", p, new Vector3(-0.005f, 0f, -0.0005f), new Vector3(0.0014f, 0.15f, 0.001f), marking, false, default(Vector3), false);

            const float row = -0.018f;
            Controls.Switch(p, new Vector2(-0.21f, row), ControlId.BatteryA, "BATT A");
            Controls.Switch(p, new Vector2(-0.135f, row), ControlId.BatteryB, "BATT B");
            Controls.Switch(p, new Vector2(-0.06f, row), ControlId.Apu, "APU");
            Controls.Switch(p, new Vector2(0.05f, row), ControlId.ExternalCamera, "EXT CAM", true);
            // Lit while the chase view is selected; pressing it swaps the external camera.
            Controls.Button(p, new Vector2(0.125f, row - 0.004f), ControlId.CameraSelect, "CAM\nSELECT", ControlFactory.Blue);
            Controls.Switch(p, new Vector2(0.20f, row), ControlId.CabinLights, "CABIN LTS", true);

            // Grab handles either side, as on a real overhead panel.
            for (int side = -1; side <= 1; side += 2)
            {
                SceneKit.Cylinder("Handle", p, new Vector3(side * 0.265f, 0f, -0.035f), 0.009f, 0.14f, structure, Axis.Y);
                SceneKit.Cylinder("Handle Post", p, new Vector3(side * 0.265f, 0.065f, -0.017f), 0.007f, 0.036f, structure, Axis.Z);
                SceneKit.Cylinder("Handle Post", p, new Vector3(side * 0.265f, -0.065f, -0.017f), 0.007f, 0.036f, structure, Axis.Z);
            }
        }

        // ---------------------------------------------------------------- side consoles

        private void BuildLeftConsole()
        {
            Vector2 size = new Vector2(0.38f, 0.58f);
            Transform p = Panel("Propulsion Console (Left)", new Vector3(-0.62f, 0.65f, 0.34f), new Vector3(70f, -25f, 0f), size, true, new Vector3(0.42f, 0.62f, 0.22f));
            Controls.AddLabel("PROPULSION", p, new Vector2(0f, 0.268f), 8f, false);

            Controls.Throttle(p, new Vector2(-0.085f, -0.035f), ControlId.Throttle, "THROTTLE");
            Controls.Dial(p, new Vector2(0.095f, 0.150f), ControlId.ThrustLimit, "THRUST LIMIT", 1f, "{0:0}%", 40f, 100f);
            Controls.Button(p, new Vector2(0.095f, 0.010f), ControlId.EngineIgnition, "ENGINE\nIGNITION", ControlFactory.Amber, true);
            Controls.Button(p, new Vector2(0.095f, -0.115f), ControlId.EngineCutoff, "ENGINE\nCUTOFF", ControlFactory.Amber);
            Controls.Button(p, new Vector2(0.095f, -0.220f), ControlId.StageSeparation, "STAGE SEP", ControlFactory.Amber, true);
        }

        private void BuildRightConsole()
        {
            Vector2 size = new Vector2(0.38f, 0.58f);
            Transform p = Panel("Flight Control Console (Right)", new Vector3(0.62f, 0.65f, 0.34f), new Vector3(70f, 25f, 0f), size, true, new Vector3(0.42f, 0.62f, 0.22f));
            Controls.AddLabel("FLIGHT CONTROL", p, new Vector2(0f, 0.268f), 8f, false);

            Controls.Dial(p, new Vector2(-0.085f, 0.135f), ControlId.NavMode, "NAV MODE", 0f, "{0}", 0f, 3f, new[] { "STBY", "ASCENT", "ORBIT", "RETRO" });
            Controls.Switch(p, new Vector2(0.060f, 0.155f), ControlId.Guidance, "GUIDANCE");
            Controls.Switch(p, new Vector2(0.135f, 0.155f), ControlId.Rcs, "RCS");
            Controls.Dial(p, new Vector2(-0.085f, -0.030f), ControlId.PitchTrim, "PITCH TRIM", 0.5f, "{0:+0.0;-0.0} DEG", -8f, 8f);
            Controls.Dial(p, new Vector2(-0.085f, -0.180f), ControlId.RollCommand, "ROLL", 0.5f, "{0:+0;-0} DEG", -180f, 180f);
            Controls.Button(p, new Vector2(0.100f, -0.130f), ControlId.Abort, "ABORT", ControlFactory.Red, true, true, 0.052f);
            HazardBorder(p, new Vector2(0.100f, -0.138f), new Vector2(0.116f, 0.100f));
        }

        /// <summary>A striped amber and black border, drawn flat on a panel around a critical control.</summary>
        private void HazardBorder(Transform panelRoot, Vector2 centre, Vector2 size)
        {
            const float segment = 0.0145f, width = 0.006f;
            for (int edge = 0; edge < 4; edge++)
            {
                bool horizontal = edge < 2;
                float length = horizontal ? size.x : size.y;
                int count = Mathf.RoundToInt(length / segment);
                for (int i = 0; i < count; i++)
                {
                    float along = (i + 0.5f) / count * length - length * 0.5f;
                    float across = (edge % 2 == 0 ? 0.5f : -0.5f) * (horizontal ? size.y : size.x);
                    Vector2 at = centre + (horizontal ? new Vector2(along, across) : new Vector2(across, along));
                    Vector3 scale = horizontal ? new Vector3(length / count, width, 0.001f) : new Vector3(width, length / count, 0.001f);
                    SceneKit.Box("Hazard Stripe", panelRoot, new Vector3(at.x, at.y, -0.0005f), scale, i % 2 == 0 ? hazard : panelBlack, false, default(Vector3), false);
                }
            }
        }

        // ---------------------------------------------------------------- lighting

        private void BuildLighting()
        {
            Transform lights = SceneKit.Empty("Cabin Lighting", Root, Vector3.zero).transform;
            var floods = new List<Light>();
            var fixtures = new List<Renderer>();
            var panels = new List<Light>();

            // Two overhead flood fixtures and a dimmer one behind the seat.
            Vector3[] floodPositions = { new Vector3(-0.5f, 1.883f, 0.05f), new Vector3(0.5f, 1.883f, 0.05f), new Vector3(0f, 1.883f, -0.55f) };
            foreach (Vector3 at in floodPositions)
            {
                GameObject housing = SceneKit.Box("Flood Fixture", lights, at, new Vector3(0.34f, 0.03f, 0.11f), fixture, false, default(Vector3), false);
                fixtures.Add(housing.GetComponent<Renderer>());
                floods.Add(SceneKit.PointLight("Flood Light", lights, at + new Vector3(0f, -0.14f, 0f), new Color(1.0f, 0.96f, 0.88f), 1.1f, 4.2f));
            }

            // Small lights above the instrument panel wash it from the crew's side.
            panels.Add(SceneKit.SpotLight("Panel Light", lights, new Vector3(-0.3f, 1.10f, 0.40f), new Vector3(68f, 8f, 0f), new Color(0.80f, 0.90f, 1.0f), 0.5f, 1.4f, 95f));
            panels.Add(SceneKit.SpotLight("Panel Light", lights, new Vector3(0.3f, 1.10f, 0.40f), new Vector3(68f, -8f, 0f), new Color(0.80f, 0.90f, 1.0f), 0.5f, 1.4f, 95f));
            panels.Add(SceneKit.SpotLight("Console Light", lights, new Vector3(-0.66f, 1.18f, 0.28f), new Vector3(80f, 0f, 0f), new Color(0.80f, 0.90f, 1.0f), 0.5f, 1.1f, 80f));
            panels.Add(SceneKit.SpotLight("Console Light", lights, new Vector3(0.66f, 1.18f, 0.28f), new Vector3(80f, 0f, 0f), new Color(0.80f, 0.90f, 1.0f), 0.5f, 1.1f, 80f));

            Light alarm = SceneKit.PointLight("Alarm Beacon", lights, new Vector3(0f, 1.32f, 0.45f), new Color(1f, 0.12f, 0.08f), 0f, 3.2f);
            Light glow = SceneKit.PointLight("Engine Glow", lights, new Vector3(0f, 1.55f, 1.0f), new Color(1f, 0.62f, 0.30f), 0f, 3.5f);

            SunBounce = SceneKit.PointLight("Sun Bounce", lights, new Vector3(0f, 1.35f, 0.30f), new Color(1f, 0.96f, 0.9f), 0f, 3.0f);

            // What the cabin's metal and glass reflect. Rendered on request by the environment, a face at a time.
            Probe = SceneKit.Empty("Cabin Reflection", lights, new Vector3(0f, 1.15f, 0.1f)).AddComponent<ReflectionProbe>();
            Probe.mode = ReflectionProbeMode.Realtime;
            Probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            Probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            Probe.resolution = 128;
            Probe.size = new Vector3(3.2f, 2.6f, 3.2f);
            Probe.nearClipPlane = 0.05f;
            Probe.farClipPlane = 6f;
            Probe.cullingMask = 1 << BuildPaths.LayerCockpit;
            Probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
            Probe.backgroundColor = Color.black;
            Probe.intensity = 0.8f;

            Lighting = lights.gameObject.AddComponent<CockpitLighting>();
            Lighting.spacecraft = spacecraft;
            Lighting.theme = assets.Theme;
            Lighting.floodLights = floods.ToArray();
            Lighting.floodFixtures = fixtures.ToArray();
            Lighting.panelLights = panels.ToArray();
            Lighting.alarmLight = alarm;
            Lighting.engineGlow = glow;
        }
    }
}
