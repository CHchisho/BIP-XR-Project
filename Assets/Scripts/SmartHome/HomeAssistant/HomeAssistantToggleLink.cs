using System.Collections;
using System.Globalization;
using UnityEngine;

namespace SmartHouse.HomeAssistant
{
    /// <summary>
    /// Mirrors a virtual on/off device (lamp, plug) with a real Home Assistant light or switch, both ways:
    /// toggling it in VR switches the real device, and switching the real device (wall switch, HA app)
    /// updates it in VR on the next poll. For dimmable lamps the brightness is mirrored both ways
    /// (dimming in VR sends brightness_pct once the drag settles), and an optional power sensor is shown.
    /// </summary>
    [RequireComponent(typeof(SmartDevice))]
    public class HomeAssistantToggleLink : MonoBehaviour
    {
        [Tooltip("light.* or switch.* entity, e.g. light.switch")]
        [SerializeField] string entityId = "light.switch";
        [Tooltip("Optional sensor.* entity with the power draw in W, e.g. sensor.aqara_lumi_plug_aeu001_power")]
        [SerializeField] string powerSensorId = "";
        [Tooltip("Seconds between state reads. 2-4 s is enough.")]
        [SerializeField] float pollInterval = 3f;
        [Tooltip("Seconds to ignore polls after sending a command; some devices report their new state slowly.")]
        [SerializeField] float commandSettleTime = 4f;
        [Tooltip("Brightness is sent once dimming has paused this long, so a drag doesn't flood Home Assistant.")]
        [SerializeField] float brightnessDebounce = 0.35f;

        SmartDevice smartDevice;
        IToggleDevice device;
        bool applyingRemote;
        bool available;
        float ignorePollUntil;

        public bool Available => available;
        string Domain => entityId.Split('.')[0];

        void Awake()
        {
            smartDevice = GetComponent<SmartDevice>();
            device = smartDevice as IToggleDevice;
            if (device == null) Debug.LogError($"[HomeAssistant] {name}: {smartDevice.GetType().Name} is not an on/off device.", this);
        }

        void OnEnable()
        {
            if (device == null) return;
            smartDevice.StateChanged += OnDeviceChanged;
            StartCoroutine(PollLoop());
        }

        void OnDisable()
        {
            if (device == null) return;
            smartDevice.StateChanged -= OnDeviceChanged;
            StopAllCoroutines();
        }

        bool lastSentOn;
        float lastSentBrightness = -1f;
        float sendBrightnessAt = -1f;

        IDimmableDevice Dimmable => device as IDimmableDevice;

        void OnDeviceChanged(SmartDevice _)
        {
            if (applyingRemote) return; // this change came from Home Assistant; don't echo it back

            if (device.IsOn == lastSentOn)
            {
                // Only the brightness (or the power reading) changed: send brightness after the drag settles.
                if (device.IsOn && Dimmable != null && Mathf.Abs(Dimmable.Brightness - lastSentBrightness) > 0.005f)
                {
                    sendBrightnessAt = Time.unscaledTime + brightnessDebounce;
                    ignorePollUntil = sendBrightnessAt + commandSettleTime;
                }
                return;
            }
            lastSentOn = device.IsOn;
            sendBrightnessAt = -1f;
            Send();
        }

        void Update()
        {
            if (sendBrightnessAt < 0f || Time.unscaledTime < sendBrightnessAt) return;
            sendBrightnessAt = -1f;
            if (device.IsOn) Send();
        }

        void Send()
        {
            var client = HomeAssistantClient.Instance;
            if (!client.HasToken) return;
            if (!available)
            {
                Debug.LogWarning($"[HomeAssistant] {entityId} is unavailable or not reached yet; command not sent.");
                return;
            }

            // A poll that was already in flight could still report the old state; ignore polls briefly.
            ignorePollUntil = Time.unscaledTime + commandSettleTime;
            string extra = null;
            if (device.IsOn && Dimmable != null && Domain == "light")
            {
                lastSentBrightness = Dimmable.Brightness;
                extra = "\"brightness_pct\":" + Mathf.Clamp(Mathf.RoundToInt(lastSentBrightness * 100f), 1, 100);
            }
            StartCoroutine(client.CallService(Domain, device.IsOn ? "turn_on" : "turn_off", entityId, ok =>
            {
                if (!ok) ignorePollUntil = 0f; // let the next poll restore the real state
            }, extra));
        }

        IEnumerator PollLoop()
        {
            lastSentOn = device.IsOn;
            var wait = new WaitForSecondsRealtime(pollInterval);
            while (true)
            {
                var client = HomeAssistantClient.Instance;
                if (client.HasToken && Time.unscaledTime >= ignorePollUntil)
                {
                    yield return client.GetState(entityId, Apply);
                    if (!string.IsNullOrEmpty(powerSensorId))
                        yield return client.GetState(powerSensorId, ApplyPower);
                }
                yield return wait;
            }
        }

        void Apply(HomeAssistantClient.EntityState s)
        {
            if (s == null) { available = false; return; }

            available = s.state != "unavailable" && s.state != "unknown";
            if (!available || Time.unscaledTime < ignorePollUntil) return;

            bool on = s.state == "on";
            applyingRemote = true;
            try
            {
                // brightness is 0..255 and only present while a dimmable light is on.
                if (on && Dimmable != null && s.attributes != null && s.attributes.brightness > 0)
                {
                    Dimmable.SetBrightness(s.attributes.brightness / 255f);
                    lastSentBrightness = Dimmable.Brightness;
                }
                if (on != device.IsOn) device.SetOn(on);
            }
            finally { applyingRemote = false; }
            lastSentOn = device.IsOn;
        }

        void ApplyPower(HomeAssistantClient.EntityState s)
        {
            if (s == null || !(device is IPowerMeter meter)) return;
            if (float.TryParse(s.state, NumberStyles.Float, CultureInfo.InvariantCulture, out float watts))
                meter.SetPower(watts);
        }
    }
}
