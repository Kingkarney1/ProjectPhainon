using System;
using Phainon.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Phainon
{
    public enum GameState { MainMenu, Briefing, Flying, Paused, Complete, Failed }

    /// <summary>
    /// Top-level game flow: menu, briefing, flight, pause and the two end states.
    /// It owns time scale and scene restart; everything else reacts to <see cref="StateChanged"/>.
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

        public GameState State { get; private set; }
        public event Action<GameState> StateChanged;

        private GameState stateBeforePause;
        private float endTimer = -1f;
        private GameState pendingEndState;

        private void Start()
        {
            Time.timeScale = 1f;
            spacecraft.Craft.Mission.Completed += OnMissionCompleted;
            spacecraft.Craft.Mission.Failed += OnMissionFailed;

            State = GameState.MainMenu;
            if (skipMenuOnLoad)
            {
                skipMenuOnLoad = false;
                SetState(GameState.Flying);
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
            skipMenuOnLoad = true;
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public void ReturnToMainMenu()
        {
            skipMenuOnLoad = false;
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

            // The simulation only advances while the crew are actually in the seat.
            spacecraft.Running = next == GameState.Flying || next == GameState.Complete || next == GameState.Failed;
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
