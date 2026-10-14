using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SmartHouse
{
    /// <summary>
    /// Colours for world-space UI. The global volume's post-exposure brightens UI as well, which made
    /// white text bloom and smear; UI colours are scaled down by the same amount so they end up as intended.
    /// </summary>
    public static class UiTheme
    {
        public static readonly Color Panel = new Color(0.05f, 0.06f, 0.08f, 0.94f);
        public static readonly Color Text = Color.white;
        public static readonly Color TextDim = new Color(0.78f, 0.82f, 0.88f);
        public static readonly Color InactiveDot = new Color(0.5f, 0.53f, 0.58f);

        static float gain = -1f;

        static float Gain
        {
            get
            {
                if (gain < 0f)
                {
                    gain = 1f;
                    var volume = Object.FindAnyObjectByType<Volume>();
                    if (volume != null && volume.sharedProfile != null &&
                        volume.sharedProfile.TryGet(out ColorAdjustments adjustments) && adjustments.postExposure.overrideState)
                        gain = Mathf.Pow(2f, -adjustments.postExposure.value);
                }
                return gain;
            }
        }

        /// <summary>Colour to put on a UI element so it is displayed as <paramref name="c"/> after post-exposure.</summary>
        public static Color Ui(Color c) => new Color(c.r * Gain, c.g * Gain, c.b * Gain, c.a);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() => gain = -1f;
    }
}
