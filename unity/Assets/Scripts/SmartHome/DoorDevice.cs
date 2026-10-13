using UnityEngine;

namespace SmartHouse
{
    /// <summary>Front door with a smart lock. Swings open around its hinge and closes again automatically.</summary>
    public class DoorDevice : SmartDevice
    {
        [SerializeField] float openAngle = -95f;
        [SerializeField] float swingSpeed = 120f;
        [SerializeField] float autoCloseAfter = 8f;

        Quaternion closedRotation;
        float angle;
        float targetAngle;
        float openTimer;
        AudioSource lockSound;

        public bool IsOpen => targetAngle != 0f;
        public event System.Action Opened;

        public override string Status => IsOpen ? "Unlocked" : "Locked";
        public override bool IsActive => IsOpen;
        public override Color AccentColor => new Color(0.4f, 0.9f, 0.55f);

        protected override void Awake()
        {
            base.Awake();
            closedRotation = transform.localRotation;
            lockSound = CreateAudioSource(gameObject, true);
            lockSound.clip = ToneLibrary.Click();
        }

        public override void Interact()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            if (IsOpen) return;
            targetAngle = openAngle;
            openTimer = autoCloseAfter;
            lockSound.Play();
            Opened?.Invoke();
            NotifyChanged();
        }

        public void Close()
        {
            if (!IsOpen) return;
            targetAngle = 0f;
            NotifyChanged();
        }

        void Update()
        {
            if (IsOpen && autoCloseAfter > 0f)
            {
                openTimer -= Time.deltaTime;
                if (openTimer <= 0f) Close();
            }

            if (!Mathf.Approximately(angle, targetAngle))
            {
                angle = Mathf.MoveTowards(angle, targetAngle, swingSpeed * Time.deltaTime);
                transform.localRotation = closedRotation * Quaternion.Euler(0f, angle, 0f);
                if (Mathf.Approximately(angle, 0f)) lockSound.Play();
            }
        }
    }
}
