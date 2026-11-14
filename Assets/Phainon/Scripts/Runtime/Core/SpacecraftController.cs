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
        [Tooltip("Largest simulation step taken in one frame, to stay stable after a hitch.")]
        [SerializeField] internal float maxFrameStep = 0.1f;

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
            Craft = new Spacecraft(vehicle, systems, mission);
            Input = new PhainonInput();
        }

        private void OnEnable() { Input.Enable(); }
        private void OnDisable() { Input.Disable(); }
        private void OnDestroy() { Input.Dispose(); }

        private void Update()
        {
            if (!Running) return;

            Craft.SetAttitudeInput(Input.Pitch.ReadValue<float>(), Input.Yaw.ReadValue<float>(), Input.Roll.ReadValue<float>());
            Craft.Tick(Mathf.Min(Time.deltaTime, maxFrameStep));
        }
    }
}
