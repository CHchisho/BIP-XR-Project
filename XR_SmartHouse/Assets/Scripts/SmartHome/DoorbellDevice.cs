using UnityEngine;

namespace SmartHouse
{
    /// <summary>
    /// Video doorbell (G4). Pressing it rings the chime, a visitor appears on the porch
    /// and the intercom inside shows the doorbell camera feed.
    /// </summary>
    public class DoorbellDevice : SmartDevice
    {
        [SerializeField] Transform cameraAnchor;
        [SerializeField] Transform visitorPoint;
        [SerializeField] IntercomDevice intercom;
        [SerializeField] DoorDevice door;
        [SerializeField] float visitorStaysFor = 30f;

        AudioSource chime;
        Camera doorCamera;
        GameObject visitor;
        float visitorTimer;

        public RenderTexture Feed { get; private set; }
        public bool VisitorWaiting => visitor != null && visitor.activeSelf;

        public override string Status => VisitorWaiting ? "Visitor at the door" : "Ready";
        public override bool IsActive => VisitorWaiting;
        public override Color AccentColor => new Color(1f, 0.55f, 0.35f);

        protected override void Awake()
        {
            base.Awake();
            chime = CreateAudioSource(gameObject, false);
            chime.clip = ToneLibrary.Chime();
            CreateCamera();
            CreateVisitor();
        }

        void Start()
        {
            if (door != null) door.Opened += () => { if (VisitorWaiting) visitorTimer = Mathf.Min(visitorTimer, 4f); };
        }

        public override void Interact() => Ring();

        public void Ring()
        {
            chime.Play();
            visitor.SetActive(true);
            visitorTimer = visitorStaysFor;
            doorCamera.enabled = true;
            if (intercom != null) intercom.ShowCall(this);
            NotifyChanged();
        }

        public void EndCall()
        {
            doorCamera.enabled = false;
            NotifyChanged();
        }

        void Update()
        {
            if (!VisitorWaiting) return;

            visitorTimer -= Time.deltaTime;
            if (visitorTimer <= 0f)
            {
                visitor.SetActive(false);
                if (intercom != null) intercom.HideCall();
                EndCall();
            }
        }

        void CreateCamera()
        {
            Feed = new RenderTexture(512, 288, 16) { name = "DoorbellFeed" };
            var anchor = cameraAnchor != null ? cameraAnchor : transform;
            var go = new GameObject("Doorbell Camera");
            go.transform.SetParent(anchor, false);
            doorCamera = go.AddComponent<Camera>();
            doorCamera.targetTexture = Feed;
            doorCamera.fieldOfView = 100f;
            doorCamera.nearClipPlane = 0.05f;
            doorCamera.enabled = false;

            Vector3 lookTarget = visitorPoint != null ? visitorPoint.position + Vector3.up * 1.3f : anchor.position + Vector3.forward;
            go.transform.rotation = Quaternion.LookRotation(lookTarget - anchor.position, Vector3.up);
        }

        void CreateVisitor()
        {
            // A simple stand-in visitor built from primitives.
            visitor = new GameObject("Visitor");
            var spot = visitorPoint != null ? visitorPoint : transform;
            visitor.transform.position = spot.position;
            Vector3 towardsDoor = transform.position - spot.position;
            towardsDoor.y = 0f;
            if (towardsDoor.sqrMagnitude > 0.001f) visitor.transform.rotation = Quaternion.LookRotation(towardsDoor);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.transform.SetParent(visitor.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.72f, 0f);
            body.transform.localScale = new Vector3(0.5f, 0.72f, 0.35f);
            Tint(body, new Color(0.2f, 0.35f, 0.6f));

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.transform.SetParent(visitor.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.58f, 0f);
            head.transform.localScale = Vector3.one * 0.26f;
            Tint(head, new Color(0.85f, 0.68f, 0.55f));

            visitor.SetActive(false);
        }

        static void Tint(GameObject go, Color color)
        {
            var r = go.GetComponent<Renderer>();
            r.material.color = color;
        }
    }
}
