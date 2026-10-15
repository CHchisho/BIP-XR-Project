using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SmartHouse.HomeAssistant
{
    /// <summary>
    /// Mirrors a virtual SpeakerDevice with the lab's Home Assistant media player (ha-api-speaker.md), both ways:
    /// pressing the speaker in VR starts a radio station from Browse media (Radio Browser → Popular) with
    /// play_media, pressing again sends turn_off, and dragging sends volume_set once the drag settles.
    /// Changes made on the real speaker (HA dashboard, Sonos app) show up in VR on the next poll.
    /// The station id is always taken from the browse result, never typed in.
    /// </summary>
    [RequireComponent(typeof(SpeakerDevice))]
    public class HomeAssistantSpeakerLink : MonoBehaviour
    {
        [Tooltip("media_player.* entity, e.g. media_player.famnest2")]
        [SerializeField] string entityId = "media_player.famnest2";
        [Tooltip("Browse media folder the station is picked from.")]
        [SerializeField] string stationFolder = "media-source://radio_browser/popular";
        [Tooltip("Title of the station to play; the first playable one is used when it isn't in the folder.")]
        [SerializeField] string stationTitle = "BBC World Service";
        [Tooltip("Mute the VR model's built-in track while the real speaker is reachable, so you don't hear both.")]
        [SerializeField] bool muteVirtualWhenLinked = true;
        [Tooltip("Seconds between state reads. 2-4 s is enough.")]
        [SerializeField] float pollInterval = 3f;
        [Tooltip("Seconds to ignore polls after sending a command; the speaker takes a moment to start a stream.")]
        [SerializeField] float commandSettleTime = 5f;
        [Tooltip("Volume is sent once dragging has paused this long, so a drag doesn't flood Home Assistant.")]
        [SerializeField] float volumeDebounce = 0.4f;

        SpeakerDevice speaker;
        bool available;
        bool applyingRemote;
        bool knownPlaying;
        float knownVolume;
        float ignorePollUntil;
        float sendVolumeAt = -1f;
        HomeAssistantClient.MediaItem station;

        public bool Available => available;
        /// <summary>The station that play_media is sent with, once it has been looked up.</summary>
        public HomeAssistantClient.MediaItem Station => station;

        void Awake() => speaker = GetComponent<SpeakerDevice>();

        void OnEnable()
        {
            knownPlaying = speaker.IsPlaying;
            knownVolume = speaker.Volume;
            speaker.StateChanged += OnSpeakerChanged;
            StartCoroutine(PollLoop());
        }

        void OnDisable()
        {
            speaker.StateChanged -= OnSpeakerChanged;
            StopAllCoroutines();
            speaker.SetLocalAudio(true);
        }

        void OnSpeakerChanged(SmartDevice _)
        {
            if (applyingRemote) return; // this change came from Home Assistant; don't echo it back

            if (speaker.IsPlaying != knownPlaying)
            {
                knownPlaying = speaker.IsPlaying;
                if (!CanSend()) return;
                ignorePollUntil = Mathf.Max(ignorePollUntil, Time.unscaledTime + commandSettleTime);
                if (knownPlaying) StartCoroutine(PlayStation());
                else Call("turn_off", null);
            }
            if (!Mathf.Approximately(speaker.Volume, knownVolume))
            {
                sendVolumeAt = Time.unscaledTime + volumeDebounce;
                ignorePollUntil = Mathf.Max(ignorePollUntil, sendVolumeAt + commandSettleTime);
            }
        }

        void Update()
        {
            if (sendVolumeAt < 0f || Time.unscaledTime < sendVolumeAt) return;
            sendVolumeAt = -1f;
            knownVolume = speaker.Volume;
            if (CanSend())
                Call("volume_set", "\"volume_level\":" + knownVolume.ToString("0.00", CultureInfo.InvariantCulture));
        }

        bool CanSend()
        {
            if (!HomeAssistantClient.Instance.HasToken) return false;
            if (available) return true;
            Debug.LogWarning($"[HomeAssistant] {entityId} is unavailable or not reached yet; command not sent.");
            return false;
        }

        IEnumerator PlayStation()
        {
            var client = HomeAssistantClient.Instance;
            if (station == null)
            {
                List<HomeAssistantClient.MediaItem> items = null;
                yield return client.BrowseMedia(entityId, stationFolder, r => items = r);
                station = PickStation(items);
                if (station == null)
                {
                    Debug.LogWarning($"[HomeAssistant] No playable station in {stationFolder}; nothing sent to {entityId}.");
                    ignorePollUntil = 0f; // let the next poll restore the real state
                    yield break;
                }
                Debug.Log($"[HomeAssistant] {entityId}: station '{station.title}' ({station.media_content_id}, {station.media_content_type}).");
            }
            if (!speaker.IsPlaying) yield break; // stopped again while browsing

            speaker.SetNowPlaying(station.title);
            Call("play_media",
                "\"media_content_id\":" + HomeAssistantClient.JsonString(station.media_content_id) +
                ",\"media_content_type\":" + HomeAssistantClient.JsonString(station.media_content_type));
        }

        HomeAssistantClient.MediaItem PickStation(List<HomeAssistantClient.MediaItem> items)
        {
            if (items == null) return null;
            HomeAssistantClient.MediaItem first = null;
            foreach (var item in items)
            {
                if (!item.can_play || string.IsNullOrEmpty(item.media_content_id) || string.IsNullOrEmpty(item.media_content_type)) continue;
                if (string.Equals(item.title?.Trim(), stationTitle, System.StringComparison.OrdinalIgnoreCase)) return item;
                first ??= item;
            }
            return first;
        }

        void Call(string service, string extraJson)
        {
            ignorePollUntil = Mathf.Max(ignorePollUntil, Time.unscaledTime + commandSettleTime);
            StartCoroutine(HomeAssistantClient.Instance.CallService("media_player", service, entityId, ok =>
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
                if (client.HasToken && Time.unscaledTime >= ignorePollUntil && sendVolumeAt < 0f)
                    yield return client.GetState(entityId, Apply);
                yield return wait;
            }
        }

        void Apply(HomeAssistantClient.EntityState s)
        {
            available = s != null && s.state != "unavailable" && s.state != "unknown";
            if (muteVirtualWhenLinked) speaker.SetLocalAudio(!available);
            if (!available || Time.unscaledTime < ignorePollUntil || sendVolumeAt >= 0f) return;

            var a = s.attributes;
            applyingRemote = true;
            try
            {
                bool playing = s.state == "playing";
                if (a != null && !float.IsNaN(a.volume_level)) speaker.SetVolume(a.volume_level);
                if (playing && a != null && !string.IsNullOrEmpty(a.media_title)) speaker.SetNowPlaying(a.media_title);
                else if (!playing) speaker.SetNowPlaying(null);
                speaker.SetPlaying(playing);
            }
            finally { applyingRemote = false; }
            knownPlaying = speaker.IsPlaying;
            knownVolume = speaker.Volume;
        }
    }
}
