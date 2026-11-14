using Phainon.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Phainon.UI
{
    /// <summary>
    /// Main menu, mission briefing with the control guide, pause menu and the two end-of-mission
    /// panels. It shows the panel that matches the game state and forwards button presses to the
    /// <see cref="GameManager"/>; it holds no game logic of its own.
    /// </summary>
    public class MenuController : MonoBehaviour
    {
        [SerializeField] internal GameManager game;
        [SerializeField] internal SpacecraftController spacecraft;

        [Header("Panels")]
        [SerializeField] internal CanvasGroup backdrop;
        [SerializeField] internal GameObject mainMenuPanel;
        [SerializeField] internal GameObject briefingPanel;
        [SerializeField] internal GameObject pausePanel;
        [SerializeField] internal GameObject completePanel;
        [SerializeField] internal GameObject failedPanel;
        [SerializeField] internal float backdropOpacity = 0.72f;

        [Header("Main menu")]
        [SerializeField] internal Button startButton;
        [SerializeField] internal Button quitButton;
        [Header("Briefing")]
        [SerializeField] internal Button beginButton;
        [SerializeField] internal Button briefingBackButton;
        [Header("Pause")]
        [SerializeField] internal Button resumeButton;
        [SerializeField] internal Button pauseRestartButton;
        [SerializeField] internal Button pauseMenuButton;
        [SerializeField] internal Button pauseQuitButton;
        [Header("Mission complete")]
        [SerializeField] internal TMP_Text completeSummary;
        [SerializeField] internal Button continueButton;
        [SerializeField] internal Button completeRestartButton;
        [SerializeField] internal Button completeMenuButton;
        [Header("Mission failed")]
        [SerializeField] internal TMP_Text failedReason;
        [SerializeField] internal Button failedRestartButton;
        [SerializeField] internal Button failedMenuButton;

        private float backdropTarget;

        private void Awake()
        {
            startButton.onClick.AddListener(game.OpenBriefing);
            quitButton.onClick.AddListener(game.Quit);
            beginButton.onClick.AddListener(game.StartMission);
            briefingBackButton.onClick.AddListener(game.BackToMenu);
            resumeButton.onClick.AddListener(game.Resume);
            pauseRestartButton.onClick.AddListener(game.RestartMission);
            pauseMenuButton.onClick.AddListener(game.ReturnToMainMenu);
            pauseQuitButton.onClick.AddListener(game.Quit);
            continueButton.onClick.AddListener(game.ContinueFlight);
            completeRestartButton.onClick.AddListener(game.RestartMission);
            completeMenuButton.onClick.AddListener(game.ReturnToMainMenu);
            failedRestartButton.onClick.AddListener(game.RestartMission);
            failedMenuButton.onClick.AddListener(game.ReturnToMainMenu);

            game.StateChanged += OnStateChanged;
            ShowOnly(null);
        }

        private void Start()
        {
            // The UI input module needs a set of actions; fall back to the package defaults if none was assigned.
            EventSystem system = EventSystem.current;
            InputSystemUIInputModule module = system != null ? system.GetComponent<InputSystemUIInputModule>() : null;
            if (module != null && module.actionsAsset == null) module.AssignDefaultActions();
        }

        private void OnDestroy()
        {
            if (game != null) game.StateChanged -= OnStateChanged;
        }

        private void Update()
        {
            // Unscaled time keeps the fade working while the game is paused.
            backdrop.alpha = Mathf.MoveTowards(backdrop.alpha, backdropTarget, Time.unscaledDeltaTime * 3f);
        }

        private void OnStateChanged(GameState state)
        {
            switch (state)
            {
                case GameState.MainMenu:
                    ShowOnly(mainMenuPanel);
                    Select(startButton);
                    break;
                case GameState.Briefing:
                    ShowOnly(briefingPanel);
                    Select(beginButton);
                    break;
                case GameState.Paused:
                    ShowOnly(pausePanel);
                    Select(resumeButton);
                    break;
                case GameState.Complete:
                    completeSummary.text = BuildSummary(spacecraft.Craft);
                    ShowOnly(completePanel);
                    Select(continueButton);
                    break;
                case GameState.Failed:
                    failedReason.text = spacecraft.Craft.Mission.FailureReason;
                    ShowOnly(failedPanel);
                    Select(failedRestartButton);
                    break;
                default:
                    ShowOnly(null);
                    break;
            }

            // Menus need a free, visible cursor regardless of what the cockpit camera was doing.
            if (state != GameState.Flying)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void ShowOnly(GameObject panel)
        {
            mainMenuPanel.SetActive(panel == mainMenuPanel);
            briefingPanel.SetActive(panel == briefingPanel);
            pausePanel.SetActive(panel == pausePanel);
            completePanel.SetActive(panel == completePanel);
            failedPanel.SetActive(panel == failedPanel);

            backdropTarget = panel != null ? backdropOpacity : 0f;
            backdrop.blocksRaycasts = panel != null;
            backdrop.interactable = panel != null;
        }

        private static void Select(Button button)
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(button.gameObject);
        }

        private static string BuildSummary(Spacecraft craft)
        {
            FlightModel flight = craft.Flight;
            string apoapsis = double.IsInfinity(flight.ApoapsisMeters) ? "ESCAPE" : (flight.ApoapsisMeters / 1000.0).ToString("0") + " km";
            // Monospaced so the two columns line up in a proportional font.
            return "<mspace=0.58em>TIME TO ORBIT      " + MissionController.FormatMet(craft.Mission.MissionElapsedSeconds) + "\n"
                 + "ORBIT              " + (flight.PeriapsisMeters / 1000.0).ToString("0") + " x " + apoapsis + "\n"
                 + "VELOCITY           " + flight.Speed.ToString("0") + " m/s\n"
                 + "PROPELLANT LEFT    " + (craft.Fuel.GetFraction(craft.Engine.ActiveStage) * 100.0).ToString("0.0") + " %</mspace>";
        }
    }
}
