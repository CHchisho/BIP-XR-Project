using UnityEngine;

namespace SmartHouse
{
    /// <summary>
    /// Wall switch that toggles a lamp and tilts its rocker. Twisting at it (trigger or grip) dims the lamp,
    /// and grip + twist changes the color of a color lamp, the same as at the lamp itself.
    /// </summary>
    public class LightSwitchDevice : SmartDevice, IAdjustable
    {
        [SerializeField] LampDevice lamp;
        [SerializeField] Transform rocker;
        [SerializeField] float rockerAngle = 12f;

        AudioSource audioSource;
        Quaternion rockerRest;

        public override string Status => lamp == null ? "Not connected" : "Light " + lamp.Status.ToLowerInvariant();
        public override bool IsActive => lamp != null && lamp.IsOn;
        public override bool ShowPopups => false; // the lamp itself pops up
        public override SmartDevice ControlledDevice => lamp;

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

        public void Adjust(float amount)
        {
            if (lamp != null) lamp.Adjust(amount);
        }

        void UpdateRocker()
        {
            if (rocker != null)
                rocker.localRotation = rockerRest * Quaternion.Euler(0f, 0f, lamp.IsOn ? -rockerAngle : rockerAngle);
            NotifyChanged();
        }
    }
}
