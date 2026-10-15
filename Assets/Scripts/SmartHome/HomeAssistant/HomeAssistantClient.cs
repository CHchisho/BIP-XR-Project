using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace SmartHouse.HomeAssistant
{
    /// <summary>
    /// Talks to the lab's Home Assistant REST API (see ha-api-v1.md).
    /// Settings (HA_URL, HA_TOKEN, HA_USERNAME, HA_PASSWORD) come from environment variables or,
    /// failing that, from a gitignored .env file in the project root (next to the .exe in a build).
    /// Token lookup order:
    ///   1. HA_TOKEN,
    ///   2. gitignored Assets/Resources/ha_token.txt,
    ///   3. a token this client created earlier, cached in Application.persistentDataPath,
    ///   4. otherwise a new long-lived token is created automatically by logging in with
    ///      HA_USERNAME / HA_PASSWORD, and then cached.
    /// A cached token is renewed when it is about to expire or when HA rejects it (401).
    /// </summary>
    public class HomeAssistantClient : MonoBehaviour
    {
        [SerializeField] string baseUrl = "http://homeassistant.local:8123/";
        [SerializeField] int timeoutSeconds = 5;

        [Header("Automatic token (password comes from HA_PASSWORD in .env)")]
        [Tooltip("Overridden by HA_USERNAME in the environment or .env.")]
        [SerializeField] string username = "bip_erasmus";
        [SerializeField] int tokenLifespanDays = 30;

        // Only these service domains may be called (ha-api-v2.md). siren.* and alarm_control_panel.* are off-limits.
        static readonly string[] AllowedDomains = { "light", "switch", "cover", "lock", "climate" };

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
            public Attributes attributes;
        }

        [Serializable]
        public class Attributes
        {
            /// <summary>cover.*: 0 = closed, 100 = open; -1 when the entity has no position.</summary>
            public int current_position = -1;
            /// <summary>light.*: 0..255 while on; 0 when absent.</summary>
            public int brightness;
            /// <summary>climate.*: measured temperature; NaN when absent.</summary>
            public float current_temperature = float.NaN;
            /// <summary>climate.*: target temperature; NaN when absent.</summary>
            public float temperature = float.NaN;
            /// <summary>climate.*: what it is doing right now, e.g. heating / idle / off.</summary>
            public string hvac_action;
        }

        const string CacheFileName = "ha_token_cache.txt";
        const int RenewDaysBeforeExpiry = 2;

        string token;
        bool loaded;
        bool acquiring;
        float lastAcquireAttempt = -1000f;
        bool configuredManually;

        public bool HasToken
        {
            get
            {
                EnsureLoaded();
                if (string.IsNullOrEmpty(token)) AcquireToken(); // retry (rate-limited) after a failed attempt
                return !string.IsNullOrEmpty(token);
            }
        }

        string CachePath => Path.Combine(Application.persistentDataPath, CacheFileName);

        Dictionary<string, string> dotEnv;

        /// <summary>Environment variable, else the value from .env, else null.</summary>
        string Setting(string key)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();

            if (dotEnv == null)
            {
                dotEnv = new Dictionary<string, string>();
                // Editor: project root. Windows build: the folder containing the .exe.
                var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".env"));
                try
                {
                    if (File.Exists(path))
                        foreach (var line in File.ReadAllLines(path))
                        {
                            var trimmed = line.Trim();
                            int eq = trimmed.IndexOf('=');
                            if (trimmed.Length == 0 || trimmed.StartsWith("#") || eq <= 0) continue;
                            dotEnv[trimmed.Substring(0, eq).Trim()] = trimmed.Substring(eq + 1).Trim().Trim('"');
                        }
                }
                catch (IOException e)
                {
                    Debug.LogWarning("[HomeAssistant] Could not read .env: " + e.Message);
                }
            }
            return dotEnv.TryGetValue(key, out value) && value.Length > 0 ? value : null;
        }

        /// <summary>
        /// Uses this server and token instead of looking them up (environment, .env, Resources, cache)
        /// and never creates a token automatically. For automated tests and tools.
        /// </summary>
        public void Configure(string url, string accessToken)
        {
            baseUrl = url.EndsWith("/") ? url : url + "/";
            token = accessToken;
            loaded = true;
            configuredManually = true;
            lastAcquireAttempt = float.MaxValue; // no automatic login
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

            var url = Setting("HA_URL");
            if (url != null) baseUrl = url;
            if (!baseUrl.EndsWith("/")) baseUrl += "/";

            token = Setting("HA_TOKEN");
            if (string.IsNullOrWhiteSpace(token))
            {
                var asset = Resources.Load<TextAsset>("ha_token");
                token = asset != null ? asset.text : null;
            }
            token = token?.Trim();

            if (string.IsNullOrEmpty(token)) token = ReadCachedToken();
            if (string.IsNullOrEmpty(token)) AcquireToken();
        }

        /// <summary>Token from the cache file, unless it is missing or about to expire.</summary>
        string ReadCachedToken()
        {
            try
            {
                if (!File.Exists(CachePath)) return null;
                var lines = File.ReadAllLines(CachePath);
                if (lines.Length < 2) return null;
                var expires = DateTime.Parse(lines[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                if (DateTime.UtcNow > expires.AddDays(-RenewDaysBeforeExpiry)) return null;
                return lines[0].Trim();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HomeAssistant] Could not read cached token: " + e.Message);
                return null;
            }
        }

        /// <summary>Logs in and creates a new long-lived token in the background, then caches it.</summary>
        async void AcquireToken()
        {
            if (acquiring || Time.unscaledTime - lastAcquireAttempt < 30f) return;
            lastAcquireAttempt = Time.unscaledTime;

            var user = Setting("HA_USERNAME") ?? username;
            var pass = Setting("HA_PASSWORD");
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            {
                Debug.LogWarning("[HomeAssistant] No token found and no login configured. Put HA_USERNAME and " +
                                 "HA_PASSWORD in a .env file in the project root (or set them as environment " +
                                 "variables), or create Assets/Resources/ha_token.txt.");
                return;
            }

            acquiring = true;
            try
            {
                Debug.Log($"[HomeAssistant] No valid token; logging in as {user} to create one...");
                string name = $"XR SmartHouse {SystemInfo.deviceName} {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                string created = await HomeAssistantTokenProvider.CreateLongLivedTokenAsync(
                    baseUrl, user, pass, tokenLifespanDays, name);
                if (this == null) return; // destroyed (e.g. play mode stopped) while waiting

                token = created;
                var expires = DateTime.UtcNow.AddDays(tokenLifespanDays);
                File.WriteAllLines(CachePath, new[] { token, expires.ToString("o", CultureInfo.InvariantCulture) });
                Debug.Log($"[HomeAssistant] Created long-lived token '{name}', valid until {expires.ToLocalTime():yyyy-MM-dd}. Cached in {CachePath}");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HomeAssistant] Could not create a token: " + e.Message);
            }
            finally
            {
                acquiring = false;
            }
        }

        /// <summary>Called when HA answers 401: drop the token and get a fresh one.</summary>
        void OnUnauthorized()
        {
            if (configuredManually) return; // a configured token is not ours to replace or delete
            token = null;
            try { if (File.Exists(CachePath)) File.Delete(CachePath); } catch (IOException) { }
            AcquireToken();
        }

        /// <summary>GET /api/states/{entityId}. Calls back with null on any failure.</summary>
        public IEnumerator GetState(string entityId, Action<EntityState> onResult)
        {
            EnsureLoaded();
            if (Offline) { onResult?.Invoke(null); yield break; }
            using var req = UnityWebRequest.Get(baseUrl + "api/states/" + entityId);
            Prepare(req);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                NoteConnectionError(req);
                LogError(req, "read " + entityId);
                if (req.responseCode == 401) OnUnauthorized();
                onResult?.Invoke(null);
                yield break;
            }

            onResult?.Invoke(JsonUtility.FromJson<EntityState>(req.downloadHandler.text));
        }

        /// <summary>
        /// POST /api/services/{domain}/{service} with {"entity_id": ...}.
        /// <paramref name="extraJson"/> adds fields to the body, e.g. "\"brightness_pct\":40".
        /// </summary>
        public IEnumerator CallService(string domain, string service, string entityId, Action<bool> onDone = null, string extraJson = null)
        {
            EnsureLoaded();
            if (Array.IndexOf(AllowedDomains, domain) < 0)
            {
                Debug.LogError($"[HomeAssistant] Refusing to call {domain}.{service}: only {string.Join(", ", AllowedDomains)} are allowed.");
                onDone?.Invoke(false);
                yield break;
            }

            if (Offline) { onDone?.Invoke(false); yield break; }

            var body = "{\"entity_id\":\"" + entityId + "\"" + (string.IsNullOrEmpty(extraJson) ? "" : "," + extraJson) + "}";
            using var req = new UnityWebRequest(baseUrl + $"api/services/{domain}/{service}", UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            req.SetRequestHeader("Content-Type", "application/json");
            Prepare(req);
            yield return req.SendWebRequest();

            bool ok = req.result == UnityWebRequest.Result.Success;
            if (!ok) NoteConnectionError(req);
            if (!ok) LogError(req, $"{domain}.{service} {entityId}");
            if (req.responseCode == 401) OnUnauthorized();
            onDone?.Invoke(ok);
        }

        void Prepare(UnityWebRequest req)
        {
            req.timeout = timeoutSeconds;
            req.SetRequestHeader("Authorization", "Bearer " + token);
        }

        static float lastErrorTime = -100f;

        // When Home Assistant can't be reached at all (not on the lab LAN), every device link would retry
        // every few seconds and Unity prints a "Curl error" for each attempt. Pause all requests instead.
        const float OfflineRetrySeconds = 30f;
        float offlineUntil = -1f;

        bool Offline => Time.unscaledTime < offlineUntil;

        void NoteConnectionError(UnityWebRequest req)
        {
            if (req.result != UnityWebRequest.Result.ConnectionError || Offline) return;
            offlineUntil = Time.unscaledTime + OfflineRetrySeconds;
            Debug.LogWarning($"[HomeAssistant] Can't reach {baseUrl} ({req.error}); are you on the lab LAN? " +
                             $"Pausing all Home Assistant requests for {OfflineRetrySeconds:0} s.");
        }

        static void LogError(UnityWebRequest req, string what)
        {
            // Polling every few seconds would flood the console when the LAN is unreachable.
            if (Time.unscaledTime - lastErrorTime < 10f) return;
            lastErrorTime = Time.unscaledTime;

            string hint = req.responseCode == 401
                ? " (token rejected; requesting a new one)"
                : req.result == UnityWebRequest.Result.ConnectionError
                    ? " (are you on the lab LAN? homeassistant.local only resolves there)"
                    : "";
            Debug.LogWarning($"[HomeAssistant] Failed to {what}: {req.responseCode} {req.error}{hint}");
        }
    }
}
