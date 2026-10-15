using UnityEngine;

namespace SmartHouse
{
    /// <summary>
    /// Pendant lamp. Light comes from the bulb: a soft downward spot light that casts the
    /// pool of light onto the table/floor, a small glow light that lights the inside of the
    /// shade, and an emissive bulb that blooms.
    /// With <see cref="colorControl"/> on (an RGBW controller) it can also be tinted: grab it and twist,
    /// or shift-scroll on the desktop, to go round the color wheel; plain white is part of the wheel.
    /// </summary>
    public class LampDevice : SmartDevice, IColorDevice, IAdjustable, IPowerMeter
    {
        [SerializeField] Renderer bulb;
        [SerializeField] Color lightColor = new Color(1f, 0.8f, 0.58f);
        [SerializeField] float intensity = 18f;
        [SerializeField] float spotAngle = 115f;
        [SerializeField] float range = 7f;
        [SerializeField] bool startOn;
        [Tooltip("The lamp is an RGB(W) light whose color can be changed.")]
        [SerializeField] bool colorControl;

        Light spot;
        Light glow;
        Material bulbMaterial;
        bool isOn;
        float brightness = 1f;
        float watts = -1f;
        // Position on the color wheel, 0..1: the white band around 0, then the hues red .. magenta.
        float wheel;
        Color color = Color.white;

        public bool IsOn => isOn;
        public float Brightness => brightness;
        public bool SupportsColor => colorControl;
        public bool IsWhite => !colorControl || WhiteAmount(wheel) >= 1f;
        public Color Color => color;
        public override Color AccentColor => IsWhite ? base.AccentColor : color;
        public override string Status
        {
            get
            {
                if (!isOn) return "Off";
                string text = brightness < 0.99f ? $"On · {Mathf.RoundToInt(brightness * 100f)}%" : "On";
                if (!IsWhite) text += " · " + ColorName(color);
                return watts >= 0f ? $"{text} · {watts:0.#} W" : text;
            }
        }
        public override bool IsActive => isOn;

        protected override void Awake()
        {
            base.Awake();

            Vector3 source = bulb != null ? bulb.bounds.center : transform.position;

            spot = new GameObject("Lamp Spot").AddComponent<Light>();
            spot.transform.SetParent(transform, false);
            spot.transform.position = source;
            spot.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            spot.type = LightType.Spot;
            spot.spotAngle = spotAngle;
            spot.innerSpotAngle = spotAngle * 0.35f;
            spot.range = range;
            spot.color = lightColor;
            spot.intensity = intensity;
            spot.shadows = LightShadows.Soft;
            spot.shadowNearPlane = 0.05f;

            glow = new GameObject("Lamp Glow").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.transform.position = source + Vector3.down * 0.02f;
            glow.type = LightType.Point;
            glow.range = 0.6f;
            glow.color = lightColor;
            glow.intensity = 1.5f;
            glow.shadows = LightShadows.None;

            if (bulb != null)
            {
                bulbMaterial = bulb.material;
                bulbMaterial.EnableKeyword("_EMISSION");
                bulb.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            SetOn(startOn);
        }

        public override void Interact() => SetOn(!isOn);

        public void SetOn(bool on)
        {
            isOn = on;
            Apply();
        }

        /// <summary>Drag up/down to dim; dragging up from off switches the lamp on at its lowest level.</summary>
        public void Adjust(float amount)
        {
            if (!isOn)
            {
                if (amount <= 0f) return;
                brightness = MinBrightness;
                isOn = true;
            }
            brightness = Mathf.Clamp(brightness + amount, MinBrightness, 1f);
            Apply();
        }

        // The white band covers WhiteBand of the wheel and blends into red / magenta over BlendBand on each side.
        const float WhiteBand = 0.1f;
        const float BlendBand = 0.05f;

        public void AdjustColor(float amount)
        {
            if (!colorControl) return;
            if (!isOn) { isOn = true; brightness = Mathf.Max(brightness, 0.5f); }
            wheel = Mathf.Repeat(wheel + amount, 1f);
            color = WheelColor(wheel);
            Apply();
        }

        public void SetWhite()
        {
            if (!colorControl || IsWhite) return;
            wheel = 0f;
            color = Color.white;
            Apply();
        }

        public void SetColor(Color rgb)
        {
            if (!colorControl) return;
            Color.RGBToHSV(rgb, out float h, out float s, out _);
            if (s < 0.15f) { SetWhite(); return; }
            Color target = Color.HSVToRGB(h, 1f, 1f);
            if (!IsWhite && ColorDistance(target, color) < 0.02f) return;
            // Place it on the hue part of the wheel, past the blend with white.
            wheel = WhiteBand / 2f + BlendBand + h * (1f - WhiteBand - 2f * BlendBand);
            color = target;
            Apply();
        }

        static float WhiteAmount(float w)
        {
            float distance = Mathf.Min(w, 1f - w); // distance to the middle of the white band
            return Mathf.Clamp01(1f - (distance - WhiteBand / 2f) / BlendBand);
        }

        static Color WheelColor(float w)
        {
            float start = WhiteBand / 2f + BlendBand;
            float hue = Mathf.Clamp01((w - start) / (1f - 2f * start));
            return Color.Lerp(Color.HSVToRGB(hue, 1f, 1f), Color.white, WhiteAmount(w));
        }

        static float ColorDistance(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);

        static string ColorName(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out _);
            if (s < 0.35f) return "White";
            string[] names = { "Red", "Orange", "Yellow", "Lime", "Green", "Mint", "Cyan", "Sky", "Blue", "Purple", "Magenta", "Pink" };
            return names[Mathf.RoundToInt(h * names.Length) % names.Length];
        }

        public void SetPower(float newWatts)
        {
            if (Mathf.Abs(newWatts - watts) < 0.05f) return;
            watts = newWatts;
            if (isOn) NotifyChanged();
        }

        const float MinBrightness = 0.02f;

        public void SetBrightness(float brightness01)
        {
            brightness01 = Mathf.Clamp(brightness01, MinBrightness, 1f);
            if (Mathf.Abs(brightness01 - brightness) < 0.005f) return;
            brightness = brightness01;
            Apply();
        }

        void Apply()
        {
            // The white of the lamp tinted towards the chosen color (on the white band it stays its own warm white).
            Color tint = colorControl ? Color.Lerp(color, lightColor, WhiteAmount(wheel)) : lightColor;
            spot.color = tint;
            glow.color = tint;
            spot.enabled = isOn;
            glow.enabled = isOn;
            // Perceived brightness isn't linear: a real bulb at 65% still looks fairly bright,
            // so the light output follows a softer curve than the percentage itself.
            float level = Mathf.Sqrt(brightness);
            spot.intensity = intensity * level;
            glow.intensity = 1.5f * level;
            if (bulbMaterial != null)
                bulbMaterial.SetColor("_EmissionColor", isOn ? tint * (12f * level) : Color.black);
            NotifyChanged();
        }
    }
}
