using Phainon.Simulation;
using UnityEngine;

namespace Phainon
{
    /// <summary>The story campaign as an asset: introduction, chapters and missions. Wraps the engine-independent <see cref="StoryCampaignSpec"/>.</summary>
    [CreateAssetMenu(menuName = "Phainon/Story Campaign", fileName = "StoryCampaign")]
    public class StoryCampaign : ScriptableObject
    {
        public StoryCampaignSpec spec = StoryCampaignContent.Create();
    }
}
