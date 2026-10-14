using System;
using System.Collections;
using UnityEngine;

namespace SmartHouse
{
    /// <summary>
    /// Zigbee smart lock on the outside of the front door. Pressing it turns the thumbturn a
    /// quarter turn to unlock and then opens the door; once the door has swung shut again the
    /// thumbturn turns back and the door is locked. The LED glows green while unlocked.
    /// </summary>
    public class DoorLockDevice : SmartDevice
    {
        [Tooltip("Pivot of the knob + grip, on the knob's axis; its local Z points out of the lock.")]
        [SerializeField] Transform thumbturn;
        [SerializeField] Renderer led;
        [SerializeField] DoorDevice door;
        [Tooltip("Thumbturn rotation when unlocked, in degrees around the knob axis (negative = clockwise seen from the front).")]
        [SerializeField] float unlockedAngle = -90f;
        [SerializeField] float turnDuration = 0.6f;
        [SerializeField] Color ledColor = new Color(0.25f, 1f, 0.45f);

        bool locked = true;
        float angle;
        Quaternion lockedRotation;
        Material ledMaterial;
        AudioSource sound;
        Coroutine turning;

        public bool IsLocked => locked;

        public override string Status => turning != null
            ? (locked ? "Locking…" : "Unlocking…")
            : (locked ? "Locked" : "Unlocked");
        public override bool IsActive => !locked;
        public override Color AccentColor => new Color(0.4f, 0.9f, 0.55f);

        protected override void Awake()
        {
            base.Awake();
            if (thumbturn != null) lockedRotation = thumbturn.localRotation;
            if (led != null)
            {
                ledMaterial = led.material;
                ledMaterial.EnableKeyword("_EMISSION");
            }
            sound = CreateAudioSource(gameObject, true);
            sound.clip = ToneLibrary.Click();
            SetLed(false);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (door == null) return;
            door.Opened += OnDoorOpened;
            door.Closed += OnDoorClosed;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (door == null) return;
            door.Opened -= OnDoorOpened;
            door.Closed -= OnDoorClosed;
        }

        public override void Interact()
        {
            if (locked) Unlock(openDoor: true);
            else if (door != null && door.IsOpen) door.Close(); // relocks once the door is shut
            else Lock();
        }

        public void Unlock(bool openDoor)
        {
            if (!locked) return;
            locked = false;
            Turn(unlockedAngle, openDoor && door != null ? door.Open : (Action)null);
        }

        public void Lock()
        {
            if (locked) return;
            locked = true;
            Turn(0f, null);
        }

        // The door was opened some other way (clicked directly, intercom): show the lock as open.
        void OnDoorOpened()
        {
            if (locked) Unlock(openDoor: false);
        }

        void OnDoorClosed() => Lock();

        void Turn(float target, Action whenDone)
        {
            if (turning != null) StopCoroutine(turning);
            turning = StartCoroutine(TurnRoutine(target, whenDone));
        }

        IEnumerator TurnRoutine(float target, Action whenDone)
        {
            sound.Play();
            NotifyChanged();
            if (target == 0f) SetLed(false);

            float start = angle;
            for (float t = 0f; t < 1f;)
            {
                t = Mathf.Min(1f, t + Time.deltaTime / turnDuration);
                angle = Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t));
                if (thumbturn != null) thumbturn.localRotation = lockedRotation * Quaternion.AngleAxis(angle, Vector3.forward);
                yield return null;
            }

            turning = null;
            SetLed(!locked);
            NotifyChanged();
            whenDone?.Invoke();
        }

        void SetLed(bool on)
        {
            if (ledMaterial == null) return;
            ledMaterial.SetColor("_BaseColor", on ? ledColor : Color.black);
            ledMaterial.SetColor("_EmissionColor", on ? ledColor * 6f : Color.black);
        }
    }
}
