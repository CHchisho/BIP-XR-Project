using System.Collections;
using System.Linq;
using NUnit.Framework;
using SmartHouse.HomeAssistant;
using UnityEngine.TestTools;

namespace SmartHouse.Tests
{
    /// <summary>
    /// The device links end to end against the mock server: state from Home Assistant reaches the
    /// VR device, VR actions send the right service call, and nothing is echoed back.
    /// </summary>
    public class HomeAssistantLinkTests : HomeAssistantTestBase
    {
        // ---- Lights (HomeAssistantToggleLink + LampDevice) --------------------------------------------

        [UnityTest]
        public IEnumerator Light_RemoteStateAndBrightness_AreShownInVR_WithoutEcho()
        {
            Server.SetState("light.test", "on", ("brightness", "128"));
            var (lamp, _) = CreateLinkedDevice<LampDevice, HomeAssistantToggleLink>(("entityId", "light.test"));

            yield return WaitUntil(() => lamp.IsOn, "the lamp to switch on from Home Assistant");
            Assert.AreEqual(128f / 255f, lamp.Brightness, 0.01f);
            yield return Wait(0.5f);
            Assert.IsEmpty(Server.ServiceCalls, "a change that came from Home Assistant must not be sent back");
        }

        [UnityTest]
        public IEnumerator Light_PressingTheLamp_SendsTurnOnThenTurnOff()
        {
            Server.SetState("light.test", "off", ("brightness", "null"));
            var (lamp, link) = CreateLinkedDevice<LampDevice, HomeAssistantToggleLink>(("entityId", "light.test"));
            yield return WaitUntil(() => link.Available, "the first poll");

            lamp.Press();
            yield return WaitUntil(() => Server.ServiceCalls.Count == 1, "turn_on");
            var on = Server.ServiceCalls[0];
            Assert.AreEqual("/api/services/light/turn_on", on.Path);
            Assert.AreEqual("light.test", MockHomeAssistant.StringField(on.Body, "entity_id"));
            Assert.AreEqual(100, MockHomeAssistant.NumberField(on.Body, "brightness_pct"));

            yield return Wait(0.3f); // presses closer together than 0.25 s count as one
            lamp.Press();
            yield return WaitUntil(() => Server.ServiceCalls.Count == 2, "turn_off");
            Assert.AreEqual("/api/services/light/turn_off", Server.ServiceCalls[1].Path);
            Assert.AreEqual("off", Server.GetStateOf("light.test"));
        }

        [UnityTest]
        public IEnumerator Light_DimmingInVR_SendsOneBrightnessCallAfterTheDragSettles()
        {
            Server.SetState("light.test", "on", ("brightness", "255"));
            var (lamp, _) = CreateLinkedDevice<LampDevice, HomeAssistantToggleLink>(("entityId", "light.test"));
            yield return WaitUntil(() => lamp.IsOn, "the lamp to switch on from Home Assistant");

            for (int i = 0; i < 5; i++) lamp.Adjust(-0.1f); // a drag downwards, 100% -> 50%

            yield return WaitUntil(() => Server.ServiceCalls.Count >= 1, "the brightness call");
            yield return Wait(0.4f);
            var call = Server.ServiceCalls.Single();
            Assert.AreEqual("/api/services/light/turn_on", call.Path);
            Assert.AreEqual(50, MockHomeAssistant.NumberField(call.Body, "brightness_pct"));
        }

        [UnityTest]
        public IEnumerator Light_RemoteColor_IsShownInVR_WithoutEcho()
        {
            Server.SetState("light.test", "on", ("brightness", "255"), ("rgbw_color", "[0,0,255,0]"));
            var (lamp, _) = CreateLinkedDevice<LampDevice, HomeAssistantToggleLink>(("entityId", "light.test"));
            Set(lamp, "colorControl", true);

            yield return WaitUntil(() => !lamp.IsWhite, "the lamp to turn blue from Home Assistant");
            Assert.AreEqual(1f, lamp.Color.b, 0.01f);
            StringAssert.Contains("Blue", lamp.Status);
            yield return Wait(0.5f);
            Assert.IsEmpty(Server.ServiceCalls, "a color that came from Home Assistant must not be sent back");

            Server.SetState("light.test", "on", ("brightness", "255"), ("rgbw_color", "[0,0,0,255]"));
            yield return WaitUntil(() => lamp.IsWhite, "the lamp to turn white again");
        }

        [UnityTest]
        public IEnumerator Light_ChangingColorInVR_SendsRgbwColor()
        {
            Server.SetState("light.test", "on", ("brightness", "255"), ("rgbw_color", "[0,0,0,255]"));
            var (lamp, _) = CreateLinkedDevice<LampDevice, HomeAssistantToggleLink>(("entityId", "light.test"));
            Set(lamp, "colorControl", true);
            yield return WaitUntil(() => lamp.IsOn, "the lamp to switch on from Home Assistant");

            for (int i = 0; i < 4; i++) lamp.AdjustColor(0.05f); // a twist off the white band into red

            yield return WaitUntil(() => Server.ServiceCalls.Count >= 1, "the color call");
            yield return Wait(0.4f);
            var call = Server.ServiceCalls.Single();
            Assert.AreEqual("/api/services/light/turn_on", call.Path);
            StringAssert.Contains("\"rgbw_color\":[255,", call.Body);
            Assert.IsFalse(lamp.IsWhite);
        }

        [UnityTest]
        public IEnumerator Light_DimmerSwitch_DimsItsLamp()
        {
            Server.SetState("light.test", "on", ("brightness", "255"));
            var (lamp, _) = CreateLinkedDevice<LampDevice, HomeAssistantToggleLink>(("entityId", "light.test"));
            var dimmerGo = Track(new UnityEngine.GameObject("Dimmer (test)"));
            dimmerGo.SetActive(false);
            var dimmer = dimmerGo.AddComponent<DimmerSwitchDevice>();
            Set(dimmer, "lamp", lamp);
            dimmerGo.SetActive(true);
            yield return WaitUntil(() => lamp.IsOn, "the lamp to switch on from Home Assistant");

            dimmer.Adjust(-0.6f);

            yield return WaitUntil(() => Server.ServiceCalls.Count >= 1, "the brightness call");
            Assert.AreEqual(40, MockHomeAssistant.NumberField(Server.ServiceCalls.Last().Body, "brightness_pct"));
            StringAssert.Contains("40%", dimmer.Status);
        }

        [UnityTest]
        public IEnumerator Light_WallSwitch_DimsAndColorsItsLamp()
        {
            Server.SetState("light.test", "on", ("brightness", "255"), ("rgbw_color", "[0,0,0,255]"));
            var (lamp, _) = CreateLinkedDevice<LampDevice, HomeAssistantToggleLink>(("entityId", "light.test"));
            Set(lamp, "colorControl", true);
            var switchGo = Track(new UnityEngine.GameObject("Light Switch (test)"));
            switchGo.SetActive(false);
            var wallSwitch = switchGo.AddComponent<LightSwitchDevice>();
            Set(wallSwitch, "lamp", lamp);
            switchGo.SetActive(true);
            yield return WaitUntil(() => lamp.IsOn, "the lamp to switch on from Home Assistant");

            Assert.AreSame(lamp, wallSwitch.ColorTarget, "grip + twist at the switch should color its lamp");
            wallSwitch.Adjust(-0.6f);

            yield return WaitUntil(() => Server.ServiceCalls.Count >= 1, "the brightness call");
            Assert.AreEqual(40, MockHomeAssistant.NumberField(Server.ServiceCalls.Last().Body, "brightness_pct"));
            StringAssert.Contains("40%", wallSwitch.Status);
        }

        [UnityTest]
        public IEnumerator Light_UnavailableEntity_GetsNoCommand()
        {
            Server.SetState("light.test", "unavailable");
            var (lamp, link) = CreateLinkedDevice<LampDevice, HomeAssistantToggleLink>(("entityId", "light.test"));
            yield return WaitUntil(() => Server.WasRead("light.test"), "the first poll");
            yield return Wait(0.2f);

            lamp.Press();
            yield return Wait(0.5f);

            Assert.IsFalse(link.Available);
            Assert.IsEmpty(Server.ServiceCalls, "never send a command to an unavailable device");
        }

        // ---- Plugs (HomeAssistantToggleLink + PlugDevice) ----------------------------------------------

        [UnityTest]
        public IEnumerator Plug_ShowsStateAndPowerReading()
        {
            Server.SetState("switch.test", "on");
            Server.SetState("sensor.test_power", "12");
            var (plug, _) = CreateLinkedDevice<PlugDevice, HomeAssistantToggleLink>(
                ("entityId", "switch.test"), ("powerSensorId", "sensor.test_power"));

            yield return WaitUntil(() => plug.Status.Contains("W"), "the power reading");
            Assert.IsTrue(plug.IsOn);
            StringAssert.Contains("12 W", plug.Status);
        }

        [UnityTest]
        public IEnumerator Plug_PressingIt_SendsSwitchTurnOff_WithoutBrightness()
        {
            Server.SetState("switch.test", "on");
            var (plug, _) = CreateLinkedDevice<PlugDevice, HomeAssistantToggleLink>(("entityId", "switch.test"));
            yield return WaitUntil(() => plug.IsOn, "the plug to switch on from Home Assistant");

            plug.Press();

            yield return WaitUntil(() => Server.ServiceCalls.Count == 1, "turn_off");
            var call = Server.ServiceCalls[0];
            Assert.AreEqual("/api/services/switch/turn_off", call.Path);
            Assert.IsNull(MockHomeAssistant.NumberField(call.Body, "brightness_pct"));
        }

        // ---- Curtains (HomeAssistantCurtainLink + CurtainDevice) ---------------------------------------

        [UnityTest]
        public IEnumerator Curtain_RemotePosition_IsShownInVR()
        {
            Server.SetState("cover.test", "open", ("current_position", "40"));
            var (curtain, _) = CreateLinkedDevice<CurtainDevice, HomeAssistantCurtainLink>(("entityId", "cover.test"));

            yield return WaitUntil(() => curtain.Status == "Open 40%", "the curtain to reach 40% open", 6f);
            Assert.IsEmpty(Server.ServiceCalls);
        }

        [UnityTest]
        public IEnumerator Curtain_PressingIt_SendsOpenCover()
        {
            Server.SetState("cover.test", "closed", ("current_position", "0"));
            var (curtain, link) = CreateLinkedDevice<CurtainDevice, HomeAssistantCurtainLink>(("entityId", "cover.test"));
            yield return WaitUntil(() => link.Available, "the first poll");

            curtain.Press();

            yield return WaitUntil(() => Server.ServiceCalls.Count == 1, "open_cover");
            Assert.AreEqual("/api/services/cover/open_cover", Server.ServiceCalls[0].Path);
            Assert.IsTrue(curtain.IsOpen);
        }

        // ---- Locks (HomeAssistantLockLink + DoorLockDevice) --------------------------------------------

        [UnityTest]
        public IEnumerator Lock_RemoteUnlock_TurnsTheVRLock_WithoutEcho()
        {
            Server.SetState("lock.test", "unlocked");
            var (doorLock, _) = CreateLinkedDevice<DoorLockDevice, HomeAssistantLockLink>(("entityId", "lock.test"));

            yield return WaitUntil(() => !doorLock.IsLocked, "the lock to unlock from Home Assistant");
            yield return Wait(0.5f);
            Assert.IsEmpty(Server.ServiceCalls);
        }

        [UnityTest]
        public IEnumerator Lock_PressingAnUnlockedLock_SendsLock()
        {
            Server.SetState("lock.test", "unlocked");
            var (doorLock, _) = CreateLinkedDevice<DoorLockDevice, HomeAssistantLockLink>(("entityId", "lock.test"));
            yield return WaitUntil(() => !doorLock.IsLocked, "the lock to unlock from Home Assistant");

            doorLock.Press();

            yield return WaitUntil(() => Server.ServiceCalls.Count == 1, "lock/lock");
            Assert.AreEqual("/api/services/lock/lock", Server.ServiceCalls[0].Path);
            Assert.AreEqual("locked", Server.GetStateOf("lock.test"));
            Assert.IsTrue(doorLock.IsLocked);
        }

        // ---- Thermostat (HomeAssistantClimateLink + ThermostatDevice) ----------------------------------

        [UnityTest]
        public IEnumerator Thermostat_RemoteModeAndTemperatures_AreShownInVR()
        {
            Server.SetState("climate.test", "heat",
                ("current_temperature", "20.5"), ("temperature", "21.5"), ("hvac_action", "\"heating\""));
            var (thermostat, _) = CreateLinkedDevice<ThermostatDevice, HomeAssistantClimateLink>(("entityId", "climate.test"));

            yield return WaitUntil(() => UnityEngine.Mathf.Approximately(thermostat.Target, 21.5f), "the target from Home Assistant");
            Assert.IsTrue(thermostat.HeatMode);
            yield return Wait(0.5f);
            Assert.IsEmpty(Server.ServiceCalls);
        }

        [UnityTest]
        public IEnumerator Thermostat_Dragging_SendsOneSetTemperature()
        {
            Server.SetState("climate.test", "heat", ("current_temperature", "20"), ("temperature", "21.5"));
            var (thermostat, _) = CreateLinkedDevice<ThermostatDevice, HomeAssistantClimateLink>(("entityId", "climate.test"));
            yield return WaitUntil(() => UnityEngine.Mathf.Approximately(thermostat.Target, 21.5f), "the target from Home Assistant");

            thermostat.Adjust(0.0625f); // +0.5 degrees
            thermostat.Adjust(0.0625f); // +0.5 degrees

            yield return WaitUntil(() => Server.ServiceCalls.Count >= 1, "set_temperature");
            yield return Wait(0.4f);
            var call = Server.ServiceCalls.Single();
            Assert.AreEqual("/api/services/climate/set_temperature", call.Path);
            Assert.AreEqual(22.5, MockHomeAssistant.NumberField(call.Body, "temperature"));
        }

        [UnityTest]
        public IEnumerator Thermostat_Pressing_SwitchesHeatingOff()
        {
            Server.SetState("climate.test", "heat", ("current_temperature", "20"), ("temperature", "21"));
            var (thermostat, link) = CreateLinkedDevice<ThermostatDevice, HomeAssistantClimateLink>(("entityId", "climate.test"));
            yield return WaitUntil(() => link.Available, "the first poll");

            thermostat.Press();

            yield return WaitUntil(() => Server.ServiceCalls.Count == 1, "set_hvac_mode");
            var call = Server.ServiceCalls[0];
            Assert.AreEqual("/api/services/climate/set_hvac_mode", call.Path);
            Assert.AreEqual("off", MockHomeAssistant.StringField(call.Body, "hvac_mode"));
            Assert.AreEqual("off", Server.GetStateOf("climate.test"));
        }

        // ---- Speaker (HomeAssistantSpeakerLink + SpeakerDevice) ---------------------------------------

        const string Popular = "media-source://radio_browser/popular";
        const string Bbc = "media-source://radio_browser/98adecf7-2683-4408-9be7-02d3f9098eb8";

        void SetPopularStations() => Server.SetBrowseFolder(Popular,
            ("Popular stations", "media-source://radio_browser/folder", "", false),
            ("Some AAC Station", "media-source://radio_browser/11111111-1111-1111-1111-111111111111", "audio/aac", true),
            ("BBC World Service", Bbc, "audio/mpeg", true));

        [UnityTest]
        public IEnumerator Speaker_Pressing_BrowsesPopularAndPlaysTheStation()
        {
            Server.SetState("media_player.test", "off");
            SetPopularStations();
            var (speaker, link) = CreateLinkedDevice<SpeakerDevice, HomeAssistantSpeakerLink>(("entityId", "media_player.test"));
            yield return WaitUntil(() => link.Available, "the first poll");

            speaker.Press();

            yield return WaitUntil(() => Server.ServiceCalls.Count == 1, "play_media");
            var call = Server.ServiceCalls[0];
            Assert.AreEqual("/api/services/media_player/play_media", call.Path);
            Assert.AreEqual("media_player.test", MockHomeAssistant.StringField(call.Body, "entity_id"));
            Assert.AreEqual(Bbc, MockHomeAssistant.StringField(call.Body, "media_content_id"));
            Assert.AreEqual("audio/mpeg", MockHomeAssistant.StringField(call.Body, "media_content_type"));
            Assert.AreEqual("playing", Server.GetStateOf("media_player.test"));

            var browse = Server.Requests.Single(r => r.Method == "WS");
            Assert.AreEqual(Popular, MockHomeAssistant.StringField(browse.Body, "media_content_id"));
            Assert.AreEqual("music", MockHomeAssistant.StringField(browse.Body, "media_content_type"));

            yield return Wait(0.6f); // past the settle time: the poll now reads "playing" and keeps it
            Assert.IsTrue(speaker.IsPlaying);
            Assert.AreEqual(1, Server.ServiceCalls.Count, "nothing echoed back");
        }

        [UnityTest]
        public IEnumerator Speaker_PressingWhilePlaying_SendsTurnOff()
        {
            Server.SetState("media_player.test", "playing", ("volume_level", "0.3"));
            var (speaker, _) = CreateLinkedDevice<SpeakerDevice, HomeAssistantSpeakerLink>(("entityId", "media_player.test"));
            yield return WaitUntil(() => speaker.IsPlaying, "the playing state from Home Assistant");
            Assert.AreEqual(0.3f, speaker.Volume, 0.001f);

            speaker.Press();

            yield return WaitUntil(() => Server.ServiceCalls.Count == 1, "turn_off");
            Assert.AreEqual("/api/services/media_player/turn_off", Server.ServiceCalls[0].Path);
            Assert.AreEqual("off", Server.GetStateOf("media_player.test"));
            Assert.IsFalse(Server.Requests.Any(r => r.Method == "WS"), "no browse needed to stop");
        }

        [UnityTest]
        public IEnumerator Speaker_Dragging_SendsOneVolumeSet()
        {
            Server.SetState("media_player.test", "playing", ("volume_level", "0.2"));
            var (speaker, _) = CreateLinkedDevice<SpeakerDevice, HomeAssistantSpeakerLink>(
                ("entityId", "media_player.test"), ("volumeDebounce", 0.15f));
            yield return WaitUntil(() => UnityEngine.Mathf.Approximately(speaker.Volume, 0.2f), "the volume from Home Assistant");

            speaker.Adjust(0.1f);
            speaker.Adjust(0.1f);

            yield return WaitUntil(() => Server.ServiceCalls.Count >= 1, "volume_set");
            yield return Wait(0.4f);
            var call = Server.ServiceCalls.Single();
            Assert.AreEqual("/api/services/media_player/volume_set", call.Path);
            Assert.AreEqual(0.4, MockHomeAssistant.NumberField(call.Body, "volume_level"), 0.001);
        }

        [UnityTest]
        public IEnumerator Speaker_Unavailable_GetsNoCommand_AndPlaysLocally()
        {
            Server.SetState("media_player.test", "unavailable");
            SetPopularStations();
            var (speaker, link) = CreateLinkedDevice<SpeakerDevice, HomeAssistantSpeakerLink>(("entityId", "media_player.test"));
            yield return WaitUntil(() => Server.WasRead("media_player.test"), "the first poll");
            yield return Wait(0.2f);
            Assert.IsFalse(link.Available);

            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning,
                new System.Text.RegularExpressions.Regex("media_player.test is unavailable"));
            speaker.Press();
            yield return Wait(0.4f);

            Assert.IsEmpty(Server.ServiceCalls);
            Assert.IsTrue(speaker.IsPlaying);
            var source = speaker.GetComponentInChildren<UnityEngine.AudioSource>();
            Assert.IsTrue(source.isPlaying, "the VR model plays its own track when the real speaker can't");
        }
    }
}
