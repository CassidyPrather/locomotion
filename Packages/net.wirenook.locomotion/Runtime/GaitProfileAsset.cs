using UnityEngine;

namespace Wirenook.Locomotion
{
    /// <summary>
    /// A gait style saved as an asset, plus where the body's measurements
    /// come from. The style fields are documented, with units and ranges, on
    /// <see cref="GaitProfile"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Wirenook/Locomotion/Gait Profile", fileName = "Gait Profile")]
    public sealed class GaitProfileAsset : ScriptableObject
    {
        [Tooltip("Measure leg length, hip width, foot length and ankle height from the rig being baked. " +
                 "Turn off to use the numbers below instead, for example to bake one size for several avatars.")]
        public bool measureBody = true;

        [Tooltip("Body measurements in metres, used when Measure Body is off.")]
        public BodyProportions body = BodyProportions.Adult;

        public GaitProfile style = new GaitProfile();
    }
}
