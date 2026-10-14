using System.Collections;
using UnityEngine;

namespace SmartHouse.HomeAssistant
{
    /// <summary>
    /// Mirrors a virtual LampDevice with a real Home Assistant light or switch, both ways:
    /// toggling the lamp in VR switches the real device, and switching the real device
    /// (wall knob, HA app) updates the lamp in VR on the next poll.
    /// </summary>
    [RequireComponent(typeof(LampDevice))]
    public class HomeAssistantLampLink : MonoBehaviour
    {
        [Tooltip("light.* or switch.* entity, e.g. light.aqara_dimmer_switch_h2")]
        [SerializeField] string entityId = "light.aqara_dimmer_switch_h2";
        [Tooltip("Seconds between state reads. 2-4 s is enough.")]
        [SerializeField] float pollInterval = 3f;

        LampDevice lamp;
        bool applyingRemote;
        bool available;
        float ignorePollUntil;

        public bool Available => available;
        string Domain => entityId.Split('.')[0];

        void Awake() => lamp = GetComponent<LampDevice>();

        void OnEnable()
        {
            lamp.StateChanged += OnLampChanged;
            StartCoroutine(PollLoop());
        }

        void OnDisable()
        {
            lamp.StateChanged -= OnLampChanged;
            StopAllCoroutines();
        }

        void OnLampChanged(SmartDevice _)
        {
            if (applyingRemote) return; // this change came from Home Assistant; don't echo it back

            var client = HomeAssistantClient.Instance;
            if (!client.HasToken) return;
            if (!available)
            {
                Debug.LogWarning($"[HomeAssistant] {entityId} is unavailable or not reached yet; command not sent.");
                return;
            }

            // A poll that was already in flight could still report the old state; ignore polls briefly.
            ignorePollUntil = Time.unscaledTime + pollInterval;
            StartCoroutine(client.CallService(Domain, lamp.IsOn ? "turn_on" : "turn_off", entityId, ok =>
            {
                if (!ok) ignorePollUntil = 0f; // let the next poll restore the real state
            }));
        }

        IEnumerator PollLoop()
        {
            var wait = new WaitForSecondsRealtime(pollInterval);
            while (true)
            {
                var client = HomeAssistantClient.Instance;
                if (client.HasToken && Time.unscaledTime >= ignorePollUntil)
                    yield return client.GetState(entityId, Apply);
                yield return wait;
            }
        }

        void Apply(HomeAssistantClient.EntityState s)
        {
            if (s == null) { available = false; return; }

            available = s.state != "unavailable" && s.state != "unknown";
            if (!available || Time.unscaledTime < ignorePollUntil) return;

            bool on = s.state == "on";
            if (on == lamp.IsOn) return;

            applyingRemote = true;
            try { lamp.SetOn(on); }
            finally { applyingRemote = false; }
        }
    }
}
