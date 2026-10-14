using System.Collections;
using System.Globalization;
using UnityEngine;

namespace SmartHouse.HomeAssistant
{
    /// <summary>
    /// Mirrors a virtual ThermostatDevice with a Home Assistant climate entity, both ways:
    /// mode (heat / off) and target temperature changed in VR are sent with set_hvac_mode /
    /// set_temperature, and the real mode, target and room temperature are shown in VR on the next poll.
    /// </summary>
    [RequireComponent(typeof(ThermostatDevice))]
    public class HomeAssistantClimateLink : MonoBehaviour
    {
        [Tooltip("climate.* entity, e.g. climate.thermostat")]
        [SerializeField] string entityId = "climate.thermostat";
        [Tooltip("Seconds between state reads. 2-4 s is enough.")]
        [SerializeField] float pollInterval = 3f;
        [Tooltip("Seconds to ignore polls after sending a command.")]
        [SerializeField] float commandSettleTime = 4f;
        [Tooltip("The target is sent once dragging has paused this long, so a drag doesn't flood Home Assistant.")]
        [SerializeField] float targetDebounce = 0.6f;

        ThermostatDevice thermostat;
        bool available;
        bool applyingRemote;
        bool knownHeat;
        float knownTarget;
        float ignorePollUntil;
        float sendTargetAt = -1f;

        public bool Available => available;

        void Awake() => thermostat = GetComponent<ThermostatDevice>();

        void OnEnable()
        {
            knownHeat = thermostat.HeatMode;
            knownTarget = thermostat.Target;
            thermostat.StateChanged += OnThermostatChanged;
            StartCoroutine(PollLoop());
        }

        void OnDisable()
        {
            thermostat.StateChanged -= OnThermostatChanged;
            StopAllCoroutines();
        }

        void OnThermostatChanged(SmartDevice _)
        {
            if (applyingRemote) return; // this change came from Home Assistant; don't echo it back

            if (thermostat.HeatMode != knownHeat)
            {
                knownHeat = thermostat.HeatMode;
                Call("set_hvac_mode", "\"hvac_mode\":\"" + (knownHeat ? "heat" : "off") + "\"");
            }
            if (!Mathf.Approximately(thermostat.Target, knownTarget))
            {
                sendTargetAt = Time.unscaledTime + targetDebounce;
                ignorePollUntil = sendTargetAt + commandSettleTime;
            }
        }

        void Update()
        {
            if (sendTargetAt < 0f || Time.unscaledTime < sendTargetAt) return;
            sendTargetAt = -1f;
            knownTarget = thermostat.Target;
            Call("set_temperature", "\"temperature\":" + knownTarget.ToString("0.0", CultureInfo.InvariantCulture));
        }

        void Call(string service, string extraJson)
        {
            var client = HomeAssistantClient.Instance;
            if (!client.HasToken) return;
            if (!available)
            {
                Debug.LogWarning($"[HomeAssistant] {entityId} is unavailable or not reached yet; command not sent.");
                return;
            }
            ignorePollUntil = Mathf.Max(ignorePollUntil, Time.unscaledTime + commandSettleTime);
            StartCoroutine(client.CallService("climate", service, entityId, ok =>
            {
                if (!ok) ignorePollUntil = 0f; // let the next poll restore the real state
            }, extraJson));
        }

        IEnumerator PollLoop()
        {
            var wait = new WaitForSecondsRealtime(pollInterval);
            while (true)
            {
                var client = HomeAssistantClient.Instance;
                if (client.HasToken && Time.unscaledTime >= ignorePollUntil && sendTargetAt < 0f)
                    yield return client.GetState(entityId, Apply);
                yield return wait;
            }
        }

        void Apply(HomeAssistantClient.EntityState s)
        {
            if (s == null) { available = false; return; }

            available = s.state != "unavailable" && s.state != "unknown";
            if (!available || Time.unscaledTime < ignorePollUntil || sendTargetAt >= 0f) return;

            var a = s.attributes;
            applyingRemote = true;
            try
            {
                if (a != null && !float.IsNaN(a.current_temperature))
                    thermostat.SetMeasured(a.current_temperature, a.hvac_action == "heating");
                thermostat.SetHeatMode(s.state == "heat");
                // JsonUtility reads a JSON null as 0, so treat anything below the valid range as "no target".
                if (a != null && !float.IsNaN(a.temperature) && a.temperature > 0f) thermostat.SetTarget(a.temperature);
            }
            finally { applyingRemote = false; }
            knownHeat = thermostat.HeatMode;
            knownTarget = thermostat.Target;
        }
    }
}
