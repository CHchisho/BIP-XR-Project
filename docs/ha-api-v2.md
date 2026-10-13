## Network

The API exists only on the lab LAN.

- Base URL: `http://homeassistant.local:8123`
- `homeassistant.local` is mDNS. A normal DNS lookup does not find it. It does not answer from the public internet or from another Wi-Fi.
- `GET /api/` with no token returns **401**. That means the service is up.

Do not put the Wi-Fi password, the Home Assistant password, or a token in git.

## Auth

Send every `/api/` call with:

```http
Authorization: Bearer <token>
```

Create a **long-lived access token** in the Home Assistant profile: Security → Long-lived access tokens. Store it in an environment variable or a gitignored file. The UI login is not a token.

## Read and write

```http
GET /api/states/{entity_id}
POST /api/services/{domain}/{service}
Content-Type: application/json
```

Useful fields: `state`, `attributes.friendly_name`, `attributes.unit_of_measurement`.

`unavailable` means the device is not responding. Do not send a command.

HTTP 200 means Home Assistant accepted the call. The body is often an empty array. Read the entity again to see the new state.

Polling every 2–4 seconds is enough.

Do not call `siren.*` or `alarm_control_panel.*`.

## Approved devices

Checked from the lab panel. This list is `lab-panel/approved.json` on 13 Oct 2026.

| entity_id                                               | Name                 | Use                                       |
| ------------------------------------------------------- | -------------------- | ----------------------------------------- |
| `light.switch`                                          | Switch               | Light. `on` / `off`.                      |
| `light.aqara_dimmer_switch_h2_2`                        | Dimmer               | Light. `on` / `off`. Brightness optional. |
| `light.rgbw_controller_2`                               | RGBW controller      | Light. `on` / `off`, brightness, color.   |
| `switch.aqara_lumi_plug_aeu001_switch_2`                | Plug                 | Switch. `on` / `off`.                     |
| `switch.metered_wall_plug_switch`                       | Wall plug            | Switch. `on` / `off`.                     |
| `cover.aqara_curtain_driver_e1`                         | Curtain              | Cover. Open, close, stop, position.       |
| `cover.aqara_roller_shade_driver_e1`                    | Roller shade         | Cover. Open, close, stop, position.       |
| `lock.aqara_smart_lock_u200_lite`                       | Aqara lock U200 Lite | Lock. `locked` / `unlocked`.              |
| `lock.z_wave_controlled_door_lock_with_bluetooth_smart` | Z-Wave door lock     | Lock. `locked` / `unlocked`.              |
| `climate.thermostat`                                    | Kitchen thermostat   | Mode and target temperature.              |
| `sensor.aqara_dimmer_switch_h2_power`                   | Dimmer power         | Read only. `state` is watts.              |

`light.switch` is a light. `switch/turn_on` does not apply.

## Lights

```http
POST /api/services/light/turn_on
POST /api/services/light/turn_off

{"entity_id": "light.switch"}
```

Same body for `light.aqara_dimmer_switch_h2_2` and `light.rgbw_controller_2`.

Brightness is 1–100 and is sent with turn on:

```json
{ "entity_id": "light.aqara_dimmer_switch_h2_2", "brightness_pct": 40 }
```

RGBW color is four channels, 0–255, also with turn on. The fourth channel is white:

```json
{ "entity_id": "light.rgbw_controller_2", "rgbw_color": [255, 80, 0, 0] }
```

Read `state` (`on` or `off`). Brightness in the reply is `attributes.brightness`, 0–255.

## Switches

```http
POST /api/services/switch/turn_on
POST /api/services/switch/turn_off

{"entity_id": "switch.aqara_lumi_plug_aeu001_switch_2"}
```

Same calls for `switch.metered_wall_plug_switch`. Read `state`: `on` or `off`.

## Covers

```http
POST /api/services/cover/open_cover
POST /api/services/cover/close_cover
POST /api/services/cover/stop_cover
POST /api/services/cover/set_cover_position
```

```json
{ "entity_id": "cover.aqara_curtain_driver_e1" }
```

```json
{ "entity_id": "cover.aqara_roller_shade_driver_e1", "position": 50 }
```

`position` is 0–100. Read `state`: `open`, `closed`, `opening`, or `closing`. Current position is `attributes.current_position`.

## Locks

```http
POST /api/services/lock/lock
POST /api/services/lock/unlock

{"entity_id": "lock.aqara_smart_lock_u200_lite"}
```

Same calls for `lock.z_wave_controlled_door_lock_with_bluetooth_smart`. Read `state`: `locked` or `unlocked`.

## Thermostat

`state` is the mode (`heat` or `off`). Current temperature is `attributes.current_temperature`. Target is `attributes.temperature`.

```http
POST /api/services/climate/set_hvac_mode
{"entity_id": "climate.thermostat", "hvac_mode": "heat"}

POST /api/services/climate/set_temperature
{"entity_id": "climate.thermostat", "temperature": 21}
```

Allowed modes: `heat`, `off`. Temperature range 5–35, step 0.5.

## Power sensor

```http
GET /api/states/sensor.aqara_dimmer_switch_h2_power
```

No write. `state` is watts.
