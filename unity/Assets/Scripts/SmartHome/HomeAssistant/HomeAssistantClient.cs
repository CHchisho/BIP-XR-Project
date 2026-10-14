using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace SmartHouse.HomeAssistant
{
    /// <summary>
    /// Talks to the lab's Home Assistant REST API (see ha-api-v1.md).
    /// The token is never stored in the scene: it is read from the HA_TOKEN environment
    /// variable, or from a gitignored Assets/Resources/ha_token.txt (needed for headset builds).
    /// The base URL can be overridden with the HA_URL environment variable.
    /// </summary>
    public class HomeAssistantClient : MonoBehaviour
    {
        [SerializeField] string baseUrl = "http://homeassistant.local:8123/";
        [SerializeField] int timeoutSeconds = 5;

        // Only these service domains may be called. lock.*, siren.* and alarm_control_panel.* are off-limits.
        static readonly string[] AllowedDomains = { "light", "switch" };

        static HomeAssistantClient instance;

        /// <summary>The client in the scene, or one created on demand with default settings.</summary>
        public static HomeAssistantClient Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindAnyObjectByType<HomeAssistantClient>();
                    if (instance == null)
                        instance = new GameObject("Home Assistant Client").AddComponent<HomeAssistantClient>();
                }
                return instance;
            }
        }

        [Serializable]
        public class EntityState
        {
            public string entity_id;
            public string state;
        }

        string token;
        bool loaded;

        public bool HasToken
        {
            get { EnsureLoaded(); return !string.IsNullOrEmpty(token); }
        }

        void Awake()
        {
            if (instance != null && instance != this) { Destroy(this); return; }
            instance = this;
            EnsureLoaded();
        }

        void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;

            var url = Environment.GetEnvironmentVariable("HA_URL");
            if (!string.IsNullOrWhiteSpace(url)) baseUrl = url.Trim();
            if (!baseUrl.EndsWith("/")) baseUrl += "/";

            token = Environment.GetEnvironmentVariable("HA_TOKEN");
            if (string.IsNullOrWhiteSpace(token))
            {
                var asset = Resources.Load<TextAsset>("ha_token");
                token = asset != null ? asset.text : null;
            }
            token = token?.Trim();

            if (string.IsNullOrEmpty(token))
                Debug.LogWarning("[HomeAssistant] No token found. Set the HA_TOKEN environment variable " +
                                 "or create Assets/Resources/ha_token.txt containing a long-lived access token.");
        }

        /// <summary>GET /api/states/{entityId}. Calls back with null on any failure.</summary>
        public IEnumerator GetState(string entityId, Action<EntityState> onResult)
        {
            EnsureLoaded();
            using var req = UnityWebRequest.Get(baseUrl + "api/states/" + entityId);
            Prepare(req);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                LogError(req, "read " + entityId);
                onResult?.Invoke(null);
                yield break;
            }

            onResult?.Invoke(JsonUtility.FromJson<EntityState>(req.downloadHandler.text));
        }

        /// <summary>POST /api/services/{domain}/{service} with {"entity_id": ...}.</summary>
        public IEnumerator CallService(string domain, string service, string entityId, Action<bool> onDone = null)
        {
            EnsureLoaded();
            if (Array.IndexOf(AllowedDomains, domain) < 0)
            {
                Debug.LogError($"[HomeAssistant] Refusing to call {domain}.{service}: only light and switch are allowed.");
                onDone?.Invoke(false);
                yield break;
            }

            var body = "{\"entity_id\":\"" + entityId + "\"}";
            using var req = new UnityWebRequest(baseUrl + $"api/services/{domain}/{service}", UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            req.SetRequestHeader("Content-Type", "application/json");
            Prepare(req);
            yield return req.SendWebRequest();

            bool ok = req.result == UnityWebRequest.Result.Success;
            if (!ok) LogError(req, $"{domain}.{service} {entityId}");
            onDone?.Invoke(ok);
        }

        void Prepare(UnityWebRequest req)
        {
            req.timeout = timeoutSeconds;
            req.SetRequestHeader("Authorization", "Bearer " + token);
        }

        static float lastErrorTime = -100f;

        static void LogError(UnityWebRequest req, string what)
        {
            // Polling every few seconds would flood the console when the LAN is unreachable.
            if (Time.unscaledTime - lastErrorTime < 10f) return;
            lastErrorTime = Time.unscaledTime;

            string hint = req.responseCode == 401
                ? " (token missing or invalid)"
                : req.result == UnityWebRequest.Result.ConnectionError
                    ? " (are you on the lab LAN? homeassistant.local only resolves there)"
                    : "";
            Debug.LogWarning($"[HomeAssistant] Failed to {what}: {req.responseCode} {req.error}{hint}");
        }
    }
}
