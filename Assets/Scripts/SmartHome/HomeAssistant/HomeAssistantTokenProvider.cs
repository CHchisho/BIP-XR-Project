using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SmartHouse.HomeAssistant
{
    /// <summary>
    /// Creates a Home Assistant long-lived access token from a username and password, so nobody
    /// has to make one by hand in the HA profile page:
    /// 1. the login flow (/auth/login_flow + /auth/token) gives a 30-minute access token,
    /// 2. the WebSocket command auth/long_lived_access_token turns that into a long-lived one.
    /// Accounts with MFA enabled are not supported.
    /// </summary>
    public static class HomeAssistantTokenProvider
    {
        [Serializable] class FlowStep { public string flow_id; public string type; public string step_id; public string result; }
        [Serializable] class TokenResponse { public string access_token; }
        [Serializable] class WsError { public string code; public string message; }
        [Serializable] class WsMessage { public string type; public bool success; public string result; public string message; public WsError error; }

        /// <param name="baseUrl">e.g. http://homeassistant.local:8123/ (with trailing slash)</param>
        /// <param name="clientName">Shown in the HA profile token list; must be unique per user.</param>
        public static async Task<string> CreateLongLivedTokenAsync(string baseUrl, string username, string password,
            int lifespanDays, string clientName, CancellationToken ct = default)
        {
            string accessToken = await LoginAsync(baseUrl, username, password, ct);
            return await RequestLongLivedTokenAsync(baseUrl, accessToken, lifespanDays, clientName, ct);
        }

        static async Task<string> LoginAsync(string baseUrl, string username, string password, CancellationToken ct)
        {
            using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(10) };

            var flow = await PostJson<FlowStep>(http, "auth/login_flow",
                $"{{\"client_id\":{Quote(baseUrl)},\"handler\":[\"homeassistant\",null],\"redirect_uri\":{Quote(baseUrl)}}}", ct);

            var step = await PostJson<FlowStep>(http, "auth/login_flow/" + flow.flow_id,
                $"{{\"client_id\":{Quote(baseUrl)},\"username\":{Quote(username)},\"password\":{Quote(password)}}}", ct);

            if (step.type != "create_entry")
                throw new Exception(step.step_id == "mfa"
                    ? $"Login for '{username}' needs MFA, which is not supported."
                    : $"Login for '{username}' failed: wrong username or password.");

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = step.result,
                ["client_id"] = baseUrl,
            });
            using var response = await http.PostAsync("auth/token", form, ct);
            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new Exception($"auth/token returned {(int)response.StatusCode}: {body}");
            return JsonUtility.FromJson<TokenResponse>(body).access_token;
        }

        static async Task<string> RequestLongLivedTokenAsync(string baseUrl, string accessToken, int lifespanDays,
            string clientName, CancellationToken ct)
        {
            var wsUrl = new Uri(baseUrl.Replace("https://", "wss://").Replace("http://", "ws://") + "api/websocket");
            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(wsUrl, ct);
            try
            {
                await Receive(ws, ct); // auth_required
                await Send(ws, $"{{\"type\":\"auth\",\"access_token\":{Quote(accessToken)}}}", ct);
                var hello = await Receive(ws, ct);
                if (hello.type != "auth_ok") throw new Exception("WebSocket auth failed: " + hello.message);

                await Send(ws, $"{{\"id\":1,\"type\":\"auth/long_lived_access_token\",\"client_name\":{Quote(clientName)},\"lifespan\":{lifespanDays}}}", ct);
                var reply = await Receive(ws, ct);
                if (!reply.success || string.IsNullOrEmpty(reply.result))
                    throw new Exception("Could not create long-lived token: " + reply.error?.message);
                return reply.result;
            }
            finally
            {
                if (ws.State == WebSocketState.Open)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
        }

        static async Task<T> PostJson<T>(HttpClient http, string path, string json, CancellationToken ct)
        {
            using var response = await http.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"), ct);
            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new Exception($"{path} returned {(int)response.StatusCode}: {body}");
            return JsonUtility.FromJson<T>(body);
        }

        static Task Send(ClientWebSocket ws, string json, CancellationToken ct) =>
            ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, ct);

        static async Task<WsMessage> Receive(ClientWebSocket ws, CancellationToken ct)
        {
            var buffer = new byte[16384];
            var text = new StringBuilder();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close) throw new Exception("Home Assistant closed the WebSocket.");
                text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            } while (!result.EndOfMessage);
            return JsonUtility.FromJson<WsMessage>(text.ToString());
        }

        /// <summary>JSON string literal, escaped.</summary>
        static string Quote(string value)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in value ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    default:
                        if (c < 0x20) sb.Append($"\\u{(int)c:x4}");
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
