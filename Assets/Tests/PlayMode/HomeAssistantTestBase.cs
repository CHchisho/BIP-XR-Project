using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SmartHouse.HomeAssistant;
using UnityEngine;

namespace SmartHouse.Tests
{
    /// <summary>
    /// Starts a <see cref="MockHomeAssistant"/> and a <see cref="HomeAssistantClient"/> pointed at it
    /// for every test, and cleans both up afterwards. Never talks to the real Home Assistant.
    /// </summary>
    public abstract class HomeAssistantTestBase
    {
        protected MockHomeAssistant Server;
        protected HomeAssistantClient Client;
        readonly List<GameObject> created = new List<GameObject>();

        [SetUp]
        public void StartMockHomeAssistant()
        {
            Server = new MockHomeAssistant();
            Client = CreateClient(MockHomeAssistant.Token);
        }

        [TearDown]
        public void StopMockHomeAssistant()
        {
            // Immediately, not at the end of the frame: the client is a single shared instance, and the
            // next test's client would otherwise see this one still alive and remove itself.
            foreach (var go in created)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            created.Clear();
            foreach (var leftover in UnityEngine.Object.FindObjectsByType<HomeAssistantClient>(FindObjectsInactive.Include))
                UnityEngine.Object.DestroyImmediate(leftover.gameObject);
            Server.Dispose();
        }

        /// <summary>A client configured before it wakes up, so it never looks for a real token or logs in.</summary>
        protected HomeAssistantClient CreateClient(string token)
        {
            var go = Track(new GameObject("Home Assistant Client (test)"));
            go.SetActive(false);
            var client = go.AddComponent<HomeAssistantClient>();
            client.Configure(Server.Url, token, Server.WebSocketUrl);
            go.SetActive(true);
            return client;
        }

        /// <summary>A device plus its link, configured while inactive and then switched on.</summary>
        protected (TDevice device, TLink link) CreateLinkedDevice<TDevice, TLink>(params (string field, object value)[] linkSettings)
            where TDevice : SmartDevice
            where TLink : MonoBehaviour
        {
            var go = Track(new GameObject(typeof(TDevice).Name + " (test)"));
            go.SetActive(false);
            var device = go.AddComponent<TDevice>();
            var link = go.AddComponent<TLink>();
            // Fast timings so the tests run in a second or two instead of the real 3 s poll interval.
            TrySet(link, "pollInterval", 0.1f);
            TrySet(link, "commandSettleTime", 0.3f);
            TrySet(link, "brightnessDebounce", 0.15f);
            TrySet(link, "targetDebounce", 0.15f);
            foreach (var (field, value) in linkSettings) Set(link, field, value);
            go.SetActive(true);
            return (device, link);
        }

        protected GameObject Track(GameObject go)
        {
            created.Add(go);
            return go;
        }

        protected static IEnumerator WaitUntil(Func<bool> condition, string what, float timeoutSeconds = 5f)
        {
            float end = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail($"Timed out after {timeoutSeconds} s waiting for: {what}");
                yield return null;
            }
        }

        protected static IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        protected static void Set(object target, string field, object value)
        {
            var info = FindField(target.GetType(), field) ?? throw new ArgumentException($"{target.GetType().Name} has no field '{field}'");
            info.SetValue(target, value);
        }

        static void TrySet(object target, string field, object value) => FindField(target.GetType(), field)?.SetValue(target, value);

        static FieldInfo FindField(Type type, string name)
        {
            for (; type != null; type = type.BaseType)
            {
                var info = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (info != null) return info;
            }
            return null;
        }
    }
}
