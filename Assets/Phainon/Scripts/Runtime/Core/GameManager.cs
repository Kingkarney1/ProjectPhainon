using System;
using Phainon.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Phainon
{
    public enum GameState { MainMenu, Briefing, Flying, Paused, Complete, Failed, Intro, Cinematic }

    /// <summary>
    /// Top-level game flow: menu, briefing, the story's introduction and wake-up, flight, pause and
    /// the two end states. It owns time scale and scene restart; everything else reacts to <see cref="StateChanged"/>.
    /// </summary>
    [DefaultExecutionOrder(-150)]
    public class GameManager : MonoBehaviour
    {
        [SerializeField] internal SpacecraftController spacecraft;
        [Tooltip("Seconds between reaching orbit and the mission-complete panel appearing.")]
        [SerializeField] internal float completeDelay = 4f;
        [SerializeField] internal float failDelay = 2.5f;

        /// <summary>Set when a restart is requested so the reloaded scene skips the main menu.</summary>
        private static bool skipMenuOnLoad;

        // The editor can enter Play mode without reloading scripts, which would carry the last session's mode into this one.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            skipMenuOnLoad = false;
            GameSession.Mode = GameMode.Standard;
            GameSession.LoadSave = false;
            GameSession.SkipSequences = false;
            StorySave.Suspended = false;
        }

        public GameState State { get; private set; }
        public event Action<GameState> StateChanged;
        /// <summary>True while a story mission is being flown.</summary>
        public bool IsStory { get { return spacecraft.Craft.Story != null; } }

        private GameState stateBeforePause;
        private float endTimer = -1f;
        private GameState pendingEndState;

        private void Start()
        {
            Time.timeScale = 1f;
            Spacecraft craft = spacecraft.Craft;
            craft.Mission.Completed += OnMissionCompleted;
            craft.Mission.Failed += OnMissionFailed;
            if (craft.Story != null) craft.Story.Completed += OnMissionCompleted;

            State = GameState.MainMenu;
            if (skipMenuOnLoad)
            {
                skipMenuOnLoad = false;
                // A new story opens with its introduction; a continued one goes straight back to the seat.
                SetState(IsStory && !GameSession.LoadSave && !GameSession.SkipSequences ? GameState.Intro : GameState.Flying);
            }
            else
            {
                SetState(GameState.MainMenu, true);
            }
        }

        private void Update()
        {
            if (spacecraft.Input.Pause.WasPressedThisFrame()) TogglePause();

            if (endTimer >= 0f && State == GameState.Flying)
            {
                endTimer -= Time.deltaTime;
                if (endTimer < 0f) SetState(pendingEndState);
            }
        }

        // ---------------------------------------------------------------- menu commands

        public void OpenBriefing() { SetState(GameState.Briefing); }
        public void BackToMenu() { SetState(GameState.MainMenu); }
        public void StartMission() { SetState(GameState.Flying); }

        /// <summary>Starts the story from the beginning. Any saved story is replaced.</summary>
        public void NewStory()
        {
            StorySave.Delete();
            Load(GameMode.Story, false, true);
        }

        public void ContinueStory()
        {
            if (StorySave.Exists) Load(GameMode.Story, true, true);
        }

        /// <summary>The introduction has finished or been skipped.</summary>
        public void IntroFinished()
        {
            if (State == GameState.Intro) SetState(GameState.Cinematic);
        }

        /// <summary>The wake-up has finished or been skipped: the astronaut has the controls.</summary>
        public void CinematicFinished()
        {
            if (State == GameState.Cinematic) SetState(GameState.Flying);
        }

        public void TogglePause()
        {
            if (State == GameState.Paused) Resume();
            else if (State == GameState.Flying || State == GameState.Complete || State == GameState.Failed) Pause();
        }

        public void Pause()
        {
            if (State == GameState.Paused) return;
            stateBeforePause = State;
            SetState(GameState.Paused);
        }

        public void Resume()
        {
            if (State == GameState.Paused) SetState(stateBeforePause);
        }

        /// <summary>Closes the mission-complete panel and hands the spacecraft back for free flight.</summary>
        public void ContinueFlight()
        {
            if (State == GameState.Complete) SetState(GameState.Flying);
        }

        public void RestartMission()
        {
            // A story mission goes back to its last save, if there is one.
            Load(GameSession.Mode, GameSession.Mode == GameMode.Story && StorySave.Exists, true);
        }

        public void ReturnToMainMenu()
        {
            Load(GameMode.Standard, false, false);
        }

        internal static void Load(GameMode mode, bool loadSave, bool skipMenu)
        {
            GameSession.Mode = mode;
            GameSession.LoadSave = loadSave;
            skipMenuOnLoad = skipMenu;
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------------------------------------------------------------- state

        private void OnMissionCompleted()
        {
            pendingEndState = GameState.Complete;
            endTimer = completeDelay;
        }

        private void OnMissionFailed(string reason)
        {
            pendingEndState = GameState.Failed;
            endTimer = failDelay;
        }

        private void SetState(GameState next, bool force = false)
        {
            if (State == next && !force) return;
            State = next;

            // The simulation only advances while the crew are actually in the seat. It is held under
            // the mission-complete panel, so nothing that needs an answer goes by behind it.
            spacecraft.Running = next == GameState.Flying || next == GameState.Cinematic || next == GameState.Failed;
            Time.timeScale = next == GameState.Paused ? 0f : 1f;
            AudioListener.pause = next == GameState.Paused;
            if (next == GameState.Complete || next == GameState.Failed) endTimer = -1f;

            if (StateChanged != null) StateChanged(next);
        }

        private void OnDestroy()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }
    }
}
