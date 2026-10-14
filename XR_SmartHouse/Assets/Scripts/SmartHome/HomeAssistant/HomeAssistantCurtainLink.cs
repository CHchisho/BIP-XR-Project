using System.Collections;
using UnityEngine;

namespace SmartHouse.HomeAssistant
{
    /// <summary>
    /// Mirrors a virtual CurtainDevice with a real Home Assistant cover, both ways:
    /// opening/closing the curtain in VR opens/closes the real blind, and the real blind's
    /// movement and position (HA app, wall button) are shown in VR on the next poll.
    /// </summary>
    [RequireComponent(typeof(CurtainDevice))]
    public class HomeAssistantCurtainLink : MonoBehaviour
    {
        [Tooltip("cover.* entity, e.g. cover.aqara_roller_shade_driver_e1")]
        [SerializeField] string entityId = "cover.aqara_roller_shade_driver_e1";
        [Tooltip("Seconds between state reads. 2-4 s is enough.")]
        [SerializeField] float pollInterval = 3f;
        [Tooltip("Seconds to ignore polls after sending a command; a blind reports its new state slowly.")]
        [SerializeField] float commandSettleTime = 5f;

        CurtainDevice curtain;
        bool available;
        bool applyingRemote;
        bool knownOpen; // last open/closed state that is in sync with Home Assistant
        float ignorePollUntil;

        public bool Available => available;

        void Awake() => curtain = GetComponent<CurtainDevice>();

        void OnEnable()
        {
            knownOpen = curtain.IsOpen;
            curtain.StateChanged += OnCurtainChanged;
            StartCoroutine(PollLoop());
        }

        void OnDisable()
        {
            curtain.StateChanged -= OnCurtainChanged;
            StopAllCoroutines();
        }

        void OnCurtainChanged(SmartDevice _)
        {
            if (applyingRemote) return; // this change came from Home Assistant; don't echo it back
            // StateChanged also fires while the curtain animates; only a new open/closed target is a command.
            if (curtain.IsOpen == knownOpen) return;
            knownOpen = curtain.IsOpen;

            var client = HomeAssistantClient.Instance;
            if (!client.HasToken) return;
            if (!available)
            {
                Debug.LogWarning($"[HomeAssistant] {entityId} is unavailable or not reached yet; command not sent.");
                return;
            }

            ignorePollUntil = Time.unscaledTime + commandSettleTime;
            StartCoroutine(client.CallService("cover", knownOpen ? "open_cover" : "close_cover", entityId, ok =>
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

            // While moving, show where the blind is heading; once stopped, its exact position.
            float open01;
            if (s.state == "opening") open01 = 1f;
            else if (s.state == "closing") open01 = 0f;
            else if (s.attributes != null && s.attributes.current_position >= 0) open01 = s.attributes.current_position / 100f;
            else open01 = s.state == "open" ? 1f : 0f;

            applyingRemote = true;
            try { curtain.SetOpenAmount(open01); }
            finally { applyingRemote = false; }
            knownOpen = curtain.IsOpen;
        }
    }
}
