using UnityEngine;

namespace SmartHouse
{
    /// <summary>Motorised roller blind: rolls the shade up into the cassette and back down.</summary>
    public class CurtainDevice : SmartDevice
    {
        [SerializeField] Transform shade;
        [SerializeField] Transform bottomBar;
        [SerializeField, Range(0.02f, 1f)] float openedFraction = 0.05f;
        [SerializeField] float speed = 0.5f;

        float closedHeight;
        Vector3 shadeScale;
        Vector3 barClosedPosition;
        float current = 1f; // 1 = fully closed (down), openedFraction = rolled up
        float target = 1f;
        AudioSource motor;

        public override string Status
        {
            get
            {
                int openPercent = Mathf.RoundToInt(Mathf.InverseLerp(1f, openedFraction, current) * 100f);
                if (!Mathf.Approximately(current, target)) return (target < 1f ? "Opening " : "Closing ") + openPercent + "%";
                return target < 1f ? "Open" : "Closed";
            }
        }

        public override bool IsActive => target < 1f;
        public override Color AccentColor => new Color(0.45f, 0.78f, 1f);

        protected override void Awake()
        {
            base.Awake();
            if (shade != null) shadeScale = shade.localScale;
            if (bottomBar != null && shade != null)
            {
                barClosedPosition = bottomBar.position;
                closedHeight = shade.position.y - barClosedPosition.y;
            }
            motor = CreateAudioSource(gameObject, true);
            motor.clip = ToneLibrary.Click();
        }

        public override void Interact()
        {
            target = target < 1f ? 1f : openedFraction;
            motor.Play();
            NotifyChanged();
        }

        void Update()
        {
            if (Mathf.Approximately(current, target)) return;

            current = Mathf.MoveTowards(current, target, speed * Time.deltaTime);
            // The shade's pivot sits at the top (cassette), so scaling Y rolls it upwards.
            if (shade != null) shade.localScale = new Vector3(shadeScale.x, shadeScale.y * current, shadeScale.z);
            if (bottomBar != null) bottomBar.position = new Vector3(barClosedPosition.x, shade.position.y - closedHeight * current, barClosedPosition.z);

            if (Mathf.Approximately(current, target)) NotifyChanged();
        }
    }
}
