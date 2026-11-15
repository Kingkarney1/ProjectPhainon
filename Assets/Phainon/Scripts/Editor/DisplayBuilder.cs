using Phainon.Displays;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Phainon.EditorTools
{
    /// <summary>
    /// Builds the three cockpit display units. Each one gets its own world-space canvas mounted on
    /// its screen glass, its own display script and its own widget hierarchy. Layouts are authored
    /// on a 1000 x 625 pixel grid.
    /// </summary>
    public class DisplayBuilder
    {
        public const float CanvasWidth = 1000f;
        public const float CanvasHeight = 625f;

        private readonly AssetFactory assets;
        private readonly SpacecraftController spacecraft;
        private readonly UiKit ui;
        private readonly CockpitTheme theme;

        public DisplayBuilder(AssetFactory assets, SpacecraftController spacecraft)
        {
            this.assets = assets;
            this.spacecraft = spacecraft;
            ui = new UiKit(assets);
            theme = assets.Theme;
        }

        // ---------------------------------------------------------------- shared frame

        private class Frame
        {
            public Canvas canvas;
            public RectTransform content;
            public CanvasGroup contentGroup;
            public CanvasGroup bootGroup;
            public TMP_Text bootText;
            public TMP_Text headerCentre;
            public TMP_Text headerRight;
            public Light glow;
        }

        private Frame CreateFrame(Transform mount, string title, string unitName)
        {
            var frame = new Frame();

            var canvasObject = new GameObject(title + " Canvas", typeof(RectTransform));
            canvasObject.layer = BuildPaths.LayerCockpit;
            var canvasRect = (RectTransform)canvasObject.transform;
            canvasRect.SetParent(mount, false);
            canvasRect.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);
            // Scale the pixel grid down to the physical screen, and sit a hair in front of the glass.
            canvasRect.localScale = Vector3.one * (CockpitBuilder.ScreenSize.x / CanvasWidth);
            canvasRect.localPosition = new Vector3(0f, 0f, -0.0015f);

            frame.canvas = canvasObject.AddComponent<Canvas>();
            frame.canvas.renderMode = RenderMode.WorldSpace;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 3f;

            RectTransform background = ui.Fill("Background", canvasRect);
            ui.Image(background, theme.screenBackground);

            frame.content = ui.Fill("Content", canvasRect);
            frame.contentGroup = frame.content.gameObject.AddComponent<CanvasGroup>();
            frame.contentGroup.blocksRaycasts = false;
            frame.contentGroup.interactable = false;

            // Header bar, common to all three units.
            ui.Image("Header", frame.content, 0f, 0f, CanvasWidth, 48f, theme.panelFill);
            ui.Image("Header Rule", frame.content, 0f, 47f, CanvasWidth, 1f, theme.panelOutline);
            ui.Image("Header Accent", frame.content, 0f, 46f, 236f, 2f, theme.accentBlue);
            // The unit's tag, as stencilled on the bezel of a real display unit: DU1, DU2, DU3.
            string tag = "DU" + unitName.Substring(unitName.Length - 1);
            ui.Image("Tag", frame.content, 14f, 12f, 46f, 24f, theme.accentBlue);
            ui.Text("Tag Text", frame.content, 14f, 12f, 46f, 24f, tag, 15f, theme.screenBackground, TextAlignmentOptions.Center, FontStyles.Bold);
            TextMeshProUGUI heading = ui.Text("Title", frame.content, 72f, 0f, 280f, 48f, title, 20f, theme.textPrimary, TextAlignmentOptions.Left, FontStyles.Bold);
            heading.characterSpacing = 8f;
            frame.headerCentre = ui.Text("Header Centre", frame.content, 320f, 0f, 360f, 48f, "", 21f, theme.accentBlue, TextAlignmentOptions.Center, FontStyles.Bold);
            frame.headerRight = ui.Text("Header Right", frame.content, 690f, 0f, 294f, 48f, "", 24f, theme.nominalGreen, TextAlignmentOptions.Right, FontStyles.Bold);

            // The glass itself: darker towards the corners. Part of the content, so it fades with the screen.
            RectTransform vignette = ui.Fill("Vignette", frame.content);
            ui.Image(vignette, new Color(0f, 0f, 0f, 0.55f), assets.Vignette);

            // What the lit screen throws onto its surround. The display drives it with its own brightness.
            frame.glow = SceneKit.PointLight("Screen Glow", mount, new Vector3(0f, 0f, -0.05f), new Color(0.45f, 0.68f, 0.95f), 0f, 0.5f);

            RectTransform boot = ui.Fill("Boot Screen", canvasRect);
            frame.bootGroup = boot.gameObject.AddComponent<CanvasGroup>();
            frame.bootGroup.blocksRaycasts = false;
            frame.bootGroup.interactable = false;
            frame.bootText = ui.Text("Boot Text", boot, 60f, 60f, CanvasWidth - 120f, CanvasHeight - 120f, "", 24f, theme.nominalGreen, TextAlignmentOptions.TopLeft);

            return frame;
        }

        private void Bind(CockpitDisplay display, Frame frame, string unitName)
        {
            display.spacecraft = spacecraft;
            display.theme = theme;
            display.content = frame.contentGroup;
            display.bootScreen = frame.bootGroup;
            display.bootText = frame.bootText;
            display.unitName = unitName;
            display.glow = frame.glow;
        }

        // ---------------------------------------------------------------- display 1: flight and navigation

        public FlightNavDisplay BuildFlightNav(Transform mount)
        {
            Frame frame = CreateFrame(mount, "FLIGHT / NAV", "DISPLAY UNIT 1");
            var display = frame.canvas.gameObject.AddComponent<FlightNavDisplay>();
            Bind(display, frame, "DISPLAY UNIT 1  FLIGHT / NAV");
            display.phaseText = frame.headerCentre;
            display.clockText = frame.headerRight;

            // Flight data column.
            RectTransform data = ui.Section(frame.content, 10f, 58f, 262f, 478f, "FLIGHT DATA");
            float y = 36f;
            const float row = 48f;
            display.altitude = ui.Readout(data, 12f, y, 238f, row, "ALTITUDE", "KM", 32f); y += row;
            display.velocity = ui.Readout(data, 12f, y, 238f, row, "VELOCITY", "M/S", 32f); y += row;
            display.verticalSpeed = ui.Readout(data, 12f, y, 238f, row, "VERT SPD", "M/S"); y += row;
            display.downrange = ui.Readout(data, 12f, y, 238f, row, "DOWNRANGE", "KM"); y += row;
            display.apoapsis = ui.Readout(data, 12f, y, 238f, row, "APOAPSIS", "KM"); y += row;
            display.periapsis = ui.Readout(data, 12f, y, 238f, row, "PERIAPSIS", "KM"); y += row;
            display.gLoad = ui.Readout(data, 12f, y, 238f, row, "G LOAD", "G"); y += row;
            display.dynamicPressure = ui.Readout(data, 12f, y, 238f, row, "DYN PRESS", "KPA"); y += row;
            display.mach = ui.Readout(data, 12f, y, 238f, row, "MACH", "");

            // Attitude column.
            RectTransform attitudeSection = ui.Section(frame.content, 282f, 58f, 330f, 478f, "ATTITUDE");
            display.attitude = BuildAttitudeIndicator(attitudeSection, 20f, 38f, 290f);
            display.pitch = ui.StackedReadout(attitudeSection, 14f, 338f, 98f, 70f, "PITCH", "DEG");
            display.roll = ui.StackedReadout(attitudeSection, 116f, 338f, 98f, 70f, "ROLL", "DEG");
            display.yaw = ui.StackedReadout(attitudeSection, 218f, 338f, 98f, 70f, "YAW", "DEG");
            display.guidanceStatus = ui.Status(attitudeSection, 16f, 426f, 298f, 34f, "GUIDANCE", 17f);

            // Navigation page.
            RectTransform nav = ui.Section(frame.content, 622f, 58f, 368f, 478f, "");
            display.navTitle = ui.Text("Title", nav, 12f, 6f, 340f, 20f, "NAVIGATION", 15f, theme.accentBlue, TextAlignmentOptions.Left, FontStyles.Bold);
            ui.Image("Title Rule", nav, 12f, 28f, 344f, 1.5f, theme.panelOutline);
            RectTransform page = ui.Rect("Page", nav, 12f, 38f, 344f, 428f);
            display.trajectory = BuildTrajectoryPlot(page);
            display.orbitMap = BuildOrbitMap(page);

            RectTransform overlay = ui.Fill("Nav Message", page);
            display.navOverlay = overlay.gameObject;
            display.navOverlayText = ui.Text(ui.Fill("Text", overlay), "NAV SYSTEM OFF", 26f, theme.textDim, TextAlignmentOptions.Center, FontStyles.Bold);

            // Orbital velocity strip.
            RectTransform orbit = ui.Section(frame.content, 10f, 546f, 980f, 70f, "");
            display.orbitalVelocity = ui.Bar(orbit, 16f, 10f, 700f, 50f, "ORBITAL VELOCITY");
            display.orbitStatus = ui.Status(orbit, 740f, 18f, 224f, 34f, "ORBIT", 18f);

            return display;
        }

        private AttitudeIndicator BuildAttitudeIndicator(Transform parent, float x, float y, float size)
        {
            const float pixelsPerDegree = 4f;
            RectTransform viewport = ui.Rect("Attitude Indicator", parent, x, y, size, size);
            ui.Image(viewport, theme.screenBackground);
            viewport.gameObject.AddComponent<RectMask2D>();
            var indicator = viewport.gameObject.AddComponent<AttitudeIndicator>();
            indicator.pixelsPerDegree = pixelsPerDegree;

            RectTransform rollRoot = ui.Centred("Roll", viewport, 0f, 0f);
            RectTransform pitchRoot = ui.Centred("Pitch", rollRoot, 0f, 0f);
            indicator.rollRoot = rollRoot;
            indicator.pitchRoot = pitchRoot;

            // Sky above the horizon line, ground below. Both are far larger than the viewport so no edge ever shows.
            const float extent = 1400f;
            RectTransform sky = ui.Centred("Sky", pitchRoot, extent, extent);
            sky.pivot = new Vector2(0.5f, 0f);
            ui.Image(sky, new Color(0.10f, 0.30f, 0.58f));
            RectTransform ground = ui.Centred("Ground", pitchRoot, extent, extent);
            ground.pivot = new Vector2(0.5f, 1f);
            ui.Image(ground, new Color(0.30f, 0.20f, 0.10f));
            ui.Image(ui.Centred("Horizon", pitchRoot, extent, 3f), Color.white);

            // Pitch ladder every ten degrees.
            for (int degrees = -90; degrees <= 90; degrees += 10)
            {
                if (degrees == 0) continue;
                bool major = degrees % 30 == 0;
                RectTransform rung = ui.Centred("Rung " + degrees, pitchRoot, major ? 96f : 56f, 2f);
                rung.anchoredPosition = new Vector2(0f, degrees * pixelsPerDegree);
                ui.Image(rung, new Color(1f, 1f, 1f, 0.85f));

                RectTransform tag = ui.Centred("Tag " + degrees, pitchRoot, 40f, 18f);
                tag.anchoredPosition = new Vector2((major ? 48f : 28f) + 24f, degrees * pixelsPerDegree);
                ui.Text(tag, Mathf.Abs(degrees).ToString(), 13f, Color.white, TextAlignmentOptions.Center);
            }

            // Velocity vector marker.
            RectTransform marker = ui.Centred("Flight Path Marker", viewport, 24f, 24f);
            ui.Image(marker, theme.nominalGreen, assets.Ring);
            indicator.flightPathMarker = marker;

            // Fixed vehicle symbol.
            RectTransform left = ui.Centred("Wing Left", viewport, 62f, 5f);
            left.anchoredPosition = new Vector2(-66f, 0f);
            ui.Image(left, theme.cautionAmber);
            RectTransform right = ui.Centred("Wing Right", viewport, 62f, 5f);
            right.anchoredPosition = new Vector2(66f, 0f);
            ui.Image(right, theme.cautionAmber);
            ui.Image(ui.Centred("Centre Dot", viewport, 9f, 9f), theme.cautionAmber, assets.Circle);

            return indicator;
        }

        private TrajectoryPlot BuildTrajectoryPlot(Transform parent)
        {
            RectTransform root = ui.Fill("Ascent Profile", parent);
            var plot = root.gameObject.AddComponent<TrajectoryPlot>();

            // Plot area leaves room for the axis captions.
            var areaObject = new GameObject("Plot Area", typeof(RectTransform));
            areaObject.layer = root.gameObject.layer;
            var area = (RectTransform)areaObject.transform;
            area.SetParent(root, false);
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(8f, 30f);
            area.offsetMax = new Vector2(-8f, -30f);
            ui.Image(area, new Color(theme.gaugeTrack.r, theme.gaugeTrack.g, theme.gaugeTrack.b, 0.45f));

            for (int i = 1; i < 5; i++)
            {
                RectTransform grid = ui.Fill("Grid", area);
                grid.anchorMin = new Vector2(0f, i * 0.2f);
                grid.anchorMax = new Vector2(1f, i * 0.2f);
                grid.offsetMin = Vector2.zero;
                grid.offsetMax = new Vector2(0f, 1f);
                ui.Image(grid, theme.panelOutline);
            }

            RectTransform target = ui.Fill("Target Orbit", area);
            target.offsetMin = Vector2.zero;
            target.offsetMax = new Vector2(0f, 2f);
            ui.Image(target, theme.cautionAmber);
            RectTransform targetTag = ui.Centred("Target Tag", target, 150f, 18f);
            targetTag.anchorMin = targetTag.anchorMax = new Vector2(1f, 1f);
            targetTag.pivot = new Vector2(1f, 0f);
            targetTag.anchoredPosition = new Vector2(-4f, 2f);
            ui.Text(targetTag, "TARGET ORBIT", 12.5f, theme.cautionAmber, TextAlignmentOptions.Right);
            plot.targetLine = target;

            plot.path = ui.Line("Path", area, theme.nominalGreen, 3f);

            RectTransform marker = ui.Centred("Vehicle", area, 16f, 16f);
            ui.Image(marker, Color.white, assets.Ring);
            plot.marker = marker;

            plot.altitudeLabel = ui.Text("Altitude Scale", root, 8f, 4f, 200f, 22f, "250 KM", 14f, theme.textDim, TextAlignmentOptions.Left);
            ui.Text("Altitude Caption", root, 150f, 4f, 186f, 22f, "ALTITUDE", 13f, theme.textDim, TextAlignmentOptions.Right);
            plot.rangeLabel = ui.Text("Range Scale", root, 136f, 402f, 200f, 22f, "300 KM", 14f, theme.textDim, TextAlignmentOptions.Right);
            ui.Text("Range Caption", root, 8f, 402f, 200f, 22f, "DOWNRANGE", 13f, theme.textDim, TextAlignmentOptions.Left);
            return plot;
        }

        private OrbitMap BuildOrbitMap(Transform parent)
        {
            // A square map centred in the page.
            RectTransform root = ui.Rect("Orbit Map", parent, 2f, 44f, 340f, 340f);
            var map = root.gameObject.AddComponent<OrbitMap>();

            RectTransform earth = ui.Fill("Earth", root);
            ui.Image(earth, new Color(0.05f, 0.20f, 0.40f), assets.Circle);
            RectTransform limb = ui.Fill("Limb", earth, -3f);
            ui.Image(limb, new Color(0.35f, 0.65f, 1f, 0.55f), assets.Ring);
            map.earth = earth;

            map.orbit = ui.Line("Predicted Orbit", root, new Color(theme.accentBlue.r, theme.accentBlue.g, theme.accentBlue.b, 0.75f), 2f);
            map.track = ui.Line("Ground Track", root, theme.nominalGreen, 3f);

            RectTransform site = ui.Centred("Launch Site", root, 9f, 9f);
            ui.Image(site, theme.cautionAmber);
            map.launchSite = site;

            RectTransform marker = ui.Centred("Vehicle", root, 14f, 14f);
            ui.Image(marker, Color.white, assets.Circle);
            map.marker = marker;

            // The caption belongs to the map object so it hides with the page.
            ui.Text("Caption", root, -2f, -40f, 344f, 22f, "EARTH-RELATIVE POSITION   ALTITUDE EXAGGERATED", 12.5f, theme.textDim, TextAlignmentOptions.Center);
            return map;
        }

        // ---------------------------------------------------------------- display 2: systems

        public SystemsDisplay BuildSystems(Transform mount)
        {
            Frame frame = CreateFrame(mount, "SYSTEMS", "DISPLAY UNIT 2");
            var display = frame.canvas.gameObject.AddComponent<SystemsDisplay>();
            Bind(display, frame, "DISPLAY UNIT 2  SYSTEMS");
            frame.headerCentre.text = "SPACECRAFT SYSTEMS";
            frame.headerRight.text = assets.Vehicle.spec.vehicleName;

            // Propulsion.
            RectTransform prop = ui.Section(frame.content, 10f, 58f, 322f, 302f, "PROPULSION");
            display.engineStatus = ui.Status(prop, 14f, 36f, 294f, 28f, "MAIN ENGINE", 17f);
            display.stageName = ui.Text("Stage", prop, 14f, 64f, 294f, 20f, "STAGE 1", 13.5f, theme.textDim, TextAlignmentOptions.Left);
            display.thrust = ui.Bar(prop, 14f, 88f, 294f, 40f, "THRUST");
            display.throttleCommand = ui.StackedReadout(prop, 14f, 132f, 94f, 60f, "THROTTLE", "%", 22f);
            display.thrustLimit = ui.StackedReadout(prop, 114f, 132f, 94f, 60f, "LIMIT", "%", 22f);
            display.chamberTemp = ui.StackedReadout(prop, 214f, 132f, 94f, 60f, "CHAMBER", "K", 22f);
            display.boosterFuel = ui.Bar(prop, 14f, 196f, 294f, 34f, "FUEL  BOOSTER");
            display.upperFuel = ui.Bar(prop, 14f, 230f, 294f, 34f, "FUEL  UPPER STAGE");
            display.feedPressure = ui.Bar(prop, 14f, 264f, 294f, 34f, "FEED PRESSURE");

            // Electrical.
            RectTransform elec = ui.Section(frame.content, 340f, 58f, 322f, 196f, "ELECTRICAL");
            display.busVoltage = ui.StackedReadout(elec, 14f, 34f, 140f, 62f, "MAIN BUS", "VOLTS", 26f);
            display.busLoad = ui.StackedReadout(elec, 168f, 34f, 140f, 62f, "LOAD", "KW", 26f);
            display.apuStatus = ui.Status(elec, 14f, 98f, 294f, 26f, "APU");
            display.batteryA = ui.Bar(elec, 14f, 124f, 294f, 34f, "BATTERY A");
            display.batteryB = ui.Bar(elec, 14f, 158f, 294f, 34f, "BATTERY B");

            // Communications.
            RectTransform comms = ui.Section(frame.content, 340f, 262f, 322f, 98f, "COMMUNICATIONS");
            display.commStatus = ui.Status(comms, 14f, 32f, 294f, 26f, "S-BAND LINK");
            display.signal = ui.Bar(comms, 14f, 58f, 294f, 34f, "SIGNAL");

            // Cabin.
            RectTransform cabin = ui.Section(frame.content, 670f, 58f, 320f, 302f, "CABIN ENVIRONMENT");
            display.cabinStatus = ui.Status(cabin, 14f, 36f, 292f, 28f, "CABIN LOOP", 17f);
            display.oxygen = ui.Bar(cabin, 14f, 72f, 292f, 40f, "OXYGEN SUPPLY");
            display.cabinPressure = ui.Readout(cabin, 14f, 122f, 292f, 52f, "PRESSURE", "KPA", 30f);
            display.cabinTemp = ui.Readout(cabin, 14f, 176f, 292f, 52f, "TEMP", "C", 30f);
            display.cabinSetpoint = ui.Readout(cabin, 14f, 230f, 292f, 52f, "SETPOINT", "C", 24f);

            // GO / NO-GO board: two columns of five.
            RectTransform board = ui.Section(frame.content, 10f, 368f, 566f, 248f, "");
            display.boardTitle = ui.Text("Board Title", board, 14f, 6f, 538f, 24f, "LAUNCH STATUS", 17f, theme.cautionAmber, TextAlignmentOptions.Left, FontStyles.Bold);
            ui.Image("Title Rule", board, 12f, 32f, 542f, 1.5f, theme.panelOutline);
            display.checklistRows = new StatusRow[10];
            for (int i = 0; i < display.checklistRows.Length; i++)
            {
                float columnX = i < 5 ? 14f : 290f;
                float rowY = 42f + (i % 5) * 40f;
                display.checklistRows[i] = ui.Status(board, columnX, rowY, 262f, 34f, "--", 16f);
            }

            // Caution and warning list.
            RectTransform alerts = ui.Section(frame.content, 584f, 368f, 406f, 248f, "CAUTION / WARNING");
            display.warningSummary = ui.Text("Summary", alerts, 14f, 36f, 378f, 24f, "NO ACTIVE ALERTS", 15f, theme.nominalGreen, TextAlignmentOptions.Left, FontStyles.Bold);
            display.warningRows = new TMP_Text[5];
            for (int i = 0; i < display.warningRows.Length; i++)
                display.warningRows[i] = ui.Text("Alert " + i, alerts, 14f, 66f + i * 35f, 378f, 32f, "", 16.5f, theme.warningRed, TextAlignmentOptions.Left, FontStyles.Bold);

            return display;
        }

        // ---------------------------------------------------------------- display 3: external view and mission

        public MissionDisplay BuildMission(Transform mount)
        {
            Frame frame = CreateFrame(mount, "MISSION / EXT VIEW", "DISPLAY UNIT 3");
            var display = frame.canvas.gameObject.AddComponent<MissionDisplay>();
            Bind(display, frame, "DISPLAY UNIT 3  MISSION / EXT VIEW");
            display.phaseTitle = frame.headerCentre;
            display.clock = frame.headerRight;

            // External camera feed.
            RectTransform feedFrame = ui.Rect("Camera Feed", frame.content, 10f, 58f, 600f, 375f);
            ui.Image(feedFrame, Color.black);
            RectTransform feedRect = ui.Fill("Feed", feedFrame, 2f);
            var feed = feedRect.gameObject.AddComponent<RawImage>();
            feed.texture = assets.ExternalCameraTexture;
            feed.raycastTarget = false;
            display.feed = feed;

            RectTransform noSignal = ui.Fill("No Signal", feedFrame, 2f);
            ui.Image(noSignal, new Color(0.02f, 0.025f, 0.03f));
            ui.Text(ui.Fill("Text", noSignal), "CAMERA OFF\n<size=60%>NO VIDEO SIGNAL</size>", 30f, theme.textDim, TextAlignmentOptions.Center, FontStyles.Bold);
            display.noSignal = noSignal.gameObject;
            noSignal.gameObject.SetActive(false);

            // Camera overlay: caption bar, record lamp and corner brackets.
            ui.Image("Caption Bar", feedFrame, 2f, 343f, 596f, 30f, new Color(0f, 0f, 0f, 0.55f));
            display.feedCaption = ui.Text("Caption", feedFrame, 12f, 343f, 576f, 30f, "CAM 1  AFT HULL", 15f, theme.textPrimary, TextAlignmentOptions.Left);
            display.recordLamp = ui.Image("Record Lamp", feedFrame, 14f, 14f, 12f, 12f, theme.warningRed, assets.Circle);
            ui.Text("Record Text", feedFrame, 32f, 8f, 80f, 24f, "LIVE", 14f, theme.textPrimary, TextAlignmentOptions.Left, FontStyles.Bold);
            Color bracket = new Color(1f, 1f, 1f, 0.5f);
            for (int corner = 0; corner < 4; corner++)
            {
                float bx = corner % 2 == 0 ? 190f : 388f;
                float by = corner < 2 ? 120f : 232f;
                ui.Image("Bracket H", feedFrame, bx, by, 22f, 2f, bracket);
                ui.Image("Bracket V", feedFrame, corner % 2 == 0 ? bx : bx + 20f, corner < 2 ? by : by - 20f, 2f, 22f, bracket);
            }

            // Stage status under the feed.
            RectTransform stages = ui.Section(frame.content, 10f, 441f, 600f, 175f, "STAGE STATUS");
            display.stagesTitle = stages.GetComponentInChildren<TMP_Text>();
            display.boosterStatus = ui.Status(stages, 14f, 40f, 572f, 38f, "CORE BOOSTER", 18f);
            display.upperStageStatus = ui.Status(stages, 14f, 82f, 572f, 38f, "UPPER STAGE", 18f);
            display.separationStatus = ui.Status(stages, 14f, 124f, 572f, 38f, "STAGE SEPARATION", 18f);

            // Mission column.
            RectTransform mission = ui.Section(frame.content, 620f, 58f, 370f, 250f, "");
            display.missionName = ui.Text("Mission Name", mission, 14f, 8f, 342f, 50f, "OPERATION PHAINON", 21f, theme.textPrimary, TextAlignmentOptions.TopLeft, FontStyles.Bold);
            ui.Image("Rule", mission, 12f, 62f, 346f, 1.5f, theme.panelOutline);
            ui.Text("Objective Caption", mission, 14f, 68f, 342f, 20f, "CURRENT OBJECTIVE", 13.5f, theme.accentBlue, TextAlignmentOptions.Left, FontStyles.Bold);
            display.objective = ui.Text("Objective", mission, 14f, 90f, 342f, 104f, "", 18f, theme.textPrimary, TextAlignmentOptions.TopLeft, FontStyles.Normal, true);
            display.progress = ui.Bar(mission, 14f, 200f, 342f, 42f, "MISSION PROGRESS");

            // Flight event timeline.
            RectTransform events = ui.Section(frame.content, 620f, 316f, 370f, 300f, "FLIGHT EVENTS");
            display.eventsTitle = events.GetComponentInChildren<TMP_Text>();
            display.eventRows = new StatusRow[7];
            for (int i = 0; i < display.eventRows.Length; i++)
                display.eventRows[i] = ui.Status(events, 14f, 38f + i * 36f, 342f, 32f, "--", 15.5f);

            return display;
        }
    }
}
