using System;
using Phainon.Simulation;
using UnityEngine;

namespace Phainon
{
    /// <summary>
    /// Hosts the engine-independent <see cref="Spacecraft"/> simulation inside the scene.
    /// It builds the spacecraft from the config assets, advances it each frame while the
    /// mission is running and is the one object other scene components ask for spacecraft state.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class SpacecraftController : MonoBehaviour
    {
        [SerializeField] internal VehicleConfig vehicleConfig;
        [SerializeField] internal SystemsConfig systemsConfig;
        [SerializeField] internal MissionProfile missionProfile;
        [SerializeField] internal GroundControlScript commsScript;
        [SerializeField] internal StoryCampaign storyCampaign;
        [Tooltip("When on, one seeded fault happens each flight.")]
        [SerializeField] internal bool randomFaults = true;
        [Tooltip("Seed for the fault. Zero picks a new one each flight; the Console shows the one used.")]
        [SerializeField] internal int faultSeed;
        [Tooltip("Largest simulation step taken in one frame, to stay stable after a hitch.")]
        [SerializeField] internal float maxFrameStep = 0.1f;

        private static readonly int[] TimeRates = { 1, 10, 100, 1000, 10000 };
        private int rateIndex;

        /// <summary>How many times faster than real time the simulation is running.</summary>
        public int TimeRate { get { return TimeRates[rateIndex]; } }
        /// <summary>A short message whenever the rate changes or a request is refused.</summary>
        public event Action<string> TimeRateNotice;
        /// <summary>A short message whenever the story is saved, or a save is refused.</summary>
        public event Action<string> SaveNotice;

        private bool autosaveDue;

        /// <summary>The live simulation. Valid from Awake.</summary>
        public Spacecraft Craft { get; private set; }

        /// <summary>When false the simulation is frozen (menus, briefing).</summary>
        public bool Running { get; set; }

        public PhainonInput Input { get; private set; }

        private void Awake()
        {
            // Fall back to built-in defaults so the scene still runs if a config asset is missing.
            VehicleSpec vehicle = vehicleConfig != null ? vehicleConfig.spec : new VehicleSpec();
            SystemsSpec systems = systemsConfig != null ? systemsConfig.spec : new SystemsSpec();
            MissionSpec mission = missionProfile != null ? missionProfile.spec : new MissionSpec();
            if (GameSession.Mode == GameMode.Story && storyCampaign != null)
            {
                StorySaveData save = GameSession.LoadSave ? StorySave.Load() : null;
                StoryCampaignSpec campaign = storyCampaign.spec;
                StoryMissionSpec story = campaign.FindMission(save != null ? save.missionId : campaign.FirstMissionId)
                                         ?? campaign.FindMission(campaign.FirstMissionId);
                Craft = new Spacecraft(vehicle, systems, mission, null, 0, story);
                if (save != null) Craft.Restore(save.craft);
                // The story is kept up to date at each step, as soon as nothing is burning or recording.
                Craft.Story.ObjectiveCompleted += objective => autosaveDue = true;
                Craft.Story.Completed += () => autosaveDue = true;
            }
            else
            {
                int seed = !randomFaults ? 0 : (faultSeed != 0 ? faultSeed : (System.Environment.TickCount | 1));
                Craft = new Spacecraft(vehicle, systems, mission, commsScript != null ? commsScript.spec : null, seed);
                if (seed != 0) Debug.Log("[Phainon] Fault seed " + seed + " (set it on Spacecraft Controller to repeat this flight)");
            }
            Input = new PhainonInput();

            // Anything that needs the crew's attention brings time back to normal.
            Craft.Warnings.Raised += entry => DropToRealTime(entry.Message);
            Craft.Faults.Raised += id => DropToRealTime("FAULT");
            Craft.Mission.Failed += reason => DropToRealTime("MISSION FAILED");
            Craft.GroundControl.MessageReceived += message =>
            {
                if (message.Source != null && message.Source.replies != null && message.Source.replies.Length > 0)
                    DropToRealTime("GROUND CONTROL IS CALLING");
            };
        }

        private void OnEnable() { Input.Enable(); }
        private void OnDisable() { Input.Disable(); }
        private void OnDestroy() { Input.Dispose(); }

        private void Update()
        {
            if (!Running) return;

            if (Input.TimeFaster.WasPressedThisFrame()) StepTimeRate(1);
            if (Input.TimeSlower.WasPressedThisFrame()) StepTimeRate(-1);

            // The hand controller turns the vehicle at the same rate per second of the crew's time, whatever the time rate.
            float hand = 1f / TimeRate;
            Craft.SetAttitudeInput(Input.Pitch.ReadValue<float>() * hand, Input.Yaw.ReadValue<float>() * hand, Input.Roll.ReadValue<float>() * hand);

            // Accelerated time is still simulated in slices of at most one second, so no timer, radio
            // call or flight step is skipped over. Unity's own clock is never changed.
            double remaining = Mathf.Min(Time.deltaTime, maxFrameStep) * (double)TimeRate;
            while (remaining > 1e-9)
            {
                double step = Math.Min(remaining, 1.0);
                Craft.Tick(step);
                remaining -= step;
                if (rateIndex == 0) continue;

                string inhibit = Craft.TimeWarpInhibit;
                if (inhibit != null) DropToRealTime(inhibit);
                while (rateIndex > 0 && TimeRate > Craft.MaxTimeWarp) rateIndex--;
                if (rateIndex == 0) break;      // the rest of this frame's accelerated time is not owed
            }

            if (autosaveDue && Craft.SaveInhibit == null)
            {
                autosaveDue = false;
                SaveStory();
            }
        }

        /// <summary>Saves the story now. Returns false, with a notice saying why, if this is not a moment it can be saved.</summary>
        public bool SaveStory()
        {
            if (Craft.Story == null) return false;
            string refused = Craft.SaveInhibit;
            string failed = refused == null ? StorySave.Save(Craft, Craft.Story.Spec.id) : null;
            string message = refused != null ? "NOT SAVED - " + refused : (failed != null ? "SAVE FAILED" : "STORY SAVED");
            if (SaveNotice != null) SaveNotice(message);
            return refused == null && failed == null;
        }

        private void StepTimeRate(int direction)
        {
            int next = Mathf.Clamp(rateIndex + direction, 0, TimeRates.Length - 1);
            if (next == rateIndex) return;
            if (next > rateIndex)
            {
                string inhibit = Craft.TimeWarpInhibit;
                if (inhibit != null) { Notify("TIME ACCELERATION REFUSED - " + inhibit); return; }
                if (TimeRates[next] > Craft.MaxTimeWarp) { Notify("TIME x" + TimeRates[next] + " NEEDS MORE DISTANCE FROM EARTH"); return; }
            }
            rateIndex = next;
            Notify("TIME x" + TimeRate);
        }

        private void DropToRealTime(string reason)
        {
            if (rateIndex == 0) return;
            rateIndex = 0;
            Notify("TIME x1 - " + reason);
        }

        private void Notify(string message)
        {
            if (TimeRateNotice != null) TimeRateNotice(message);
        }
    }
}
