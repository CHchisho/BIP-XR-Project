using UnityEngine;

namespace SmartHouse
{
    /// <summary>Sonos-style speaker: press to play / pause spatial music.</summary>
    public class SpeakerDevice : SmartDevice
    {
        [SerializeField] Transform audioAnchor;
        [SerializeField] AudioClip track;
        [SerializeField, Range(0f, 1f)] float volume = 0.6f;

        AudioSource source;

        public override string Status => IsActive ? "Playing" : "Paused";
        public override bool IsActive => source != null && source.isPlaying;
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

        public override void Interact()
        {
            if (source.isPlaying) source.Pause();
            else source.Play();
            NotifyChanged();
        }
    }
}
