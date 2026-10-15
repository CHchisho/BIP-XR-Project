using System.Collections;
using UnityEngine;

namespace SmartHouse.HomeAssistant
{
    /// <summary>
    /// Mirrors a virtual DoorLockDevice with a real Home Assistant lock, both ways: locking/unlocking
    /// in VR (including the automatic relock when the VR door shuts) calls lock.lock / lock.unlock,
    /// and locking/unlocking the real lock turns the thumbturn in VR on the next poll.
    /// A remote unlock only unlocks; it does not swing the VR door open.
    /// </summary>
    [RequireComponent(typeof(DoorLockDevice))]
    public class HomeAssistantLockLink : MonoBehaviour
    {
        [Tooltip("lock.* entity, e.g. lock.aqara_smart_lock_u200_lite")]
        [SerializeField] string entityId = "lock.aqara_smart_lock_u200_lite";
        [Tooltip("Seconds between state reads. 2-4 s is enough.")]
        [SerializeField] float pollInterval = 3f;
        [Tooltip("Seconds to ignore polls after sending a command; a lock takes a moment to turn and report.")]
        [SerializeField] float commandSettleTime = 6f;

        DoorLockDevice lockDevice;
        bool available;
        bool applyingRemote;
        bool knownLocked; // last state that is in sync with Home Assistant
        float ignorePollUntil;

        public bool Available => available;

        void Awake() => lockDevice = GetComponent<DoorLockDevice>();

        void OnEnable()
        {
            knownLocked = lockDevice.IsLocked;
            lockDevice.StateChanged += OnLockChanged;
            StartCoroutine(PollLoop());
        }

        void OnDisable()
        {
            lockDevice.StateChanged -= OnLockChanged;
            StopAllCoroutines();
        }

        void OnLockChanged(SmartDevice _)
        {
            if (applyingRemote) return; // this change came from Home Assistant; don't echo it back
            // StateChanged also fires when the turn animation starts and ends; only a new target is a command.
            if (lockDevice.IsLocked == knownLocked) return;
            knownLocked = lockDevice.IsLocked;

            var client = HomeAssistantClient.Instance;
            if (!client.HasToken) return;
            if (!available)
            {
                Debug.LogWarning($"[HomeAssistant] {entityId} is unavailable or not reached yet; command not sent.");
                return;
            }

            ignorePollUntil = Time.unscaledTime + commandSettleTime;
            StartCoroutine(client.CallService("lock", knownLocked ? "lock" : "unlock", entityId, ok =>
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

            // locked / unlocked, plus the transitional locking / unlocking and open; jammed is left alone.
            bool? locked = s.state switch
            {
                "locked" or "locking" => true,
                "unlocked" or "unlocking" or "open" or "opening" => false,
                _ => (bool?)null,
            };
            if (locked == null || locked == lockDevice.IsLocked) return;

            applyingRemote = true;
            try
            {
                if (locked.Value) lockDevice.Lock();
                else lockDevice.Unlock(openDoor: false);
            }
            finally { applyingRemote = false; }
            knownLocked = lockDevice.IsLocked;
        }
    }
}
