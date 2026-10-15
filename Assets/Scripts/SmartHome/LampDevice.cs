using UnityEngine;

namespace SmartHouse
{
    /// <summary>
    /// Pendant lamp. Light comes from the bulb: a soft downward spot light that casts the
    /// pool of light onto the table/floor, a small glow light that lights the inside of the
    /// shade, and an emissive bulb that blooms.
    /// </summary>
    public class LampDevice : SmartDevice, IDimmableDevice, IAdjustable, IPowerMeter
    {
        [SerializeField] Renderer bulb;
        [SerializeField] Color lightColor = new Color(1f, 0.8f, 0.58f);
        [SerializeField] float intensity = 18f;
        [SerializeField] float spotAngle = 115f;
        [SerializeField] float range = 7f;
        [SerializeField] bool startOn;

        Light spot;
        Light glow;
        Material bulbMaterial;
        bool isOn;
        float brightness = 1f;
        float watts = -1f;

        public bool IsOn => isOn;
        public float Brightness => brightness;
        public override string Status
        {
            get
            {
                if (!isOn) return "Off";
                string text = brightness < 0.99f ? $"On · {Mathf.RoundToInt(brightness * 100f)}%" : "On";
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
            spot.enabled = isOn;
            glow.enabled = isOn;
            // Perceived brightness isn't linear: a real bulb at 65% still looks fairly bright,
            // so the light output follows a softer curve than the percentage itself.
            float level = Mathf.Sqrt(brightness);
            spot.intensity = intensity * level;
            glow.intensity = 1.5f * level;
            if (bulbMaterial != null)
                bulbMaterial.SetColor("_EmissionColor", isOn ? lightColor * (12f * level) : Color.black);
            NotifyChanged();
        }
    }
}
