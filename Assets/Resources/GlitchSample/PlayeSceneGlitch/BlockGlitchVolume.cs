using System;
using UnityEngine.Rendering;

namespace GlitchSample
{
    [Serializable]
    [VolumeComponentMenu("Glitch Sample/Block Glitch")]
    public sealed class BlockGlitchVolume : VolumeComponent
    {
        public ClampedFloatParameter intensity = new(0f, 0f, 1f);
        public ClampedFloatParameter blockDensity = new(0.82f, 0f, 1f);
        public ClampedFloatParameter barDensity = new(0.92f, 0f, 1f);
        public ClampedFloatParameter maxOffset = new(0.09f, 0f, 0.25f);
        public ClampedFloatParameter barOpacity = new(0.95f, 0f, 1f);
        public ClampedFloatParameter speed = new(10f, 0f, 30f);
    }
}
