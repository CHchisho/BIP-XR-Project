using UnityEngine;

namespace SmartHouse
{
    /// <summary>
    /// Sonos-style speaker: press to play / stop spatial music; hold the trigger and move the controller
    /// up/down (or scroll on the desktop) to change the volume. With a HomeAssistantSpeakerLink it drives the
    /// real lab speaker, and the built-in track is muted while the real one is reachable.
    /// </summary>
    public class SpeakerDevice : SmartDevice, IAdjustable
    {
        [SerializeField] Transform audioAnchor;
        [SerializeField] AudioClip track;
        [SerializeField, Range(0f, 1f)] float volume = 0.6f;

        AudioSource source;
        bool playing;
        bool localAudio = true;
        string nowPlaying;

        public bool IsPlaying => playing;
        /// <summary>0..1</summary>
        public float Volume => volume;

        public override string Status => playing
            ? (string.IsNullOrEmpty(nowPlaying) ? "Playing" : nowPlaying) + $" · {Mathf.RoundToInt(volume * 100f)}%"
            : "Off";
        public override bool IsActive => playing;
        public override Color AccentColor => new Color(0.7f, 0.55f, 1f);

        protected override void Awake()
        {
            base.Awake();
            source = CreateAudioSource(audioAnchor != null ? audioAnchor.gameObject : gameObject, true);
            source.clip = track != null ? track : ToneLibrary.Music();
            source.loop = true;
            source.volume = volume;
            source.maxDistance = 12f;
        }

        public override void Interact() => SetPlaying(!playing);

        public void Adjust(float amount) => SetVolume(volume + amount);

        public void SetPlaying(bool on)
        {
            if (on == playing) return;
            playing = on;
            UpdateAudio();
            NotifyChanged();
        }

        public void SetVolume(float volume01)
        {
            volume01 = Mathf.Clamp01(volume01);
            if (Mathf.Approximately(volume01, volume)) return;
            volume = volume01;
            source.volume = volume;
            NotifyChanged();
        }

        /// <summary>Station or track name shown in the status line; null shows "Playing".</summary>
        public void SetNowPlaying(string title)
        {
            if (title == nowPlaying) return;
            nowPlaying = title;
            NotifyChanged();
        }

        /// <summary>Whether the built-in track is heard in VR (off while the real speaker is playing instead).</summary>
        public void SetLocalAudio(bool on)
        {
            localAudio = on;
            UpdateAudio();
        }

        void UpdateAudio()
        {
            bool audible = playing && localAudio;
            if (audible && !source.isPlaying) source.Play();
            else if (!audible && source.isPlaying) source.Stop();
        }
    }
}
