using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SmartHouse
{
    /// <summary>
    /// Base class for every interactive device in the house. Makes the object selectable
    /// with XR controllers (ray or direct) and exposes a status line for the hub panel.
    /// Devices that implement <see cref="IAdjustable"/> can also be adjusted while grabbed (grip): move the
    /// controller up or down, or twist the wrist like a knob, to change the value; a color lamp (or a
    /// switch that operates one) changes color when twisted instead. A short press without moving still presses.
    /// A wall panel forwards its grab here (<see cref="BeginGrab"/>), so grabbing the panel works the same.
    /// (The trigger does the same through <see cref="TriggerTwist"/>, which works without grabbing.)
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public abstract class SmartDevice : MonoBehaviour
    {
        [SerializeField] string displayName = "Device";

        public string DisplayName => displayName;
        public abstract string Status { get; }

        /// <summary>Whether the device is in its "on / open / playing" state (lights the status dot).</summary>
        public virtual bool IsActive => false;
        public virtual Color AccentColor => new Color(1f, 0.78f, 0.36f);

        /// <summary>False for devices that only mirror another device (a wall switch shows its lamp's state).</summary>
        public virtual bool ShowPopups => true;

        /// <summary>The device this one operates (a wall switch's lamp), or null. Its wall panel lights up on hover too.</summary>
        public virtual SmartDevice ControlledDevice => null;

        /// <summary>The color lamp this device sets the color of when twisted while grabbed: itself or the lamp it operates; null if none.</summary>
        public IColorDevice ColorTarget
        {
            get
            {
                if (this is IColorDevice self && self.SupportsColor) return self;
                return ControlledDevice is IColorDevice lamp && lamp.SupportsColor ? lamp : null;
            }
        }

        public event System.Action<SmartDevice> StateChanged;

        XRSimpleInteractable interactable;

        protected virtual void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            EnsureCollider();
            if (GetComponent<DeviceHoverFeedback>() == null) gameObject.AddComponent<DeviceHoverFeedback>();
        }

        protected virtual void OnEnable()
        {
            interactable.selectEntered.AddListener(OnSelected);
            interactable.selectExited.AddListener(OnDeselected);
        }

        protected virtual void OnDisable()
        {
            interactable.selectEntered.RemoveListener(OnSelected);
            interactable.selectExited.RemoveListener(OnDeselected);
            dragInteractor = null;
        }

        // Controller movement (metres, up/down) that changes an adjustable device over its full range.
        const float DragFullRange = 0.3f;
        const float DragDeadZone = 0.025f;

        Transform dragInteractor;
        bool dragged;
        float selectTime;

        void OnSelected(SelectEnterEventArgs args) => BeginGrab(args.interactorObject.transform);
        void OnDeselected(SelectExitEventArgs args) => EndGrab(args.interactorObject.transform);

        /// <summary>A controller grabbed the device (or its wall panel). Plain devices are pressed right away.</summary>
        public void BeginGrab(Transform hand)
        {
            if (this is IAdjustable || ColorTarget != null)
            {
                // Decide on release: a press if the hand stayed still, otherwise it was a drag or twist.
                dragInteractor = hand;
                dragged = false;
                selectTime = Time.unscaledTime;
                StartCoroutine(Drag(dragInteractor));
                return;
            }
            Press();
        }

        public void EndGrab(Transform hand)
        {
            if (dragInteractor == null || hand != dragInteractor) return;
            dragInteractor = null;
            // In the desktop simulator one mouse click is both a DesktopPointer press and an XR select;
            // if the device was already pressed during this select, don't toggle it a second time.
            if (!dragged && lastPressTime < selectTime - 0.05f) Press();
        }

        IEnumerator Drag(Transform hand)
        {
            float startY = hand.position.y;
            float lastY = startY;
            Quaternion lastRotation = hand.rotation;
            float twist = 0f;
            bool twisting = false;
            while (dragInteractor == hand && hand != null)
            {
                float y = hand.position.y;
                twist += WristTwist.Delta(lastRotation, hand.rotation);
                lastRotation = hand.rotation;

                // Whichever passes its dead zone first decides: twisting or moving up/down.
                if (!dragged && Mathf.Abs(twist) > WristTwist.DeadZoneDegrees)
                {
                    dragged = twisting = true;
                    twist = Mathf.Sign(twist) * (Mathf.Abs(twist) - WristTwist.DeadZoneDegrees);
                }
                else if (!dragged && this is IAdjustable && Mathf.Abs(y - startY) > DragDeadZone)
                {
                    dragged = true;
                    lastY = startY + Mathf.Sign(y - startY) * DragDeadZone;
                }

                if (twisting && twist != 0f)
                {
                    float amount = twist / WristTwist.FullRangeDegrees;
                    var colorLamp = ColorTarget;
                    if (colorLamp != null) colorLamp.AdjustColor(amount);
                    else ((IAdjustable)this).Adjust(amount);
                    twist = 0f;
                }
                else if (dragged && !twisting && !Mathf.Approximately(y, lastY))
                {
                    ((IAdjustable)this).Adjust((y - lastY) / DragFullRange);
                    lastY = y;
                }
                yield return null;
            }
        }

        float lastPressTime = -1f;

        /// <summary>
        /// Entry point for every input source (XR controller, desktop mouse).
        /// Ignores a second press in the same instant so two sources can't toggle twice.
        /// </summary>
        public void Press()
        {
            if (Time.unscaledTime - lastPressTime < 0.25f) return;
            lastPressTime = Time.unscaledTime;
            Interact();
        }

        /// <summary>Called when the user presses the device.</summary>
        public abstract void Interact();

        protected void NotifyChanged() => StateChanged?.Invoke(this);

        void EnsureCollider()
        {
            if (GetComponentInChildren<Collider>() != null) return;

            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);

            // Small devices get a slightly padded box so they are easy to hit with a ray.
            var box = gameObject.AddComponent<BoxCollider>();
            box.center = transform.InverseTransformPoint(bounds.center);
            var size = transform.InverseTransformVector(bounds.size);
            box.size = new Vector3(Mathf.Max(Mathf.Abs(size.x), 0.08f), Mathf.Max(Mathf.Abs(size.y), 0.08f), Mathf.Max(Mathf.Abs(size.z), 0.08f));
            interactable.colliders.Clear();
            interactable.colliders.Add(box);
        }

        protected static AudioSource CreateAudioSource(GameObject host, bool spatial)
        {
            var source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = spatial ? 1f : 0f;
            source.minDistance = 1f;
            source.maxDistance = 15f;
            return source;
        }
    }
}
