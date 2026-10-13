using UnityEngine;

namespace SmartHouse
{
    /// <summary>Wall switch that toggles a lamp and tilts its rocker.</summary>
    public class LightSwitchDevice : SmartDevice
    {
        [SerializeField] LampDevice lamp;
        [SerializeField] Transform rocker;
        [SerializeField] float rockerAngle = 12f;

        AudioSource audioSource;
        Quaternion rockerRest;

        public override string Status => IsActive ? "Light on" : "Light off";
        public override bool IsActive => lamp != null && lamp.IsOn;

        protected override void Awake()
        {
            base.Awake();
            audioSource = CreateAudioSource(gameObject, true);
            audioSource.clip = ToneLibrary.Click();
            if (rocker != null) rockerRest = rocker.localRotation;
        }

        void Start()
        {
            if (lamp != null)
            {
                lamp.StateChanged += _ => UpdateRocker();
                UpdateRocker();
            }
        }

        public override void Interact()
        {
            audioSource.Play();
            if (lamp != null) lamp.SetOn(!lamp.IsOn);
        }

        void UpdateRocker()
        {
            if (rocker != null)
                rocker.localRotation = rockerRest * Quaternion.Euler(0f, 0f, lamp.IsOn ? -rockerAngle : rockerAngle);
            NotifyChanged();
        }
    }
}
