using Phainon.Simulation;
using UnityEngine;

namespace Phainon
{
    /// <summary>Designer-editable vehicle definition. Wraps the engine-independent <see cref="VehicleSpec"/>.</summary>
    [CreateAssetMenu(menuName = "Phainon/Vehicle Config", fileName = "VehicleConfig")]
    public class VehicleConfig : ScriptableObject
    {
        public VehicleSpec spec = new VehicleSpec();
    }
}
