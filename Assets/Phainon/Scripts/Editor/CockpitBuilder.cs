using System.Collections.Generic;
using Phainon.Cockpit;
using Phainon.Simulation;
using TMPro;
using UnityEngine;

namespace Phainon.EditorTools
{
    /// <summary>
    /// Builds the crew cabin: pressure hull, forward window, seat, the instrument panels with all
    /// their controls, and the cabin lighting. Dimensions are in metres with the crew member's eye
    /// at (0, 1.22, 0) looking along +Z.
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
        public ControlFactory Controls { get; private set; }

        private readonly AssetFactory assets;
        private readonly SpacecraftController spacecraft;

        private Material hull, hullDark, structure, panel, panelBlack, bezel, glassBlack, fabric, fixture, floorMat, accent, glass;
        private Material pad, marking, hazard, extinguisherRed, placard;

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

            BuildHull();
            BuildWindow();
            BuildSeat();
            BuildConsoleStructure();
            BuildDisplayMounts();
            BuildAnnunciators();
            BuildMainPanel();
            BuildOverheadPanel();
            BuildLeftConsole();
            BuildRightConsole();
            BuildLighting();
            BuildDressing();

            var backlight = Root.gameObject.AddComponent<PanelBacklight>();
            backlight.spacecraft = spacecraft;
            backlight.labels = Controls.Labels.ToArray();
        }

        private void CreateMaterials()
        {
            hull = assets.Lit("HullLining", new Color(0.36f, 0.37f, 0.36f), 0.0f, 0.18f, assets.Grain, 5f);
            hullDark = assets.Lit("HullLiningDark", new Color(0.17f, 0.18f, 0.19f), 0.0f, 0.2f, assets.Grain, 5f);
            structure = assets.Lit("StructureGrey", new Color(0.30f, 0.31f, 0.33f), 0.75f, 0.5f, assets.Brushed, 4f, false, 0.12f);
            panel = assets.Lit("PanelCharcoal", new Color(0.060f, 0.063f, 0.068f), 0.15f, 0.32f, assets.Grain, 9f);
            panelBlack = assets.Lit("PanelBlack", new Color(0.022f, 0.023f, 0.025f), 0.1f, 0.28f, assets.Grain, 9f);
            bezel = assets.Lit("DisplayBezel", new Color(0.03f, 0.031f, 0.034f), 0.4f, 0.55f);
            glassBlack = assets.Lit("ScreenGlass", new Color(0.004f, 0.005f, 0.007f), 0.0f, 0.92f);
            fabric = assets.Lit("SeatFabric", new Color(0.075f, 0.085f, 0.11f), 0.0f, 0.12f, assets.Quilt, 3.5f, false, 0.4f);
            floorMat = assets.Lit("FloorPlate", new Color(0.11f, 0.115f, 0.12f), 0.5f, 0.35f, assets.Brushed, 2.5f, false, 0.15f);
            accent = assets.Lit("AccentBlue", new Color(0.06f, 0.20f, 0.42f), 0.2f, 0.45f);
            fixture = assets.Emissive("LightFixture", new Color(0.9f, 0.9f, 0.86f), new Color(1.6f, 1.5f, 1.3f));
            glass = assets.Custom("WindowGlass", "Phainon/WindowGlass");
            pad = assets.Lit("HullPadding", new Color(0.46f, 0.46f, 0.43f), 0.0f, 0.1f, assets.Quilt, 2.6f, false, 0.4f);
            marking = assets.Emissive("PanelMarking", new Color(0.8f, 0.82f, 0.84f), new Color(0.16f, 0.17f, 0.18f));
            hazard = assets.Lit("HazardAmber", new Color(0.85f, 0.55f, 0.04f), 0.0f, 0.35f);
            extinguisherRed = assets.Lit("ExtinguisherRed", new Color(0.55f, 0.04f, 0.03f), 0.2f, 0.6f);
            placard = assets.Lit("PlacardPlate", new Color(0.10f, 0.105f, 0.11f), 0.5f, 0.45f);
        }

        // ---------------------------------------------------------------- pressure hull

        private void BuildHull()
        {
            Transform shell = SceneKit.Empty("Hull", Root, Vector3.zero).transform;

            // Interior volume: 2.5 m wide, 2.1 m high, from the rear bulkhead at z = -1.15 to the forward one at z = 1.5.
            const float halfWidth = 1.25f, height = 2.1f, rear = -1.15f, front = 1.5f, wall = 0.12f;
            float length = front - rear;
            float midZ = (front + rear) * 0.5f;

            SceneKit.Box("Floor", shell, new Vector3(0f, -wall * 0.5f, midZ), new Vector3(halfWidth * 2f + wall * 2f, wall, length + wall * 2f), floorMat, true);
            SceneKit.Box("Ceiling", shell, new Vector3(0f, height + wall * 0.5f, midZ), new Vector3(halfWidth * 2f + wall * 2f, wall, length + wall * 2f), hull, true);
            SceneKit.Box("Port Wall", shell, new Vector3(-halfWidth - wall * 0.5f, height * 0.5f, midZ), new Vector3(wall, height, length + wall * 2f), hull, true);
            SceneKit.Box("Starboard Wall", shell, new Vector3(halfWidth + wall * 0.5f, height * 0.5f, midZ), new Vector3(wall, height, length + wall * 2f), hull, true);
            SceneKit.Box("Rear Bulkhead", shell, new Vector3(0f, height * 0.5f, rear - wall * 0.5f), new Vector3(halfWidth * 2f, height, wall), hullDark, true);

            // Forward bulkhead, built around the window opening.
            float zf = front + wall * 0.5f;
            SceneKit.Box("Forward Lower", shell, new Vector3(0f, WindowBottom * 0.5f, zf), new Vector3(halfWidth * 2f, WindowBottom, wall), hullDark, true);
            SceneKit.Box("Forward Upper", shell, new Vector3(0f, (WindowTop + height) * 0.5f, zf), new Vector3(halfWidth * 2f, height - WindowTop, wall), hull, true);
            float sideWidth = halfWidth - WindowHalfWidth;
            float sideX = WindowHalfWidth + sideWidth * 0.5f;
            float windowHeight = WindowTop - WindowBottom;
            float windowMid = (WindowTop + WindowBottom) * 0.5f;
            SceneKit.Box("Forward Port", shell, new Vector3(-sideX, windowMid, zf), new Vector3(sideWidth, windowHeight, wall), hull, true);
            SceneKit.Box("Forward Starboard", shell, new Vector3(sideX, windowMid, zf), new Vector3(sideWidth, windowHeight, wall), hull, true);

            // Chamfered upper corners give the cabin the tapered cross-section of a capsule.
            const float chamfer = 0.62f;
            float chamferLength = chamfer * Mathf.Sqrt(2f);
            for (int side = -1; side <= 1; side += 2)
            {
                SceneKit.Box("Upper Chamfer", shell, new Vector3(side * (halfWidth - chamfer * 0.5f), height - chamfer * 0.5f, midZ),
                    new Vector3(chamferLength, 0.05f, length), hull, false, new Vector3(0f, 0f, -side * 45f));
            }

            // Frames and stringers.
            for (float z = rear + 0.25f; z < front; z += 0.55f)
            {
                for (int side = -1; side <= 1; side += 2)
                    SceneKit.Box("Frame", shell, new Vector3(side * (halfWidth - 0.02f), (height - chamfer) * 0.5f, z), new Vector3(0.04f, height - chamfer, 0.05f), structure, false, default(Vector3), false);
                SceneKit.Box("Roof Frame", shell, new Vector3(0f, height - 0.02f, z), new Vector3((halfWidth - chamfer) * 2f, 0.04f, 0.05f), structure, false, default(Vector3), false);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                SceneKit.Box("Stringer", shell, new Vector3(side * (halfWidth - 0.025f), 1.05f, midZ), new Vector3(0.035f, 0.05f, length), structure, false, default(Vector3), false);
                SceneKit.Cylinder("Handrail", shell, new Vector3(side * (halfWidth - chamfer * 0.5f - 0.05f), height - chamfer * 0.5f - 0.06f, 0.15f), 0.014f, 1.5f, structure, Axis.Z);
            }

            // Stowage lockers on the side walls behind the seat.
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 3; i++)
                {
                    Vector3 at = new Vector3(side * (halfWidth - 0.09f), 0.38f + i * 0.34f, -0.72f);
                    SceneKit.Box("Locker", shell, at, new Vector3(0.18f, 0.31f, 0.62f), hullDark, false, default(Vector3), false);
                    SceneKit.Box("Locker Latch", shell, at + new Vector3(-side * 0.095f, 0f, 0.2f), new Vector3(0.012f, 0.035f, 0.07f), structure, false, default(Vector3), false);
                    TextMeshPro tag = SceneKit.Label("STOWAGE " + (side < 0 ? "P" : "S") + (i + 1), shell, at + new Vector3(-side * 0.0915f, 0.1f, -0.05f), 9f,
                        new Color(0.55f, 0.57f, 0.6f), 30f);
                    tag.transform.localRotation = Quaternion.Euler(0f, side * 90f, 0f);
                }
            }

            // Floor tread strips.
            for (float z = rear + 0.3f; z < 0.3f; z += 0.3f)
                SceneKit.Box("Tread", shell, new Vector3(0f, 0.003f, z), new Vector3(1.4f, 0.006f, 0.05f), structure, false, default(Vector3), false);
        }

        // ---------------------------------------------------------------- forward window

        public const float WindowHalfWidth = 0.92f;
        public const float WindowBottom = 1.25f;
        public const float WindowTop = 2.03f;

        private void BuildWindow()
        {
            Transform window = SceneKit.Empty("Forward Window", Root, new Vector3(0f, (WindowTop + WindowBottom) * 0.5f, 1.5f)).transform;
            float width = WindowHalfWidth * 2f;
            float height = WindowTop - WindowBottom;
            const float frame = 0.06f, depth = 0.10f;

            // Inner frame, standing proud of the bulkhead into the cabin.
            SceneKit.Box("Frame Top", window, new Vector3(0f, height * 0.5f + frame * 0.5f, -depth * 0.5f), new Vector3(width + frame * 2f, frame, depth), structure);
            SceneKit.Box("Frame Sill", window, new Vector3(0f, -height * 0.5f - frame * 0.5f, -depth * 0.5f), new Vector3(width + frame * 2f, frame, depth), structure);
            for (int side = -1; side <= 1; side += 2)
            {
                SceneKit.Box("Frame Side", window, new Vector3(side * (WindowHalfWidth + frame * 0.5f), 0f, -depth * 0.5f), new Vector3(frame, height, depth), structure);
                SceneKit.Box("Mullion", window, new Vector3(side * 0.33f, 0f, 0.02f), new Vector3(0.032f, height, 0.07f), structure);
            }

            // Frame fasteners.
            for (float x = -WindowHalfWidth; x <= WindowHalfWidth + 0.001f; x += 0.23f)
            {
                SceneKit.Cylinder("Bolt", window, new Vector3(x, height * 0.5f + frame * 0.5f, -depth - 0.002f), 0.009f, 0.006f, panelBlack, Axis.Z);
                SceneKit.Cylinder("Bolt", window, new Vector3(x, -height * 0.5f - frame * 0.5f, -depth - 0.002f), 0.009f, 0.006f, panelBlack, Axis.Z);
            }

            // The pane sits in the middle of the bulkhead thickness.
            GameObject pane = SceneKit.Quad("Pane", window, new Vector3(0f, 0f, 0.06f), new Vector2(width, height), glass);
            pane.GetComponent<Renderer>().receiveShadows = false;
        }

        // ---------------------------------------------------------------- seat

        private void BuildSeat()
        {
            Transform seat = SceneKit.Empty("Command Seat", Root, new Vector3(0f, 0f, -0.12f)).transform;
            SceneKit.Box("Pedestal", seat, new Vector3(0f, 0.19f, 0f), new Vector3(0.36f, 0.38f, 0.42f), structure);
            SceneKit.Box("Pan", seat, new Vector3(0f, 0.44f, 0.06f), new Vector3(0.52f, 0.11f, 0.52f), fabric);
            SceneKit.Box("Back", seat, new Vector3(0f, 0.93f, -0.27f), new Vector3(0.52f, 0.92f, 0.11f), fabric, false, new Vector3(-7f, 0f, 0f));
            SceneKit.Box("Headrest", seat, new Vector3(0f, 1.44f, -0.30f), new Vector3(0.30f, 0.20f, 0.10f), fabric, false, new Vector3(-7f, 0f, 0f));
            for (int side = -1; side <= 1; side += 2)
            {
                SceneKit.Box("Bolster", seat, new Vector3(side * 0.29f, 0.95f, -0.22f), new Vector3(0.06f, 0.78f, 0.20f), fabric, false, new Vector3(-7f, 0f, 0f));
                SceneKit.Box("Armrest", seat, new Vector3(side * 0.31f, 0.63f, 0.05f), new Vector3(0.07f, 0.05f, 0.42f), panelBlack);
                SceneKit.Box("Armrest Post", seat, new Vector3(side * 0.31f, 0.52f, -0.10f), new Vector3(0.04f, 0.20f, 0.05f), structure);
                SceneKit.Box("Harness", seat, new Vector3(side * 0.12f, 0.98f, -0.205f), new Vector3(0.05f, 0.80f, 0.012f), hullDark, false, new Vector3(-7f, 0f, 0f), false);
            }
            SceneKit.Box("Footrest", seat, new Vector3(0f, 0.12f, 0.66f), new Vector3(0.50f, 0.03f, 0.22f), structure, false, new Vector3(-22f, 0f, 0f));
        }

        // ---------------------------------------------------------------- console structure

        private void BuildConsoleStructure()
        {
            Transform console = SceneKit.Empty("Console Structure", Root, Vector3.zero).transform;

            // Solid masses sit strictly below and behind the panel faces, so no structure can cover a control.
            SceneKit.Box("Console Body", console, new Vector3(0f, 0.275f, 1.02f), new Vector3(2.5f, 0.55f, 0.96f), panelBlack, true);
            SceneKit.Box("Display Housing", console, new Vector3(0f, 0.96f, 1.22f), new Vector3(2.5f, 0.52f, 0.50f), panelBlack, true);
            SceneKit.Box("Knee Panel", console, new Vector3(0f, 0.36f, 0.63f), new Vector3(1.02f, 0.44f, 0.20f), panelBlack, true, new Vector3(-12f, 0f, 0f));

            // Glare shield: a hood over the displays. Its front edge stops short of the annunciator row
            // so the row stays in view from the seat, even when acceleration presses the head back.
            SceneKit.Box("Glare Shield", console, new Vector3(0f, 1.205f, 1.01f), new Vector3(2.5f, 0.035f, 0.62f), panel, false, new Vector3(-3f, 0f, 0f));

            // Side console pedestals under the left and right panels.
            for (int side = -1; side <= 1; side += 2)
            {
                SceneKit.Box("Side Pedestal", console, new Vector3(side * 0.70f, 0.30f, 0.32f), new Vector3(0.46f, 0.60f, 0.78f), panelBlack, true, new Vector3(0f, side * 25f, 0f));
                SceneKit.Box("Pedestal Trim", console, new Vector3(side * 0.50f, 0.02f, 0.26f), new Vector3(0.04f, 0.04f, 0.86f), accent, false, new Vector3(0f, side * 25f, 0f), false);
            }

            // Cable conduits running to the overhead.
            for (int side = -1; side <= 1; side += 2)
                SceneKit.Cylinder("Conduit", console, new Vector3(side * 1.16f, 1.55f, 1.40f), 0.022f, 1.0f, structure, Axis.Y);
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
            // Three display units wrapped around the crew member: centre head-on, the outer two angled in.
            CentreDisplayMount = DisplayUnit("Display Unit 2 (Centre)", new Vector3(0f, 0.955f, 0.86f), new Vector3(14f, 0f, 0f));
            LeftDisplayMount = DisplayUnit("Display Unit 1 (Left)", new Vector3(-0.515f, 0.955f, 0.80f), new Vector3(14f, -22f, 0f));
            RightDisplayMount = DisplayUnit("Display Unit 3 (Right)", new Vector3(0.515f, 0.955f, 0.80f), new Vector3(14f, 22f, 0f));
        }

        private Transform DisplayUnit(string name, Vector3 position, Vector3 euler)
        {
            Transform mount = SceneKit.Empty(name, Root, position, euler).transform;
            Vector2 outer = ScreenSize + new Vector2(0.06f, 0.06f);

            // The housing starts just behind the glass plane and is a little larger than the bezel, closing the gaps between units.
            SceneKit.Box("Housing", mount, new Vector3(0f, 0f, 0.002f + 0.07f), new Vector3(outer.x + 0.04f, outer.y + 0.04f, 0.14f), panelBlack, true);
            // Bezel frame around the glass, standing 6 mm proud.
            float bar = 0.03f;
            SceneKit.Box("Bezel Top", mount, new Vector3(0f, ScreenSize.y * 0.5f + bar * 0.5f, -0.003f), new Vector3(outer.x, bar, 0.006f), bezel, false, default(Vector3), false);
            SceneKit.Box("Bezel Bottom", mount, new Vector3(0f, -ScreenSize.y * 0.5f - bar * 0.5f, -0.003f), new Vector3(outer.x, bar, 0.006f), bezel, false, default(Vector3), false);
            SceneKit.Box("Bezel Left", mount, new Vector3(-ScreenSize.x * 0.5f - bar * 0.5f, 0f, -0.003f), new Vector3(bar, ScreenSize.y, 0.006f), bezel, false, default(Vector3), false);
            SceneKit.Box("Bezel Right", mount, new Vector3(ScreenSize.x * 0.5f + bar * 0.5f, 0f, -0.003f), new Vector3(bar, ScreenSize.y, 0.006f), bezel, false, default(Vector3), false);
            SceneKit.Quad("Glass", mount, new Vector3(0f, 0f, -0.0005f), ScreenSize, glassBlack);

            // Soft keys along the lower bezel: dressing that makes the unit read as avionics.
            for (int i = 0; i < 6; i++)
            {
                float x = Mathf.Lerp(-ScreenSize.x * 0.42f, ScreenSize.x * 0.42f, i / 5f);
                SceneKit.Box("Bezel Key", mount, new Vector3(x, -ScreenSize.y * 0.5f - bar * 0.5f, -0.0075f), new Vector3(0.034f, 0.012f, 0.004f), panel, false, default(Vector3), false);
            }
            return mount;
        }

        private void BuildAnnunciators()
        {
            Transform strip = Panel("Annunciator Panel", new Vector3(0f, 1.112f, 0.902f), new Vector3(14f, 0f, 0f), new Vector2(0.56f, 0.044f), false);
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
            Transform p = Panel("Main Control Panel", new Vector3(0f, 0.655f, 0.60f), new Vector3(55f, 0f, 0f), size, true, new Vector3(2.5f, 0.36f, 0.30f));

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
        }

        // ---------------------------------------------------------------- overhead panel

        /// <summary>
        /// Electrical and utility switches on a panel hung from the cabin roof, angled down at the
        /// seat. It sits well above the line of sight to the top of the window.
        /// </summary>
        private void BuildOverheadPanel()
        {
            Vector2 size = new Vector2(0.58f, 0.20f);
            Transform p = Panel("Overhead Panel", new Vector3(0f, 2.02f, 0.70f), new Vector3(-49f, 0f, 0f), size, true, new Vector3(0.62f, 0.24f, 0.12f));

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
            Transform p = Panel("Propulsion Console (Left)", new Vector3(-0.62f, 0.725f, 0.36f), new Vector3(70f, -25f, 0f), size, true, new Vector3(0.42f, 0.62f, 0.22f));
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
            Transform p = Panel("Flight Control Console (Right)", new Vector3(0.62f, 0.725f, 0.36f), new Vector3(70f, 25f, 0f), size, true, new Vector3(0.42f, 0.62f, 0.22f));
            Controls.AddLabel("FLIGHT CONTROL", p, new Vector2(0f, 0.268f), 8f, false);

            Controls.Dial(p, new Vector2(-0.085f, 0.135f), ControlId.NavMode, "NAV MODE", 0f, "{0}", 0f, 2f, new[] { "STBY", "ASCENT", "ORBIT" });
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

        // ---------------------------------------------------------------- dressing

        /// <summary>
        /// Passive detail that makes the cabin read as a working spacecraft: wall padding, the aft
        /// hatch, vents, cable runs, a fire extinguisher and placards. None of it is interactive, and
        /// none of it has a collider, so it cannot get between the crew and a control.
        /// </summary>
        private void BuildDressing()
        {
            Transform dressing = SceneKit.Empty("Dressing", Root, Vector3.zero).transform;
            const float wallX = 1.25f;

            // Quilted padding between the frames, on the side walls and overhead.
            float[] bays = { -0.625f, -0.075f, 0.475f };
            foreach (float z in bays)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    SceneKit.Box("Wall Pad", dressing, new Vector3(side * (wallX - 0.012f), 1.27f, z), new Vector3(0.024f, 0.30f, 0.44f), pad, false, default(Vector3), false);
                    SceneKit.Box("Roof Pad", dressing, new Vector3(side * 0.31f, 2.088f, z), new Vector3(0.50f, 0.024f, 0.44f), pad, false, default(Vector3), false);
                }
            }

            // Cabin air vents on the side walls in the forward bay.
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 at = new Vector3(side * (wallX - 0.012f), 1.30f, 1.02f);
                SceneKit.Box("Vent Frame", dressing, at, new Vector3(0.024f, 0.16f, 0.26f), structure, false, default(Vector3), false);
                for (int i = 0; i < 6; i++)
                    SceneKit.Box("Vent Slat", dressing, at + new Vector3(-side * 0.013f, -0.055f + i * 0.022f, 0f), new Vector3(0.012f, 0.008f, 0.22f), panelBlack, false, new Vector3(0f, 0f, side * 25f), false);
            }

            // Cable runs along the base of each wall, clamped at every frame.
            for (int side = -1; side <= 1; side += 2)
            {
                SceneKit.Cylinder("Cable Run", dressing, new Vector3(side * (wallX - 0.04f), 0.05f, 0.17f), 0.028f, 2.55f, hullDark, Axis.Z);
                SceneKit.Cylinder("Cable Run", dressing, new Vector3(side * (wallX - 0.035f), 0.105f, 0.17f), 0.016f, 2.55f, accent, Axis.Z);
                for (float z = -0.9f; z < 1.45f; z += 0.55f)
                    SceneKit.Box("Cable Clamp", dressing, new Vector3(side * (wallX - 0.04f), 0.07f, z), new Vector3(0.09f, 0.14f, 0.03f), structure, false, default(Vector3), false);
            }

            // Aft hatch in the rear bulkhead.
            Transform hatch = SceneKit.Empty("Aft Hatch", dressing, new Vector3(0f, 1.08f, -1.15f)).transform;
            SceneKit.Cylinder("Coaming", hatch, new Vector3(0f, 0f, 0.02f), 0.47f, 0.04f, structure, Axis.Z);
            SceneKit.Cylinder("Door", hatch, new Vector3(0f, 0f, 0.035f), 0.41f, 0.04f, hullDark, Axis.Z);
            SceneKit.Cylinder("Window", hatch, new Vector3(0f, 0.20f, 0.058f), 0.085f, 0.008f, glassBlack, Axis.Z);
            SceneKit.Cylinder("Handwheel", hatch, new Vector3(0f, -0.08f, 0.075f), 0.13f, 0.02f, structure, Axis.Z);
            SceneKit.Cylinder("Handwheel Hub", hatch, new Vector3(0f, -0.08f, 0.085f), 0.035f, 0.04f, panelBlack, Axis.Z);
            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f * Mathf.Deg2Rad;
                SceneKit.Box("Latch", hatch, new Vector3(Mathf.Cos(angle) * 0.44f, Mathf.Sin(angle) * 0.44f, 0.05f), new Vector3(0.07f, 0.03f, 0.03f), panelBlack,
                    false, new Vector3(0f, 0f, i * 45f), false);
            }
            Placard(dressing, "AFT HATCH", new Vector3(0f, 1.64f, -1.148f), new Vector3(0f, 180f, 0f), 0.30f, 20f, ControlFactory.Amber);

            // Fire extinguisher on the port wall beside the seat.
            Vector3 bottle = new Vector3(-(wallX - 0.075f), 0.50f, -0.20f);
            SceneKit.Cylinder("Extinguisher", dressing, bottle, 0.055f, 0.34f, extinguisherRed, Axis.Y);
            SceneKit.Cylinder("Extinguisher Valve", dressing, bottle + new Vector3(0f, 0.20f, 0f), 0.022f, 0.07f, panelBlack, Axis.Y);
            SceneKit.Box("Extinguisher Strap", dressing, bottle + new Vector3(0f, 0.04f, 0f), new Vector3(0.13f, 0.03f, 0.13f), structure, false, default(Vector3), false);
            Placard(dressing, "FIRE EXT", new Vector3(-(wallX - 0.003f), 0.80f, -0.20f), new Vector3(0f, -90f, 0f), 0.16f, 13f, ControlFactory.Red);

            // Placards.
            Placard(dressing, "PHAINON-1    CREW MODULE 01", new Vector3(0f, WindowTop + 0.03f, 1.397f), Vector3.zero, 0.46f, 13f, ControlFactory.LabelColor);
            Placard(dressing, "EMERGENCY EGRESS  AFT", new Vector3(-(wallX - 0.026f), 1.27f, -0.075f), new Vector3(0f, -90f, 0f), 0.34f, 14f, ControlFactory.Amber);
            Placard(dressing, "CABIN O2  100 KPA MAX", new Vector3(wallX - 0.026f, 1.27f, -0.075f), new Vector3(0f, 90f, 0f), 0.34f, 14f, ControlFactory.Blue);
        }

        /// <summary>A small engraved plate with a line of text, fixed to a surface.</summary>
        private void Placard(Transform parent, string text, Vector3 position, Vector3 euler, float width, float emMillimetres, Color color)
        {
            Transform root = SceneKit.Empty("Placard " + text, parent, position, euler).transform;
            float height = emMillimetres * 0.0022f;
            SceneKit.Box("Plate", root, new Vector3(0f, 0f, 0.002f), new Vector3(width, height, 0.004f), placard, false, default(Vector3), false);
            SceneKit.Label(text, root, new Vector3(0f, 0f, -0.0006f), emMillimetres, color, width * 100f);
        }

        // ---------------------------------------------------------------- lighting

        private void BuildLighting()
        {
            Transform lights = SceneKit.Empty("Cabin Lighting", Root, Vector3.zero).transform;
            var floods = new List<Light>();
            var fixtures = new List<Renderer>();
            var panels = new List<Light>();

            // Two overhead flood fixtures and a dimmer one behind the seat.
            Vector3[] floodPositions = { new Vector3(-0.42f, 2.07f, 0.25f), new Vector3(0.42f, 2.07f, 0.25f), new Vector3(0f, 2.07f, -0.75f) };
            foreach (Vector3 at in floodPositions)
            {
                GameObject housing = SceneKit.Box("Flood Fixture", lights, at, new Vector3(0.34f, 0.03f, 0.11f), fixture, false, default(Vector3), false);
                fixtures.Add(housing.GetComponent<Renderer>());
                floods.Add(SceneKit.PointLight("Flood Light", lights, at + new Vector3(0f, -0.14f, 0f), new Color(1.0f, 0.96f, 0.88f), 1.1f, 4.2f));
            }

            // Hooded lights under the glare shield wash the instrument panel.
            panels.Add(SceneKit.SpotLight("Panel Light", lights, new Vector3(-0.3f, 1.16f, 0.50f), new Vector3(68f, 8f, 0f), new Color(0.80f, 0.90f, 1.0f), 0.5f, 1.4f, 95f));
            panels.Add(SceneKit.SpotLight("Panel Light", lights, new Vector3(0.3f, 1.16f, 0.50f), new Vector3(68f, -8f, 0f), new Color(0.80f, 0.90f, 1.0f), 0.5f, 1.4f, 95f));
            panels.Add(SceneKit.SpotLight("Console Light", lights, new Vector3(-0.66f, 1.25f, 0.30f), new Vector3(80f, 0f, 0f), new Color(0.80f, 0.90f, 1.0f), 0.5f, 1.1f, 80f));
            panels.Add(SceneKit.SpotLight("Console Light", lights, new Vector3(0.66f, 1.25f, 0.30f), new Vector3(80f, 0f, 0f), new Color(0.80f, 0.90f, 1.0f), 0.5f, 1.1f, 80f));

            Light alarm = SceneKit.PointLight("Alarm Beacon", lights, new Vector3(0f, 1.32f, 0.45f), new Color(1f, 0.12f, 0.08f), 0f, 3.2f);
            Light glow = SceneKit.PointLight("Engine Glow", lights, new Vector3(0f, 1.62f, 1.38f), new Color(1f, 0.62f, 0.30f), 0f, 3.5f);

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
