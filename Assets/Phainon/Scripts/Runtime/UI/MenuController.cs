using Phainon.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Phainon.UI
{
    /// <summary>
    /// Main menu, settings, credits, mission briefing with the control guide, pause menu and the
    /// two end-of-mission panels. It shows the panel that matches the game state and forwards
    /// button presses to the <see cref="GameManager"/>; it holds no game logic of its own.
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
        [SerializeField] internal GameObject settingsPanel;
        [SerializeField] internal GameObject creditsPanel;
        [SerializeField] internal GameObject confirmPanel;
        [SerializeField] internal float backdropOpacity = 0.72f;

        [Header("Main menu")]
        [SerializeField] internal Button continueStoryButton;
        [SerializeField] internal Button newStoryButton;
        [SerializeField] internal Button startButton;
        [SerializeField] internal Button settingsButton;
        [SerializeField] internal Button creditsButton;
        [SerializeField] internal Button quitButton;
        [Header("New story confirmation")]
        [SerializeField] internal Button confirmYesButton;
        [SerializeField] internal Button confirmNoButton;
        [Header("Settings")]
        [SerializeField] internal Button volumeButton;
        [SerializeField] internal Button effectsButton;
        [SerializeField] internal Button settingsBackButton;
        [Header("Credits")]
        [SerializeField] internal Button creditsBackButton;
        [Header("Briefing")]
        [SerializeField] internal Button beginButton;
        [SerializeField] internal Button briefingBackButton;
        [Header("Pause")]
        [SerializeField] internal Button resumeButton;
        [SerializeField] internal Button saveButton;
        [SerializeField] internal TMP_Text saveStatus;
        [SerializeField] internal Button pauseRestartButton;
        [SerializeField] internal Button pauseMenuButton;
        [SerializeField] internal Button pauseQuitButton;
        [Header("Mission complete")]
        [SerializeField] internal TMP_Text completeHeading;
        [SerializeField] internal TMP_Text completeSub;
        [SerializeField] internal TMP_Text completeSummary;
        [SerializeField] internal TMP_Text completeNote;
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
            continueStoryButton.onClick.AddListener(game.ContinueStory);
            // Starting again replaces the saved story, so that is asked about first.
            newStoryButton.onClick.AddListener(() => { if (StorySave.Exists) Show(confirmPanel, confirmNoButton); else game.NewStory(); });
            confirmYesButton.onClick.AddListener(game.NewStory);
            confirmNoButton.onClick.AddListener(ShowMainMenu);
            startButton.onClick.AddListener(game.OpenBriefing);
            settingsButton.onClick.AddListener(() => { RefreshSettings(); Show(settingsPanel, settingsBackButton); });
            creditsButton.onClick.AddListener(() => Show(creditsPanel, creditsBackButton));
            quitButton.onClick.AddListener(game.Quit);

            volumeButton.onClick.AddListener(() =>
            {
                // Steps down a fifth at a time and wraps back to full.
                float next = GameSettings.MasterVolume - 0.2f;
                GameSettings.MasterVolume = next < -0.01f ? 1f : Mathf.Max(0f, next);
                AudioListener.volume = GameSettings.MasterVolume;
                RefreshSettings();
            });
            effectsButton.onClick.AddListener(() => { GameSettings.ReducedEffects = !GameSettings.ReducedEffects; RefreshSettings(); });
            settingsBackButton.onClick.AddListener(ShowMainMenu);
            creditsBackButton.onClick.AddListener(ShowMainMenu);

            beginButton.onClick.AddListener(game.StartMission);
            briefingBackButton.onClick.AddListener(game.BackToMenu);
            resumeButton.onClick.AddListener(game.Resume);
            saveButton.onClick.AddListener(() => saveStatus.text = spacecraft.SaveStory() ? "Story saved." : "Not saved: " + (spacecraft.Craft.SaveInhibit ?? "the file could not be written").ToLowerInvariant() + ".");
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
            AudioListener.volume = GameSettings.MasterVolume;
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
                    ShowMainMenu();
                    break;
                case GameState.Briefing:
                    Show(briefingPanel, beginButton);
                    break;
                case GameState.Paused:
                    saveButton.gameObject.SetActive(game.IsStory);
                    saveStatus.text = "";
                    Show(pausePanel, resumeButton);
                    break;
                case GameState.Complete:
                    FillComplete(spacecraft.Craft);
                    Show(completePanel, continueButton);
                    break;
                case GameState.Failed:
                    failedReason.text = spacecraft.Craft.Mission.FailureReason;
                    Show(failedPanel, failedRestartButton);
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

        private void ShowMainMenu()
        {
            bool saved = StorySave.Exists;
            continueStoryButton.interactable = saved;
            Show(mainMenuPanel, saved ? continueStoryButton : newStoryButton);
        }

        private void RefreshSettings()
        {
            volumeButton.GetComponentInChildren<TMP_Text>().text = "VOLUME   " + Mathf.RoundToInt(GameSettings.MasterVolume * 100f) + "%";
            effectsButton.GetComponentInChildren<TMP_Text>().text = "VISUAL EFFECTS   " + (GameSettings.ReducedEffects ? "REDUCED" : "FULL");
        }

        private void Show(GameObject panel, Button selected)
        {
            ShowOnly(panel);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(selected.gameObject);
        }

        private void ShowOnly(GameObject panel)
        {
            mainMenuPanel.SetActive(panel == mainMenuPanel);
            briefingPanel.SetActive(panel == briefingPanel);
            pausePanel.SetActive(panel == pausePanel);
            completePanel.SetActive(panel == completePanel);
            failedPanel.SetActive(panel == failedPanel);
            settingsPanel.SetActive(panel == settingsPanel);
            creditsPanel.SetActive(panel == creditsPanel);
            confirmPanel.SetActive(panel == confirmPanel);

            backdropTarget = panel != null ? backdropOpacity : 0f;
            backdrop.blocksRaycasts = panel != null;
            backdrop.interactable = panel != null;
        }

        private void FillComplete(Spacecraft craft)
        {
            FlightModel flight = craft.Flight;
            string apoapsis = double.IsInfinity(flight.ApoapsisMeters) ? "ESCAPE" : (flight.ApoapsisMeters / 1000.0).ToString("N0") + " km";
            string orbit = (flight.PeriapsisMeters / 1000.0).ToString("N0") + " x " + apoapsis;
            string propellant = (craft.Fuel.GetFraction(craft.Engine.ActiveStage) * 100.0).ToString("0.0") + " %";

            // Monospaced so the two columns line up in a proportional font.
            if (craft.Story != null)
            {
                completeHeading.text = "DATA SENT";
                completeSub.text = craft.Story.Spec.title;
                completeNote.text = craft.Story.Spec.completionText;
                double quality = 0.0;
                foreach (ObservationRecord record in craft.Science.Records) quality += record.quality;
                completeSummary.text = "<mspace=0.58em>ORBIT              " + orbit + "\n"
                    + "OBSERVATIONS       " + craft.Science.CollectedCount + "   QUALITY " + (quality / Mathf.Max(1, craft.Science.CollectedCount)).ToString("0") + "\n"
                    + "PROPELLANT LEFT    " + propellant + "\n"
                    + "OXYGEN LEFT        " + craft.LifeSupport.OxygenDaysRemaining.ToString("0") + " days</mspace>";
                return;
            }

            completeSummary.text = "<mspace=0.58em>TIME TO ORBIT      " + MissionController.FormatMet(craft.Mission.OrbitAchievedMetSeconds) + "\n"
                 + "ORBIT              " + orbit + "\n"
                 + "VELOCITY           " + flight.Speed.ToString("0") + " m/s\n"
                 + "PROPELLANT LEFT    " + propellant + "</mspace>";
        }
    }
}
