using System.Collections.Generic;
using UnityEngine;

namespace SmartHouse
{
    /// <summary>
    /// Smart plug. Pressing it toggles the socket; its indicator LED (or LED ring) glows while it is on.
    /// Shows the measured power draw when a Home Assistant power sensor is linked.
    /// </summary>
    public class PlugDevice : SmartDevice, IToggleDevice, IPowerMeter
    {
        [Tooltip("Renderer whose indicator materials glow while the plug is on.")]
        [SerializeField] Renderer body;
        [Tooltip("Materials on the body whose name starts with one of these light up (e.g. LED_Blue, Rainbow_).")]
        [SerializeField] string[] indicatorMaterialPrefixes = { "LED_" };
        [SerializeField] float glow = 4f;
        [SerializeField] bool startOn;

        readonly List<(Material material, Color color)> indicators = new List<(Material, Color)>();
        AudioSource click;
        bool isOn;
        float watts = -1f;

        public bool IsOn => isOn;
        public override bool IsActive => isOn;
        public override Color AccentColor => new Color(0.35f, 0.75f, 1f);
        public override string Status
        {
            get
            {
                if (!isOn) return "Off";
                return watts >= 0f ? $"On · {watts:0.#} W" : "On";
            }
        }

        protected override void Awake()
        {
            base.Awake();
            click = CreateAudioSource(gameObject, true);
            click.clip = ToneLibrary.Click();

            if (body != null)
            {
                foreach (var material in body.materials) // instances, so each plug glows on its own
                {
                    foreach (var prefix in indicatorMaterialPrefixes)
                    {
                        if (!material.name.StartsWith(prefix)) continue;
                        material.EnableKeyword("_EMISSION");
                        indicators.Add((material, material.GetColor("_BaseColor")));
                        break;
                    }
                }
            }
            isOn = startOn;
            ApplyIndicators();
        }

        public override void Interact()
        {
            click.Play();
            SetOn(!isOn);
        }

        public void SetOn(bool on)
        {
            if (on == isOn) return;
            isOn = on;
            ApplyIndicators();
            NotifyChanged();
        }

        public void SetPower(float newWatts)
        {
            if (Mathf.Abs(newWatts - watts) < 0.05f) return;
            watts = newWatts;
            if (isOn) NotifyChanged();
        }

        void ApplyIndicators()
        {
            foreach (var (material, color) in indicators)
                material.SetColor("_EmissionColor", isOn ? color * glow : Color.black);
        }
    }
}
