using TMPro;
using UnityEngine;

namespace SmartHouse
{
    /// <summary>
    /// Wall thermostat. Pressing it switches heating on/off; holding the trigger and moving the controller
    /// up/down (or scrolling on the desktop) changes the target temperature in 0.5° steps.
    /// The display shows the room temperature and the target; the glow renderer lights up while it is heating.
    /// </summary>
    public class ThermostatDevice : SmartDevice, IAdjustable
    {
        [SerializeField] TMP_Text currentText;
        [SerializeField] TMP_Text targetText;
        [Tooltip("Lights up in the heat colour while heating (a ring, or the screen backlight).")]
        [SerializeField] Renderer ring;
        [SerializeField] float glowStrength = 3f;
        [SerializeField] float minTemperature = 5f;
        [SerializeField] float maxTemperature = 35f;
        [SerializeField] float step = 0.5f;
        [Tooltip("Degrees per full drag range (about 30 cm of hand movement).")]
        [SerializeField] float degreesPerRange = 8f;
        [SerializeField] Color heatColor = new Color(1f, 0.45f, 0.1f);

        bool heatMode = true;
        bool heating;
        float current = 21f;
        float target = 21f;
        float pendingDrag;
        Material ringMaterial;
        AudioSource click;

        public bool HeatMode => heatMode;
        public float Target => target;

        public override string Status => heatMode
            ? $"Heat · {target:0.0}° · room {current:0.0}°"
            : $"Off · room {current:0.0}°";
        public override bool IsActive => heatMode;
        public override Color AccentColor => heatColor;

        protected override void Awake()
        {
            base.Awake();
            click = CreateAudioSource(gameObject, true);
            click.clip = ToneLibrary.Click();
            if (ring != null)
            {
                ringMaterial = ring.material;
                ringMaterial.EnableKeyword("_EMISSION");
            }
            UpdateDisplay();
        }

        public override void Interact()
        {
            click.Play();
            SetHeatMode(!heatMode);
        }

        public void Adjust(float amount)
        {
            pendingDrag += amount * degreesPerRange;
            float steps = (float)System.Math.Truncate(pendingDrag / step);
            if (steps == 0f) return;
            pendingDrag -= steps * step;
            if (!heatMode) heatMode = true; // changing the target implies you want heat
            SetTarget(target + steps * step);
        }

        public void SetHeatMode(bool on)
        {
            if (on == heatMode) return;
            heatMode = on;
            if (!on) heating = false;
            UpdateDisplay();
            NotifyChanged();
        }

        public void SetTarget(float temperature)
        {
            temperature = Mathf.Clamp(Mathf.Round(temperature / step) * step, minTemperature, maxTemperature);
            if (Mathf.Approximately(temperature, target)) return;
            target = temperature;
            UpdateDisplay();
            NotifyChanged();
        }

        /// <summary>Measured room temperature and whether it is actually heating right now (from Home Assistant).</summary>
        public void SetMeasured(float temperature, bool isHeating)
        {
            if (Mathf.Approximately(temperature, current) && isHeating == heating) return;
            current = temperature;
            heating = isHeating;
            UpdateDisplay();
        }

        void UpdateDisplay()
        {
            if (currentText != null) currentText.text = $"{current:0.0}°";
            if (targetText != null) targetText.text = heatMode ? $"set {target:0.0}°" : "off";
            if (ringMaterial != null)
            {
                bool glow = heatMode && (heating || target > current);
                ringMaterial.SetColor("_EmissionColor", glow ? heatColor * glowStrength : Color.black);
            }
        }
    }
}
