using System;
using System.Collections;
using NUnit.Framework;
using SmartHouse.HomeAssistant;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.TestTools;

namespace SmartHouse.Tests
{
    /// <summary>
    /// Read-only checks against the REAL lab Home Assistant: the API answers, the token is accepted,
    /// and every approved device from ha-api-v2.md exists. Never sends a command.
    /// Skipped (Ignored) when there is no token or the lab LAN can't be reached.
    /// Run only these with the test filter category "LiveHomeAssistant".
    /// </summary>
    [Category("LiveHomeAssistant")]
    public class LiveHomeAssistantTests
    {
        // The approved list in ha-api-v2.md (13 Oct 2026).
        static readonly string[] ApprovedEntities =
        {
            "light.switch",
            "light.aqara_dimmer_switch_h2_2",
            "light.rgbw_controller_2",
            "switch.aqara_lumi_plug_aeu001_switch_2",
            "switch.metered_wall_plug_switch",
            "cover.aqara_curtain_driver_e1",
            "cover.aqara_roller_shade_driver_e1",
            "lock.aqara_smart_lock_u200_lite",
            "lock.z_wave_controlled_door_lock_with_bluetooth_smart",
            "climate.thermostat",
            "sensor.aqara_dimmer_switch_h2_power",
        };

        string baseUrl;
        string token;

        [SetUp]
        public void FindConnection()
        {
            baseUrl = Environment.GetEnvironmentVariable("HA_URL");
            if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = "http://homeassistant.local:8123/";
            if (!baseUrl.EndsWith("/")) baseUrl += "/";

            token = Environment.GetEnvironmentVariable("HA_TOKEN");
            if (string.IsNullOrWhiteSpace(token)) token = Resources.Load<TextAsset>("ha_token")?.text;
            token = token?.Trim();
            if (string.IsNullOrEmpty(token))
                Assert.Ignore("No Home Assistant token (HA_TOKEN or Assets/Resources/ha_token.txt); live tests skipped.");
        }

        IEnumerator Get(string path, Action<UnityWebRequest> check)
        {
            using var req = UnityWebRequest.Get(baseUrl + path);
            req.timeout = 5;
            req.SetRequestHeader("Authorization", "Bearer " + token);
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.ConnectionError)
                Assert.Ignore($"Home Assistant not reachable at {baseUrl} ({req.error}); are you on the lab LAN?");
            check(req);
        }

        [UnityTest]
        public IEnumerator Api_IsRunning_AndAcceptsTheToken()
        {
            yield return Get("api/", req =>
            {
                Assert.AreNotEqual(401, req.responseCode, "the token was rejected; create a new one with Tools/Get-HaToken.ps1");
                Assert.AreEqual(200, req.responseCode, req.downloadHandler.text);
                StringAssert.Contains("API running", req.downloadHandler.text);
            });
        }

        [UnityTest]
        public IEnumerator ApprovedEntity_Exists([ValueSource(nameof(ApprovedEntities))] string entityId)
        {
            yield return Get("api/states/" + entityId, req =>
            {
                Assert.AreEqual(200, req.responseCode, $"{entityId}: {req.downloadHandler.text}");
                var state = JsonUtility.FromJson<HomeAssistantClient.EntityState>(req.downloadHandler.text);
                Assert.AreEqual(entityId, state.entity_id);
                Assert.IsNotEmpty(state.state);
                if (state.state == "unavailable")
                    Debug.LogWarning($"[LiveHomeAssistant] {entityId} exists but is unavailable (device not responding).");
            });
        }
    }
}
