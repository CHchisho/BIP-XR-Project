using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace SmartHouse.Tests
{
    /// <summary>
    /// A small in-process stand-in for the Home Assistant REST API (ha-api-v2.md), on 127.0.0.1:
    /// GET /api/ and /api/states/{entity_id}, POST /api/services/{domain}/{service}.
    /// It checks the bearer token, records every request, and applies service calls to its
    /// entity states the way the real devices would, so the app's polling sees the result.
    /// </summary>
    public sealed class MockHomeAssistant : IDisposable
    {
        public const string Token = "test-token";

        public sealed class Request
        {
            public string Method;
            public string Path;
            public string Authorization;
            public string Body;
            public override string ToString() => $"{Method} {Path} {Body}";
        }

        sealed class Entity
        {
            public string State;
            public readonly Dictionary<string, string> Attributes = new Dictionary<string, string>(); // name -> JSON value
        }

        readonly HttpListener listener = new HttpListener();
        readonly Thread thread;
        readonly object gate = new object();
        readonly Dictionary<string, Entity> entities = new Dictionary<string, Entity>();
        readonly List<Request> requests = new List<Request>();

        public string Url { get; }

        public MockHomeAssistant()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            Url = $"http://127.0.0.1:{port}/";
            listener.Prefixes.Add(Url);
            listener.Start();
            thread = new Thread(Serve) { IsBackground = true, Name = "MockHomeAssistant" };
            thread.Start();
        }

        /// <summary>Sets an entity; attributes are given as raw JSON values, e.g. ("brightness", "128").</summary>
        public void SetState(string entityId, string state, params (string name, string json)[] attributes)
        {
            lock (gate)
            {
                var e = new Entity { State = state };
                foreach (var (name, json) in attributes) e.Attributes[name] = json;
                entities[entityId] = e;
            }
        }

        public string GetStateOf(string entityId)
        {
            lock (gate) return entities.TryGetValue(entityId, out var e) ? e.State : null;
        }

        public List<Request> Requests
        {
            get { lock (gate) return requests.ToList(); }
        }

        public List<Request> ServiceCalls => Requests.Where(r => r.Method == "POST").ToList();

        public bool WasRead(string entityId) => Requests.Any(r => r.Method == "GET" && r.Path == "/api/states/" + entityId);

        void Serve()
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try { context = listener.GetContext(); }
                catch (Exception) { return; } // listener stopped
                try { Handle(context); }
                catch (Exception) { /* client went away; keep serving */ }
            }
        }

        void Handle(HttpListenerContext context)
        {
            var req = context.Request;
            string body;
            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8)) body = reader.ReadToEnd();
            var record = new Request
            {
                Method = req.HttpMethod,
                Path = req.Url.AbsolutePath,
                Authorization = req.Headers["Authorization"],
                Body = body,
            };
            lock (gate) requests.Add(record);

            if (record.Authorization != "Bearer " + Token)
            {
                Reply(context, 401, "{\"message\":\"Unauthorized\"}");
                return;
            }

            if (record.Method == "GET" && record.Path == "/api/")
            {
                Reply(context, 200, "{\"message\":\"API running.\"}");
                return;
            }

            if (record.Method == "GET" && record.Path.StartsWith("/api/states/"))
            {
                string id = record.Path.Substring("/api/states/".Length);
                string json;
                lock (gate) json = entities.TryGetValue(id, out var e) ? ToJson(id, e) : null;
                if (json == null) Reply(context, 404, "{\"message\":\"Entity not found.\"}");
                else Reply(context, 200, json);
                return;
            }

            if (record.Method == "POST" && record.Path.StartsWith("/api/services/"))
            {
                var parts = record.Path.Substring("/api/services/".Length).Split('/');
                lock (gate) ApplyService(parts[0], parts.Length > 1 ? parts[1] : "", body);
                Reply(context, 200, "[]");
                return;
            }

            Reply(context, 404, "{\"message\":\"Not found\"}");
        }

        /// <summary>What the real device would do, so the next poll reports the new state.</summary>
        void ApplyService(string domain, string service, string body)
        {
            string id = StringField(body, "entity_id");
            if (id == null || !entities.TryGetValue(id, out var e)) return;

            switch (domain + "." + service)
            {
                case "light.turn_on":
                    e.State = "on";
                    var pct = NumberField(body, "brightness_pct");
                    if (pct != null) e.Attributes["brightness"] = Mathf255(pct.Value);
                    else if (!e.Attributes.ContainsKey("brightness") || e.Attributes["brightness"] == "null") e.Attributes["brightness"] = "255";
                    break;
                case "light.turn_off":
                    e.State = "off";
                    e.Attributes["brightness"] = "null";
                    break;
                case "switch.turn_on": e.State = "on"; break;
                case "switch.turn_off": e.State = "off"; break;
                case "cover.open_cover": e.State = "open"; e.Attributes["current_position"] = "100"; break;
                case "cover.close_cover": e.State = "closed"; e.Attributes["current_position"] = "0"; break;
                case "lock.lock": e.State = "locked"; break;
                case "lock.unlock": e.State = "unlocked"; break;
                case "climate.set_hvac_mode": e.State = StringField(body, "hvac_mode") ?? e.State; break;
                case "climate.set_temperature":
                    var t = NumberField(body, "temperature");
                    if (t != null) e.Attributes["temperature"] = t.Value.ToString(CultureInfo.InvariantCulture);
                    break;
            }
        }

        static string Mathf255(double pct) => ((int)Math.Round(pct * 255.0 / 100.0)).ToString(CultureInfo.InvariantCulture);

        static string ToJson(string id, Entity e)
        {
            var attrs = string.Join(",", e.Attributes.Select(kv => $"\"{kv.Key}\":{kv.Value}"));
            return $"{{\"entity_id\":\"{id}\",\"state\":\"{e.State}\",\"attributes\":{{{attrs}}}}}";
        }

        public static string StringField(string json, string name)
        {
            var m = Regex.Match(json ?? "", $"\"{Regex.Escape(name)}\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        public static double? NumberField(string json, string name)
        {
            var m = Regex.Match(json ?? "", $"\"{Regex.Escape(name)}\"\\s*:\\s*(-?[0-9.]+)");
            return m.Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : (double?)null;
        }

        static void Reply(HttpListenerContext context, int status, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.OutputStream.Close();
        }

        public void Dispose()
        {
            try { listener.Stop(); listener.Close(); } catch (ObjectDisposedException) { }
            thread.Join(1000);
        }
    }
}
