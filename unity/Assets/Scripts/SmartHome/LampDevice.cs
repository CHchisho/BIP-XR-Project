using UnityEngine;

namespace SmartHouse
{
    /// <summary>
    /// Pendant lamp. Light comes from the bulb: a soft downward spot light that casts the
    /// pool of light onto the table/floor, a small glow light that lights the inside of the
    /// shade, and an emissive bulb that blooms.
    /// </summary>
    public class LampDevice : SmartDevice
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

        public bool IsOn => isOn;
        public override string Status => isOn ? "On" : "Off";
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
            spot.enabled = on;
            glow.enabled = on;
            if (bulbMaterial != null)
                bulbMaterial.SetColor("_EmissionColor", on ? lightColor * 12f : Color.black);
            NotifyChanged();
        }
    }
}
