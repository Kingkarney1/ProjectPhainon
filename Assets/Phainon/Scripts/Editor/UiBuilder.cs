using Phainon.Audio;
using Phainon.Cockpit;
using Phainon.Story;
using Phainon.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Phainon.EditorTools
{
    /// <summary>
    /// Builds the screen-space interface: the in-flight HUD and the menus. Both share one overlay
    /// canvas authored at 1920 x 1080 and scaled to the window.
    /// </summary>
    public class UiBuilder
    {
        public HudController Hud { get; private set; }
        public MenuController Menus { get; private set; }

        private readonly AssetFactory assets;
        private readonly UiKit ui;
        private readonly CockpitTheme theme;
        private RectTransform canvasRect;

        private static readonly Color PanelColor = new Color(0.03f, 0.045f, 0.06f, 0.88f);

        public UiBuilder(AssetFactory assets)
        {
            this.assets = assets;
            ui = new UiKit(assets);
            theme = assets.Theme;
        }

        public void Build(SpacecraftController spacecraft, GameManager game, CockpitInteractionManager interaction,
            CockpitControlRouter router, CockpitCameraController cockpitCamera, Camera viewCamera, AudioManager audio)
        {
            var canvasObject = new GameObject("Interface Canvas", typeof(RectTransform));
            canvasObject.layer = LayerMask.NameToLayer("UI");
            canvasRect = (RectTransform)canvasObject.transform;

            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            canvasObject.AddComponent<GraphicRaycaster>();

            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();

            BuildHud(spacecraft, game, interaction, router, cockpitCamera);
            BuildStorySequences(spacecraft, game, cockpitCamera, viewCamera, audio);
            BuildMenus(spacecraft, game);
        }

        /// <summary>The story's two full-screen sequences: the wake-up in the seat, and over it the introduction.</summary>
        private void BuildStorySequences(SpacecraftController spacecraft, GameManager game, CockpitCameraController cockpitCamera,
            Camera viewCamera, AudioManager audio)
        {
            RectTransform wakeRoot = ui.Fill("Wake Up", canvasRect);
            var wake = wakeRoot.gameObject.AddComponent<WakeUpSequence>();
            wake.game = game;
            wake.spacecraft = spacecraft;
            wake.cockpitCamera = cockpitCamera;
            wake.audioManager = audio;
            wake.group = wakeRoot.gameObject.AddComponent<CanvasGroup>();
            wake.group.blocksRaycasts = false;
            // Haze first, so the eyelids and the dark close over it.
            wake.haze = ui.Image(ui.Fill("Haze", wakeRoot), new Color(0.62f, 0.66f, 0.70f, 0f));
            wake.lidTop = ui.Fill("Upper Eyelid", wakeRoot);
            ui.Image(wake.lidTop, Color.black);
            wake.lidBottom = ui.Fill("Lower Eyelid", wakeRoot);
            ui.Image(wake.lidBottom, Color.black);
            wake.black = ui.Image(ui.Fill("Dark", wakeRoot), Color.black);

            RectTransform introRoot = ui.Fill("Introduction", canvasRect);
            var intro = introRoot.gameObject.AddComponent<IntroSequence>();
            intro.game = game;
            intro.spacecraft = spacecraft;
            intro.campaign = assets.Story;
            intro.cockpitCamera = cockpitCamera;
            intro.viewCamera = viewCamera;
            intro.skyMask = 1 << BuildPaths.LayerEnvironment;
            intro.group = introRoot.gameObject.AddComponent<CanvasGroup>();
            intro.group.blocksRaycasts = false;
            // A light veil keeps the words readable over the brightest stars.
            ui.Image(ui.Fill("Veil", introRoot), new Color(0f, 0f, 0f, 0.35f));
            RectTransform words = Pinned("Line", introRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1300f, 400f));
            intro.line = ui.Text(words, "", 40f, theme.textPrimary, TextAlignmentOptions.Center, FontStyles.Normal, true);
            intro.line.lineSpacing = 12f;
            RectTransform skip = Pinned("Skip Hint", introRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(600f, 24f));
            ui.Text(skip, "Space to skip", 15f, new Color(theme.textDim.r, theme.textDim.g, theme.textDim.b, 0.7f), TextAlignmentOptions.Center);
            intro.blackout = ui.Image(ui.Fill("Blackout", introRoot), new Color(0f, 0f, 0f, 0f));
        }

        /// <summary>A rect pinned to a point of its parent (anchor) by its own pivot, offset in pixels.</summary>
        private RectTransform Pinned(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 offset, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
            return rect;
        }

        // ---------------------------------------------------------------- HUD

        private void BuildHud(SpacecraftController spacecraft, GameManager game, CockpitInteractionManager interaction,
            CockpitControlRouter router, CockpitCameraController cockpitCamera)
        {
            RectTransform root = ui.Fill("HUD", canvasRect);
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            Hud = root.gameObject.AddComponent<HudController>();
            Hud.spacecraft = spacecraft;
            Hud.game = game;
            Hud.interaction = interaction;
            Hud.router = router;
            Hud.cockpitCamera = cockpitCamera;
            Hud.theme = theme;
            Hud.root = group;

            // Objective guide, top left.
            RectTransform guide = Pinned("Objective Guide", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -28f), new Vector2(600f, 168f));
            ui.Image(guide, PanelColor, assets.RoundedPanel, true);
            ui.Image("Accent", guide, 0f, 18f, 4f, 132f, theme.accentBlue);
            Hud.phaseTitle = ui.Text("Phase", guide, 22f, 12f, 380f, 30f, "PRE-LAUNCH", 23f, theme.accentBlue, TextAlignmentOptions.Left, FontStyles.Bold);
            Hud.clockText = ui.Text("Clock", guide, 380f, 12f, 200f, 30f, "T-00:00:15", 22f, theme.nominalGreen, TextAlignmentOptions.Right, FontStyles.Bold);
            Hud.objectiveText = ui.Text("Objective", guide, 22f, 48f, 558f, 84f, "", 20f, theme.textPrimary, TextAlignmentOptions.TopLeft, FontStyles.Normal, true);
            ui.Text("Hint", guide, 22f, 138f, 558f, 22f, "The next control pulses amber.   H hides this guide.", 14f, theme.textDim, TextAlignmentOptions.Left);
            Hud.guidePanel = guide.gameObject;

            // Notifications, top centre.
            Hud.toastRows = new TMP_Text[4];
            for (int i = 0; i < Hud.toastRows.Length; i++)
            {
                RectTransform row = Pinned("Notification " + i, root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f - i * 34f), new Vector2(900f, 32f));
                Hud.toastRows[i] = ui.Text(row, "", 22f, theme.accentBlue, TextAlignmentOptions.Center, FontStyles.Bold);
            }

            // Warning banner under the notifications.
            RectTransform warning = Pinned("Warning Banner", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -178f), new Vector2(720f, 78f));
            Hud.warningGroup = warning.gameObject.AddComponent<CanvasGroup>();
            Hud.warningGroup.alpha = 0f;
            Hud.warningBackground = ui.Image(warning, theme.warningRed, assets.RoundedPanel, true);
            Hud.warningText = ui.Text(ui.Fill("Text", warning, 8f), "", 26f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);

            // Interaction prompt, bottom centre.
            RectTransform prompt = Pinned("Interaction Prompt", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(620f, 84f));
            Hud.promptGroup = prompt.gameObject.AddComponent<CanvasGroup>();
            Hud.promptGroup.alpha = 0f;
            ui.Image(prompt, PanelColor, assets.RoundedPanel, true);
            Hud.promptTitle = ui.Text("Control", prompt, 20f, 10f, 580f, 34f, "", 24f, theme.textPrimary, TextAlignmentOptions.Center, FontStyles.Bold);
            Hud.promptAction = ui.Text("Action", prompt, 20f, 46f, 580f, 28f, "", 18f, theme.textDim, TextAlignmentOptions.Center);

            // Centre reticle, shown while looking around.
            RectTransform reticle = Pinned("Reticle", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f));
            Hud.reticle = ui.Image(reticle, theme.textDim, assets.Ring);
            Hud.reticle.enabled = false;

            // Key reminder, bottom left.
            RectTransform keys = Pinned("Key Reminder", root, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(28f, 22f), new Vector2(1300f, 26f));
            ui.Text(keys, "RMB look    LMB operate    Scroll adjust    1-6 views    Z zoom    Shift / Ctrl throttle    W S A D Q E attitude    7 8 9 radio    C radio log    J log    , . time rate    Esc pause",
                15f, new Color(theme.textDim.r, theme.textDim.g, theme.textDim.b, 0.8f), TextAlignmentOptions.Left);

            BuildComms(root, spacecraft, game);
            BuildJournal(root, spacecraft, game);
        }

        /// <summary>The astronaut's log, left of centre and clear of the radio. Shown with J on a story mission.</summary>
        private void BuildJournal(RectTransform root, SpacecraftController spacecraft, GameManager game)
        {
            RectTransform panel = Pinned("Log", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -210f), new Vector2(760f, 760f));
            ui.Image(panel, PanelColor, assets.RoundedPanel, true);
            ui.Image("Accent", panel, 0f, 18f, 4f, 724f, theme.accentBlue);
            ui.Text("Title", panel, 22f, 10f, 500f, 22f, "LOG   new first   J closes", 14f, theme.textDim, TextAlignmentOptions.Left, FontStyles.Bold);

            var journal = root.gameObject.AddComponent<JournalPanel>();
            journal.spacecraft = spacecraft;
            journal.game = game;
            journal.theme = theme;
            journal.panel = panel.gameObject;
            journal.body = ui.Text("Entries", panel, 22f, 40f, 716f, 706f, "", 17f, theme.textPrimary, TextAlignmentOptions.TopLeft, FontStyles.Normal, true);
        }

        /// <summary>The radio panel, top right and clear of the displays, with the message log below it.</summary>
        private void BuildComms(RectTransform root, SpacecraftController spacecraft, GameManager game)
        {
            const float width = 500f;
            const float inner = width - 44f;
            var corner = new Vector2(1f, 1f);
            RectTransform panel = Pinned("Ground Control", root, corner, corner, new Vector2(-28f, -28f), new Vector2(width, 250f));
            ui.Image(panel, PanelColor, assets.RoundedPanel, true);
            ui.Image("Accent", panel, 0f, 18f, 4f, 214f, theme.accentBlue);

            var comms = panel.gameObject.AddComponent<CommsPanel>();
            comms.spacecraft = spacecraft;
            comms.game = game;
            comms.theme = theme;
            comms.group = panel.gameObject.AddComponent<CanvasGroup>();

            ui.Text("Title", panel, 22f, 10f, 200f, 20f, "GROUND CONTROL", 14f, theme.textDim, TextAlignmentOptions.Left, FontStyles.Bold);
            comms.signalText = ui.Text("Signal", panel, 222f, 10f, inner - 200f, 20f, "NO LINK", 14f, theme.cautionAmber, TextAlignmentOptions.Right, FontStyles.Bold);
            comms.speakerText = ui.Text("Speaker", panel, 22f, 34f, 330f, 26f, "", 19f, theme.accentBlue, TextAlignmentOptions.Left, FontStyles.Bold);
            comms.stampText = ui.Text("Stamp", panel, 352f, 36f, inner - 330f, 24f, "", 16f, theme.textDim, TextAlignmentOptions.Right);
            comms.bodyText = ui.Text("Body", panel, 22f, 64f, inner, 110f, "", 18f, theme.textPrimary, TextAlignmentOptions.TopLeft, FontStyles.Normal, true);

            comms.replyRows = new TMP_Text[3];
            for (int i = 0; i < comms.replyRows.Length; i++)
                comms.replyRows[i] = ui.Text("Reply " + (i + 1), panel, 22f, 178f + i * 21f, inner, 20f, "", 17f, theme.cautionAmber, TextAlignmentOptions.Left);

            RectTransform log = Pinned("Radio Log", root, corner, corner, new Vector2(-28f, -286f), new Vector2(width, 420f));
            ui.Image(log, PanelColor, assets.RoundedPanel, true);
            ui.Text("Title", log, 22f, 10f, 400f, 20f, "RADIO LOG   newest first", 14f, theme.textDim, TextAlignmentOptions.Left, FontStyles.Bold);
            comms.logText = ui.Text("Messages", log, 22f, 38f, inner, 370f, "", 15f, theme.textPrimary, TextAlignmentOptions.TopLeft, FontStyles.Normal, true);
            comms.logPanel = log.gameObject;
        }

        // ---------------------------------------------------------------- menus

        private void BuildMenus(SpacecraftController spacecraft, GameManager game)
        {
            RectTransform root = ui.Fill("Menus", canvasRect);
            Menus = root.gameObject.AddComponent<MenuController>();
            Menus.game = game;
            Menus.spacecraft = spacecraft;

            RectTransform backdrop = ui.Fill("Backdrop", root);
            Image shade = ui.Image(backdrop, Color.black);
            shade.raycastTarget = true;
            Menus.backdrop = backdrop.gameObject.AddComponent<CanvasGroup>();
            Menus.backdrop.alpha = 1f;

            BuildMainMenu(root);
            BuildBriefing(root);
            BuildPause(root);
            BuildComplete(root);
            BuildFailed(root);
            BuildSettings(root);
            BuildCredits(root);
            BuildConfirm(root);
        }

        private void BuildSettings(RectTransform root)
        {
            RectTransform panel = Pinned("Settings", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700f, 470f));
            ui.Image(panel, PanelColor, assets.RoundedPanel, true);
            Menus.settingsPanel = panel.gameObject;
            ui.Text("Heading", panel, 0f, 40f, 700f, 50f, "SETTINGS", 38f, theme.textPrimary, TextAlignmentOptions.Center, FontStyles.Bold);
            ui.Image("Rule", panel, 150f, 100f, 400f, 2f, theme.accentBlue);
            Menus.volumeButton = MenuButton(panel, 110f, 132f, 480f, "VOLUME   100%", false);
            Menus.effectsButton = MenuButton(panel, 110f, 212f, 480f, "VISUAL EFFECTS   FULL", false);
            ui.Text("Note", panel, 90f, 286f, 520f, 60f,
                "Reduced removes flicker, haze and camera shake, and shortens the wake-up to a plain fade.",
                17f, theme.textDim, TextAlignmentOptions.Top, FontStyles.Normal, true);
            Menus.settingsBackButton = MenuButton(panel, 200f, 366f, 300f, "BACK", true);
        }

        private void BuildCredits(RectTransform root)
        {
            RectTransform panel = Pinned("Credits", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 560f));
            ui.Image(panel, PanelColor, assets.RoundedPanel, true);
            Menus.creditsPanel = panel.gameObject;
            ui.Text("Heading", panel, 0f, 40f, 900f, 50f, "CREDITS", 38f, theme.textPrimary, TextAlignmentOptions.Center, FontStyles.Bold);
            ui.Image("Rule", panel, 250f, 100f, 400f, 2f, theme.accentBlue);
            ui.Text("Text", panel, 80f, 126f, 740f, 320f,
                "<b>Earth</b>   NASA Blue Marble, Land, Ocean, Ice composite. Public domain.\n\n" +
                "<b>Jupiter</b>   Cassini cylindrical map, PIA07782. NASA / JPL / Space Science Institute. Public domain.\n\n" +
                "<b>Type</b>   Liberation Sans and TextMesh Pro, supplied with Unity.\n\n" +
                "Everything else, including every sound, is generated by this project's own code.\n\n" +
                "The spacecraft is fictional and is not affiliated with any space agency.",
                20f, theme.textPrimary, TextAlignmentOptions.TopLeft, FontStyles.Normal, true);
            Menus.creditsBackButton = MenuButton(panel, 300f, 462f, 300f, "BACK", true);
        }

        private void BuildConfirm(RectTransform root)
        {
            RectTransform panel = Pinned("New Story Confirmation", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 360f));
            ui.Image(panel, PanelColor, assets.RoundedPanel, true);
            Menus.confirmPanel = panel.gameObject;
            ui.Text("Heading", panel, 0f, 40f, 760f, 50f, "START A NEW STORY?", 36f, theme.textPrimary, TextAlignmentOptions.Center, FontStyles.Bold);
            ui.Text("Text", panel, 80f, 110f, 600f, 80f, "There is a saved story. Starting again replaces it, and it cannot be brought back.",
                21f, theme.textDim, TextAlignmentOptions.Top, FontStyles.Normal, true);
            Menus.confirmNoButton = MenuButton(panel, 70f, 240f, 290f, "KEEP MY STORY", true);
            Menus.confirmYesButton = MenuButton(panel, 400f, 240f, 290f, "START AGAIN", false);
        }

        private void BuildMainMenu(RectTransform root)
        {
            RectTransform panel = ui.Fill("Main Menu", root);
            Menus.mainMenuPanel = panel.gameObject;

            RectTransform block = Pinned("Title Block", panel, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(150f, 20f), new Vector2(1100f, 800f));
            ui.Text("Programme", block, 0f, 0f, 1100f, 32f, "PHAINON FLIGHT SYSTEMS", 22f, theme.textDim, TextAlignmentOptions.Left, FontStyles.Bold).characterSpacing = 18f;
            ui.Text("Title", block, 0f, 46f, 1100f, 110f, assets.Mission.spec.missionName, 96f, theme.textPrimary, TextAlignmentOptions.Left, FontStyles.Bold);
            ui.Text("Subtitle", block, 4f, 164f, 1100f, 44f, assets.Mission.spec.missionSubtitle, 34f, theme.accentBlue, TextAlignmentOptions.Left, FontStyles.Bold).characterSpacing = 22f;
            ui.Image("Rule", block, 4f, 226f, 520f, 3f, theme.accentBlue);
            ui.Text("Blurb", block, 4f, 246f, 760f, 96f,
                "A solitary expedition into deep space, and the launch that began it. Take the command seat.",
                24f, theme.textDim, TextAlignmentOptions.TopLeft, FontStyles.Normal, true);

            const float step = 70f;
            float y = 336f;
            Menus.continueStoryButton = MenuButton(block, 4f, y, 420f, "CONTINUE STORY", true); y += step;
            Menus.newStoryButton = MenuButton(block, 4f, y, 420f, "NEW STORY", true); y += step;
            Menus.startButton = MenuButton(block, 4f, y, 420f, "EARTH LAUNCH MISSION", false); y += step;
            Menus.settingsButton = MenuButton(block, 4f, y, 420f, "SETTINGS", false); y += step;
            Menus.creditsButton = MenuButton(block, 4f, y, 420f, "CREDITS", false); y += step;
            Menus.quitButton = MenuButton(block, 4f, y, 420f, "QUIT", false);

            RectTransform footer = Pinned("Footer", panel, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(154f, 48f), new Vector2(1200f, 26f));
            ui.Text(footer, "VERTICAL SLICE  " + Application.version + "    A fictional spacecraft. Not affiliated with any space agency.",
                16f, theme.textDim, TextAlignmentOptions.Left);
        }

        private void BuildBriefing(RectTransform root)
        {
            RectTransform panel = Pinned("Briefing", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500f, 900f));
            ui.Image(panel, PanelColor, assets.RoundedPanel, true);
            Menus.briefingPanel = panel.gameObject;

            ui.Text("Heading", panel, 50f, 34f, 900f, 50f, "MISSION BRIEFING", 40f, theme.textPrimary, TextAlignmentOptions.Left, FontStyles.Bold);
            ui.Image("Rule", panel, 50f, 92f, 1400f, 2f, theme.accentBlue);

            const string flow =
                "<color=#45A8FF><b>1  PRE-LAUNCH</b></color>\n" +
                "Switch on both batteries on the overhead panel (key 6), press MAIN POWER, then bring every row of the GO / NO-GO board on the centre display to GO. " +
                "Press LAUNCH SEQ to start the count.\n\n" +
                "<color=#45A8FF><b>2  ATMOSPHERIC ASCENT</b></color>\n" +
                "Set full throttle. At T-6 lift the guard and press ENGINE IGNITION. Ease the throttle back through max-Q " +
                "and again if the G load climbs past 4.\n\n" +
                "<color=#45A8FF><b>3  STAGE SEPARATION</b></color>\n" +
                "When the booster burns out, lift the guard and press STAGE SEP, then press ENGINE IGNITION for the upper stage.\n\n" +
                "<color=#45A8FF><b>4  ORBITAL INSERTION</b></color>\n" +
                "Hold the burn. When ORBIT GO lights, press ENGINE CUTOFF.\n\n" +
                "<color=#45A8FF><b>5  OUTER SPACE</b></color>\n" +
                "You are in orbit. Switch GUIDANCE off and RCS on to fly the spacecraft by hand.";
            ui.Text("Flow", panel, 50f, 112f, 660f, 680f, flow, 21f, theme.textPrimary, TextAlignmentOptions.TopLeft, FontStyles.Normal, true);

            ui.Image("Divider", panel, 748f, 112f, 2f, 660f, theme.panelOutline);

            const string guide =
                "<color=#45A8FF><b>COCKPIT CONTROLS</b></color>\n" +
                "<b>Look around</b>   hold the right mouse button and move the mouse\n" +
                "<b>Jump to a view</b>   1 window   2 displays   3 main panel   4 left console   5 right console   6 overhead\n" +
                "<b>Zoom</b>   hold Z or the middle mouse button\n\n" +
                "<b>Buttons</b>   point and left-click. Red-framed buttons are guarded: the first click lifts the guard, the second presses.\n" +
                "<b>Switches</b>   left-click to flip. The lamp above shows the state.\n" +
                "<b>Dials</b>   hold left-click and drag, or point and scroll the wheel.\n" +
                "<b>Throttle</b>   drag the lever on the left console, scroll over it, or use Shift and Ctrl. T is full, X is idle.\n" +
                "<b>Attitude</b>   W S pitch, A D yaw, Q E roll (guidance off, RCS on).\n\n" +
                "<color=#45A8FF><b>THE THREE DISPLAYS</b></color>\n" +
                "<b>Left</b>   flight and navigation: altitude, velocity, attitude, trajectory.\n" +
                "<b>Centre</b>   systems: engine, fuel, power, cabin, launch status and alerts.\n" +
                "<b>Right</b>   hull camera, current objective and the flight-event timeline.\n\n" +
                "The control you need next pulses amber, and the objective is shown top left. Esc pauses.";
            ui.Text("Guide", panel, 780f, 112f, 670f, 680f, guide, 20f, theme.textPrimary, TextAlignmentOptions.TopLeft, FontStyles.Normal, true);

            Menus.briefingBackButton = MenuButton(panel, 50f, 808f, 300f, "BACK", false);
            Menus.beginButton = MenuButton(panel, 1070f, 808f, 380f, "BEGIN MISSION", true);
        }

        private void BuildPause(RectTransform root)
        {
            RectTransform panel = Pinned("Pause", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 740f));
            ui.Image(panel, PanelColor, assets.RoundedPanel, true);
            Menus.pausePanel = panel.gameObject;

            ui.Text("Heading", panel, 0f, 40f, 620f, 50f, "MISSION PAUSED", 38f, theme.textPrimary, TextAlignmentOptions.Center, FontStyles.Bold);
            ui.Image("Rule", panel, 110f, 100f, 400f, 2f, theme.accentBlue);
            Menus.resumeButton = MenuButton(panel, 110f, 132f, 400f, "RESUME", true);
            Menus.pauseRestartButton = MenuButton(panel, 110f, 212f, 400f, "RESTART MISSION", false);
            Menus.pauseMenuButton = MenuButton(panel, 110f, 292f, 400f, "MAIN MENU", false);
            Menus.pauseQuitButton = MenuButton(panel, 110f, 372f, 400f, "QUIT", false);
            // Story missions only; the menu hides it otherwise.
            Menus.saveButton = MenuButton(panel, 110f, 452f, 400f, "SAVE STORY", false);
            Menus.saveStatus = ui.Text("Save Status", panel, 60f, 518f, 500f, 24f, "", 16f, theme.accentBlue, TextAlignmentOptions.Center);
            ui.Text("Reminder", panel, 60f, 562f, 500f, 150f,
                "RMB look   LMB operate   Scroll adjust\n1-6 views   Z zoom   H guide\nShift / Ctrl throttle   T full   X idle\nW S A D Q E attitude",
                17f, theme.textDim, TextAlignmentOptions.Top, FontStyles.Normal, true);
        }

        private void BuildComplete(RectTransform root)
        {
            RectTransform panel = Pinned("Mission Complete", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 640f));
            ui.Image(panel, PanelColor, assets.RoundedPanel, true);
            Menus.completePanel = panel.gameObject;

            Menus.completeHeading = ui.Text("Heading", panel, 0f, 40f, 820f, 60f, "ORBIT ACHIEVED", 52f, theme.nominalGreen, TextAlignmentOptions.Center, FontStyles.Bold);
            Menus.completeSub = ui.Text("Sub", panel, 0f, 104f, 820f, 34f, "MISSION COMPLETE   WELCOME TO OUTER SPACE", 22f, theme.textPrimary, TextAlignmentOptions.Center, FontStyles.Bold);
            ui.Image("Rule", panel, 160f, 152f, 500f, 2f, theme.nominalGreen);
            Menus.completeSummary = ui.Text("Summary", panel, 150f, 176f, 520f, 150f, "", 22f, theme.textPrimary, TextAlignmentOptions.TopLeft);
            Menus.completeNote = ui.Text("Note", panel, 90f, 322f, 640f, 70f,
                "The flight is held here. Continue it to stay in orbit, or to drop the upper stage and leave Earth.", 18f, theme.textDim, TextAlignmentOptions.Center, FontStyles.Normal, true);
            Menus.continueButton = MenuButton(panel, 210f, 396f, 400f, "CONTINUE FLIGHT", true);
            Menus.completeRestartButton = MenuButton(panel, 210f, 470f, 400f, "RESTART MISSION", false);
            Menus.completeMenuButton = MenuButton(panel, 210f, 544f, 400f, "MAIN MENU", false);
        }

        private void BuildFailed(RectTransform root)
        {
            RectTransform panel = Pinned("Mission Failed", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 500f));
            ui.Image(panel, PanelColor, assets.RoundedPanel, true);
            Menus.failedPanel = panel.gameObject;

            ui.Text("Heading", panel, 0f, 40f, 820f, 60f, "MISSION FAILED", 52f, theme.warningRed, TextAlignmentOptions.Center, FontStyles.Bold);
            ui.Image("Rule", panel, 160f, 112f, 500f, 2f, theme.warningRed);
            Menus.failedReason = ui.Text("Reason", panel, 90f, 136f, 640f, 120f, "", 23f, theme.textPrimary, TextAlignmentOptions.Top, FontStyles.Normal, true);
            Menus.failedRestartButton = MenuButton(panel, 210f, 290f, 400f, "RESTART MISSION", true);
            Menus.failedMenuButton = MenuButton(panel, 210f, 370f, 400f, "MAIN MENU", false);
        }

        private Button MenuButton(Transform parent, float x, float y, float width, string label, bool primary)
        {
            RectTransform rect = ui.Rect("Button " + label, parent, x, y, width, 62f);
            Color baseColor = primary ? new Color(0.10f, 0.34f, 0.62f, 1f) : new Color(0.10f, 0.14f, 0.18f, 1f);
            Image image = ui.Image(rect, Color.white, assets.RoundedPanel, true);
            image.raycastTarget = true;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = baseColor;
            colors.highlightedColor = Color.Lerp(baseColor, theme.accentBlue, 0.55f);
            colors.selectedColor = Color.Lerp(baseColor, theme.accentBlue, 0.35f);
            colors.pressedColor = theme.accentBlue;
            colors.disabledColor = new Color(0.1f, 0.1f, 0.1f, 0.5f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            TextMeshProUGUI text = ui.Text(ui.Fill("Label", rect), label, 24f, theme.textPrimary, TextAlignmentOptions.Center, FontStyles.Bold);
            text.characterSpacing = 6f;
            return button;
        }
    }
}
