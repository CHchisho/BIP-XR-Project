using System.Collections;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SmartHouse.HomeAssistant;
using UnityEngine;
using UnityEngine.TestTools;

namespace SmartHouse.Tests
{
    /// <summary>The REST calls of <see cref="HomeAssistantClient"/>, against the mock server.</summary>
    public class HomeAssistantClientTests : HomeAssistantTestBase
    {
        [UnityTest]
        public IEnumerator GetState_SendsBearerToken_AndParsesLightAttributes()
        {
            Server.SetState("light.test", "on", ("brightness", "128"));

            HomeAssistantClient.EntityState result = null;
            yield return Client.GetState("light.test", s => result = s);

            Assert.IsNotNull(result);
            Assert.AreEqual("light.test", result.entity_id);
            Assert.AreEqual("on", result.state);
            Assert.AreEqual(128, result.attributes.brightness);
            Assert.AreEqual("Bearer " + MockHomeAssistant.Token, Server.Requests.Last().Authorization);
        }

        [UnityTest]
        public IEnumerator GetState_ParsesCoverAndClimateAttributes()
        {
            Server.SetState("cover.test", "open", ("current_position", "40"));
            Server.SetState("climate.test", "heat",
                ("current_temperature", "20.5"), ("temperature", "21.5"), ("hvac_action", "\"heating\""));

            HomeAssistantClient.EntityState cover = null, climate = null;
            yield return Client.GetState("cover.test", s => cover = s);
            yield return Client.GetState("climate.test", s => climate = s);

            Assert.AreEqual(40, cover.attributes.current_position);
            Assert.AreEqual(20.5f, climate.attributes.current_temperature, 0.001f);
            Assert.AreEqual(21.5f, climate.attributes.temperature, 0.001f);
            Assert.AreEqual("heating", climate.attributes.hvac_action);
        }

        [UnityTest]
        public IEnumerator GetState_NullBrightnessOfALightThatIsOff_ReadsAsZero()
        {
            Server.SetState("light.test", "off", ("brightness", "null"));

            HomeAssistantClient.EntityState result = null;
            yield return Client.GetState("light.test", s => result = s);

            Assert.AreEqual("off", result.state);
            Assert.AreEqual(0, result.attributes.brightness);
        }

        [UnityTest]
        public IEnumerator GetState_UnknownEntity_ReturnsNull()
        {
            bool called = false;
            HomeAssistantClient.EntityState result = new HomeAssistantClient.EntityState();
            yield return Client.GetState("light.does_not_exist", s => { called = true; result = s; });

            Assert.IsTrue(called);
            Assert.IsNull(result);
        }

        [UnityTest]
        public IEnumerator CallService_PostsEntityIdAndExtraFields()
        {
            Server.SetState("light.test", "off");

            bool? ok = null;
            yield return Client.CallService("light", "turn_on", "light.test", r => ok = r, "\"brightness_pct\":40");

            Assert.AreEqual(true, ok);
            var call = Server.ServiceCalls.Single();
            Assert.AreEqual("/api/services/light/turn_on", call.Path);
            Assert.AreEqual("light.test", MockHomeAssistant.StringField(call.Body, "entity_id"));
            Assert.AreEqual(40, MockHomeAssistant.NumberField(call.Body, "brightness_pct"));
            Assert.AreEqual("on", Server.GetStateOf("light.test"));
        }

        [UnityTest]
        public IEnumerator CallService_RefusesForbiddenDomains_WithoutSendingAnything(
            [Values("siren", "alarm_control_panel")] string domain)
        {
            LogAssert.Expect(LogType.Error, new Regex($"Refusing to call {domain}\\."));

            bool? ok = null;
            yield return Client.CallService(domain, "turn_on", domain + ".test", r => ok = r);

            Assert.AreEqual(false, ok);
            Assert.IsEmpty(Server.Requests);
        }

        [UnityTest]
        public IEnumerator CallService_AllowsEveryApprovedDomain(
            [Values("light", "switch", "cover", "lock", "climate")] string domain)
        {
            bool? ok = null;
            yield return Client.CallService(domain, "noop", domain + ".test", r => ok = r);

            Assert.AreEqual(true, ok);
            Assert.AreEqual($"/api/services/{domain}/noop", Server.ServiceCalls.Single().Path);
        }

        [UnityTest]
        public IEnumerator RejectedToken_FailsCleanly_AndLeavesTheTokenCacheAlone()
        {
            string cache = Path.Combine(Application.persistentDataPath, "ha_token_cache.txt");
            bool cacheExisted = File.Exists(cache);
            Client.Configure(Server.Url, "wrong-token");
            Server.SetState("light.test", "on");

            HomeAssistantClient.EntityState result = new HomeAssistantClient.EntityState();
            yield return Client.GetState("light.test", s => result = s);

            Assert.IsNull(result);
            Assert.AreEqual("Bearer wrong-token", Server.Requests.Last().Authorization);
            Assert.AreEqual(cacheExisted, File.Exists(cache), "a test-configured client must not delete the real token cache");
        }
    }
}
