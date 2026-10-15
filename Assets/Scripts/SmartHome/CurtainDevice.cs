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
                if (!IsOpen) return "Closed";
                return openPercent >= 99 ? "Open" : $"Open {openPercent}%";
            }
        }

        public override bool IsActive => IsOpen;
        public bool IsOpen => target < 1f - 0.001f;
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

        public override void Interact() => SetOpenAmount(IsOpen ? 0f : 1f);

        /// <summary>0 = fully closed (down), 1 = fully open (rolled up). Animates to it.</summary>
        public void SetOpenAmount(float open01)
        {
            float newTarget = Mathf.Lerp(1f, openedFraction, Mathf.Clamp01(open01));
            if (Mathf.Approximately(newTarget, target)) return;
            target = newTarget;
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
