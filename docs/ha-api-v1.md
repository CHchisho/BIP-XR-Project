## Network

The API exists only on the lab LAN.

- Base URL: `http://homeassistant.local:8123`
- The name `homeassistant.local` is mDNS. A normal DNS lookup does not find it. It does not answer from the public internet or from another Wi-Fi.
- `GET /api/` with no token returns **401**. That means the service is up.

Do not put the Wi-Fi password, the Home Assistant password, or a token in git.

## Auth

Send every `/api/` call with:

```http
Authorization: Bearer <token>
```

For C#, create a **long-lived access token** in the Home Assistant profile: Security → Long-lived access tokens. Store it in an environment variable or a gitignored file. The UI login is not a token. A token from the login flow below lives 30 minutes.

Login flow, if a long-lived token is not ready yet. Username is `bip_erasmus`.

1. `POST /auth/login_flow`

```json
{
  "client_id": "http://homeassistant.local:8123/",
  "handler": ["homeassistant", null],
  "redirect_uri": "http://homeassistant.local:8123/"
}
```

2. `POST /auth/login_flow/{flow_id}` with `client_id`, `username`, `password`. A successful body has `"type": "create_entry"` and a `result` code.
3. `POST /auth/token` as form data: `grant_type=authorization_code`, `code`, `client_id`. The JSON field `access_token` is the Bearer token.

## Read

```http
GET /api/states
GET /api/states/{entity_id}
```

Useful fields: `entity_id`, `state`, `attributes.friendly_name`, `attributes.unit_of_measurement`.

`on` / `off` for lights and switches. A number plus a unit for the power sensor. `unavailable` means the device is not responding; do not send a command.

Polling every 2–4 seconds is enough for v1.

## Write

```http
POST /api/services/light/turn_on
POST /api/services/light/turn_off
POST /api/services/switch/turn_on
POST /api/services/switch/turn_off
Content-Type: application/json

{"entity_id": "light.aqara_dimmer_switch_h2"}
```

HTTP 200 means Home Assistant accepted the call. Read the entity again to see the new state. The response body is often an empty array.

## Verified in the room

These four were matched to physical devices on 12 Oct 2026.

| entity_id                                | What was checked                                                         | Call                                                                                |
| ---------------------------------------- | ------------------------------------------------------------------------ | ----------------------------------------------------------------------------------- |
| `light.aqara_dimmer_switch_h2`           | The room lamp turns on and off                                           | `POST /api/services/light/turn_on` and `turn_off`. Read `state`: `on` or `off`.     |
| `switch.aqara_lumi_plug_aeu001_switch_2` | The plug switch changes state                                            | `POST /api/services/switch/turn_on` and `turn_off`.                                 |
| `switch.metered_wall_plug_switch`        | The wall-plug switch changes state                                       | `POST /api/services/switch/turn_on` and `turn_off`.                                 |
| `sensor.aqara_dimmer_switch_h2_power`    | Turning the physical Aqara Dimmer Switch H2 knob changes the Power value | Read only. `GET /api/states/sensor.aqara_dimmer_switch_h2_power`. `state` is watts. |

Do not call `lock.*`, `siren.*`, or `alarm_control_panel.*`.

## C# sketch

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;

using var http = new HttpClient
{
    BaseAddress = new Uri("http://homeassistant.local:8123/")
};
http.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", token);

var power = await http.GetFromJsonAsync<HaState>(
    "api/states/sensor.aqara_dimmer_switch_h2_power");

await http.PostAsJsonAsync(
    "api/services/light/turn_on",
    new { entity_id = "light.aqara_dimmer_switch_h2" });

sealed class HaState
{
    public string entity_id { get; set; }
    public string state { get; set; }
}
```

Same pattern for the two switches, with `api/services/switch/turn_on` and `turn_off`.
