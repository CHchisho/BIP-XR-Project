using UnityEngine;

namespace SmartHouse
{
    /// <summary>
    /// Indoor intercom display. Shows the doorbell camera when someone rings;
    /// pressing it during a call unlocks and opens the front door.
    /// </summary>
    public class IntercomDevice : SmartDevice
    {
        [SerializeField] Renderer screen;
        [Tooltip("Unlit material used for the screen so the camera feed is shown at full brightness.")]
        [SerializeField] Material screenTemplate;
        [SerializeField] DoorDevice door;
        [SerializeField] Color idleColor = new Color(0.02f, 0.03f, 0.05f);

        Material screenMaterial;
        DoorbellDevice activeCall;

        public override string Status => activeCall != null ? "Incoming call · press to open" : "Standby";
        public override bool IsActive => activeCall != null;
        public override Color AccentColor => new Color(1f, 0.55f, 0.35f);

        protected override void Awake()
        {
            base.Awake();
            if (screen != null)
            {
                screenMaterial = screenTemplate != null ? new Material(screenTemplate) : screen.material;
                screen.material = screenMaterial;
            }
            HideCall();
        }

        public override void Interact()
        {
            if (activeCall == null) return;
            if (door != null) door.Open();
            var call = activeCall;
            HideCall();
            call.EndCall();
        }

        public void ShowCall(DoorbellDevice doorbell)
        {
            activeCall = doorbell;
            if (screenMaterial != null)
            {
                screenMaterial.SetTexture("_BaseMap", doorbell.Feed);
                screenMaterial.SetColor("_BaseColor", Color.white);
            }
            NotifyChanged();
        }

        public void HideCall()
        {
            activeCall = null;
            if (screenMaterial != null)
            {
                screenMaterial.SetTexture("_BaseMap", null);
                screenMaterial.SetColor("_BaseColor", idleColor);
            }
            NotifyChanged();
        }
    }
}
