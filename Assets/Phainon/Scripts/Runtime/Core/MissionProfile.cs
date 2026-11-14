using Phainon.Simulation;
using UnityEngine;

namespace Phainon
{
    /// <summary>Designer-editable mission definition: timings, thresholds and phase text.</summary>
    [CreateAssetMenu(menuName = "Phainon/Mission Profile", fileName = "MissionProfile")]
    public class MissionProfile : ScriptableObject
    {
        public MissionSpec spec = new MissionSpec();
    }
}
