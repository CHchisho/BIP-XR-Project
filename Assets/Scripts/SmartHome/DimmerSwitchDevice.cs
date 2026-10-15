using UnityEngine;

namespace SmartHouse
{
    /// <summary>
    /// Zigbee rotary dimmer on the wall. Pressing the knob toggles the lamp; holding the trigger and
    /// moving the controller up/down (or scrolling on the desktop) dims it. The knob turns to show
    /// the lamp's brightness, also when it is dimmed on the real switch or in Home Assistant.
    /// </summary>
    public class DimmerSwitchDevice : SmartDevice, IAdjustable
    {
        [SerializeField] LampDevice lamp;
        [Tooltip("Pivot of the knob; its local Z points out of the wall.")]
        [SerializeField] Transform knob;
        [Tooltip("Knob rotation at full brightness, in degrees (negative = clockwise seen from the front).")]
        [SerializeField] float fullBrightnessAngle = -270f;
        [SerializeField] float turnSpeed = 360f;

        AudioSource click;
        Quaternion knobRest;
        float angle;

        public override string Status => lamp == null ? "Not connected" : "Light " + lamp.Status.ToLowerInvariant();
        public override bool IsActive => lamp != null && lamp.IsOn;
        public override bool ShowPopups => false; // the lamp itself pops up
        public override SmartDevice ControlledDevice => lamp;

        protected override void Awake()
        {
            base.Awake();
            click = CreateAudioSource(gameObject, true);
            click.clip = ToneLibrary.Click();
            if (knob != null) knobRest = knob.localRotation;
        }

        void Start()
        {
            if (lamp != null) lamp.StateChanged += _ => NotifyChanged();
        }

        public override void Interact()
        {
            click.Play();
            if (lamp != null) lamp.SetOn(!lamp.IsOn);
        }

        public void Adjust(float amount)
        {
            if (lamp != null) lamp.Adjust(amount);
        }

        void Update()
        {
            if (knob == null || lamp == null) return;
            float target = lamp.IsOn ? fullBrightnessAngle * lamp.Brightness : 0f;
            if (Mathf.Approximately(angle, target)) return;
            angle = Mathf.MoveTowards(angle, target, turnSpeed * Time.deltaTime);
            knob.localRotation = knobRest * Quaternion.AngleAxis(angle, Vector3.forward);
        }
    }
}
