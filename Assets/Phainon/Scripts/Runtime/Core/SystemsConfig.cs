using Phainon.Simulation;
using UnityEngine;

namespace Phainon
{
    /// <summary>Designer-editable housekeeping systems and warning limits.</summary>
    [CreateAssetMenu(menuName = "Phainon/Systems Config", fileName = "SystemsConfig")]
    public class SystemsConfig : ScriptableObject
    {
        public SystemsSpec spec = new SystemsSpec();
    }
}
