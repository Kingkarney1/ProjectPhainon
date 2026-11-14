using Phainon.Simulation;
using UnityEngine;

namespace Phainon
{
    /// <summary>Designer-editable Ground Control script: the characters on the loop and everything they can say.</summary>
    [CreateAssetMenu(menuName = "Phainon/Ground Control Script", fileName = "GroundControlScript")]
    public class GroundControlScript : ScriptableObject
    {
        public CommsScriptSpec spec = new CommsScriptSpec();
    }
}
