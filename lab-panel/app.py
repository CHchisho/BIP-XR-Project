"""Local click-panel for the KNX lab Home Assistant.

The browser only talks to this process. Home Assistant is called from here,
so CORS does not apply. Bind to 127.0.0.1. Credentials stay in memory.
Approved entity ids are stored in approved.json next to this file.

Service names match the Home Assistant UI: lock/unlock, open_cover, and so on.
Sirens and alarm panels are left out.
"""

from __future__ import annotations

import asyncio
import colorsys
import json
import re
import time
import urllib.parse
from pathlib import Path

import httpx
import websockets
from fastapi import FastAPI, HTTPException
from fastapi.responses import FileResponse, Response, StreamingResponse
from pydantic import BaseModel, Field

ROOT = Path(__file__).parent
APPROVED_PATH = ROOT / "approved.json"
ENTITY_ID = re.compile(r"^[a-z0-9_]+\.[a-z0-9_]+$")
BLOCKED_DOMAINS = {"siren", "alarm_control_panel"}
BLOCKED_CLASSES = {"smoke", "gas", "carbon_monoxide", "safety"}
SKIP_NAME_PARTS = ("pairing", "identify")
HIDDEN_DOMAINS = BLOCKED_DOMAINS | {
    "update",
    "conversation",
    "backup",
    "tts",
    "stt",
    "todo",
    "calendar",
    "zone",
    "persistent_notification",
}
COMPACT_DOMAINS = {"sensor", "binary_sensor"}
# Same bit flags the Home Assistant UI uses.
LOCK_OPEN = 1
COVER_SET_POSITION = 4
COVER_STOP = 8
VALVE_SET_POSITION = 4
VALVE_STOP = 8
FAN_SET_SPEED = 1
MEDIA_PAUSE = 1
MEDIA_TURN_ON = 128
MEDIA_TURN_OFF = 256
MEDIA_STOP = 4096
MEDIA_PLAY = 16384
MEDIA_VOLUME_SET = 4
VACUUM_TURN_ON = 1
VACUUM_TURN_OFF = 2
VACUUM_PAUSE = 4
VACUUM_STOP = 8
VACUUM_RETURN = 16
VACUUM_START = 8192
BRIGHTNESS_MODES = {"brightness", "color_temp", "hs", "xy", "rgb", "rgbw", "rgbww", "white"}
COLOR_MODES = {"hs", "xy", "rgb", "rgbw", "rgbww"}

DOMAIN_SERVICES = {
    "light": {"turn_on", "turn_off"},
    "switch": {"turn_on", "turn_off"},
    "fan": {"turn_on", "turn_off", "set_percentage"},
    "input_boolean": {"turn_on", "turn_off"},
    "remote": {"turn_on", "turn_off"},
    "humidifier": {"turn_on", "turn_off", "set_humidity"},
    "lock": {"lock", "unlock", "open"},
    "cover": {"open_cover", "close_cover", "stop_cover", "set_cover_position"},
    "valve": {"open_valve", "close_valve", "stop_valve", "set_valve_position"},
    "climate": {"set_hvac_mode", "set_temperature", "turn_on", "turn_off"},
    "water_heater": {"set_operation_mode", "set_temperature", "turn_on", "turn_off"},
    "media_player": {"turn_on", "turn_off", "media_play", "media_pause", "media_stop", "select_source", "volume_set", "play_media"},
    "vacuum": {"start", "stop", "pause", "return_to_base", "turn_on", "turn_off"},
    "button": {"press"},
    "input_button": {"press"},
    "scene": {"turn_on"},
    "script": {"turn_on"},
    "automation": {"turn_on", "turn_off", "trigger"},
    "number": {"set_value"},
    "input_number": {"set_value"},
    "select": {"select_option"},
    "input_select": {"select_option"},
}
SERVICE_PARAMS = {
    "turn_on": {"brightness_pct"},
    "turn_off": set(),
    "lock": set(),
    "unlock": set(),
    "open": set(),
    "open_cover": set(),
    "close_cover": set(),
    "stop_cover": set(),
    "set_cover_position": {"position"},
    "open_valve": set(),
    "close_valve": set(),
    "stop_valve": set(),
    "set_valve_position": {"position"},
    "set_hvac_mode": {"hvac_mode"},
    "set_temperature": {"temperature"},
    "set_humidity": {"humidity"},
    "set_operation_mode": {"operation_mode"},
    "set_percentage": {"percentage"},
    "set_value": {"value"},
    "select_option": {"option"},
    "select_source": {"source"},
    "press": set(),
    "start": set(),
    "stop": set(),
    "pause": set(),
    "return_to_base": set(),
    "media_play": set(),
    "media_pause": set(),
    "media_stop": set(),
    "volume_set": {"volume_pct"},
    "play_media": {"media_content_id", "media_content_type"},
    "trigger": set(),
}
GROUP_ORDER = [
    "lock",
    "light",
    "switch",
    "cover",
    "valve",
    "climate",
    "water_heater",
    "fan",
    "humidifier",
    "media_player",
    "vacuum",
    "remote",
    "button",
    "input_button",
    "input_boolean",
    "scene",
    "script",
    "automation",
    "number",
    "input_number",
    "select",
    "input_select",
    "binary_sensor",
    "sensor",
    "device_tracker",
    "person",
    "camera",
    "weather",
    "sun",
]
GROUP_TITLES = {
    "lock": "Locks",
    "light": "Lights",
    "switch": "Switches",
    "cover": "Covers",
    "valve": "Valves",
    "climate": "Climate",
    "water_heater": "Water heaters",
    "fan": "Fans",
    "humidifier": "Humidifiers",
    "media_player": "Media players",
    "vacuum": "Vacuums",
    "remote": "Remotes",
    "button": "Buttons",
    "input_button": "Input buttons",
    "input_boolean": "Input booleans",
    "scene": "Scenes",
    "script": "Scripts",
    "automation": "Automations",
    "number": "Numbers",
    "input_number": "Input numbers",
    "select": "Selects",
    "input_select": "Input selects",
    "binary_sensor": "Binary sensors",
    "sensor": "Sensors",
    "device_tracker": "Device trackers",
    "person": "People",
    "camera": "Cameras",
    "weather": "Weather",
    "sun": "Sun",
}
# Speaker on the Home Assistant dashboard, Audio section, Browse media.
RADIO_ENTITIES = {"media_player.famnest2"}
RADIO_ID = re.compile(r"media-source://radio_browser/[A-Za-z0-9\-]+")
RADIO_TYPES = {"music", "audio/mpeg", "audio/aac", "audio/ogg", "audio/flac"}
_radio_cache: dict[str, tuple[float, list[dict]]] = {}

DEFAULT_APPROVED = {
    "light.aqara_dimmer_switch_h2": "Room lamp. On and off confirmed.",
    "switch.aqara_lumi_plug_aeu001_switch_2": "Plug. State change confirmed.",
    "switch.metered_wall_plug_switch": "Wall plug. State change confirmed.",
    "sensor.aqara_dimmer_switch_h2_power": "Dimmer knob. Power (W) changed when turned. Read only.",
    "climate.thermostat": "Kitchen thermostat. Mode and target temperature checked on the device.",
}

app = FastAPI(title="BIP lab panel")


class Session:
    def __init__(self) -> None:
        self.base_url = ""
        self.client_id = ""
        self.access_token = ""
        self.refresh_token = ""
        self.expires_at = 0.0

    @property
    def logged_in(self) -> bool:
        return bool(self.access_token and self.base_url)

    def clear(self) -> None:
        self.__init__()


session = Session()


def _load_approved() -> dict[str, str]:
    if not APPROVED_PATH.exists():
        APPROVED_PATH.write_text(json.dumps(DEFAULT_APPROVED, indent=2) + "\n", encoding="utf-8")
        return dict(DEFAULT_APPROVED)
    raw = json.loads(APPROVED_PATH.read_text(encoding="utf-8"))
    if not isinstance(raw, dict):
        raise RuntimeError("approved.json must be an object of entity_id to note")
    return {str(key): str(value) for key, value in raw.items()}


def _save_approved() -> None:
    APPROVED_PATH.write_text(json.dumps(approved, indent=2) + "\n", encoding="utf-8")


approved = _load_approved()


class LoginBody(BaseModel):
    base_url: str = "http://homeassistant.local:8123"
    username: str = ""
    password: str = ""
    token: str = ""


class EntityBody(BaseModel):
    entity_id: str = Field(min_length=3)


class ApproveBody(BaseModel):
    entity_id: str = Field(min_length=3)
    note: str = "Checked from the panel."


class CallBody(BaseModel):
    entity_id: str = Field(min_length=3)
    service: str = Field(min_length=2, max_length=40)
    data: dict = Field(default_factory=dict)


class WebRtcOfferBody(BaseModel):
    entity_id: str = Field(min_length=3)
    offer: str = Field(min_length=10)


class WebRtcCandidateBody(BaseModel):
    entity_id: str = Field(min_length=3)
    session_id: str = Field(min_length=4)
    candidate: dict


def _base_url(raw: str) -> str:
    url = raw.strip().rstrip("/")
    if not url.startswith(("http://", "https://")):
        raise HTTPException(400, "Address must start with http:// or https://")
    return url


def _domain(entity_id: str) -> str:
    if "." not in entity_id:
        return ""
    return entity_id.split(".", 1)[0]


def _entity_name(entity_id: str) -> str:
    return entity_id.split(".", 1)[1]


def _require_login() -> None:
    if not session.logged_in:
        raise HTTPException(401, "Not signed in")


def _attrs(state: dict) -> dict:
    raw = state.get("attributes") or {}
    return raw if isinstance(raw, dict) else {}


def _modes(attrs: dict) -> set[str]:
    raw = attrs.get("supported_color_modes") or []
    if isinstance(raw, str):
        raw = [raw]
    return {str(mode) for mode in raw}


def _clamp_byte(value, default: int = 0) -> int:
    number = _num(value, default)
    if number is None:
        number = default
    return max(0, min(255, int(round(number))))


def _hex_color(red, green, blue) -> str:
    return f"#{_clamp_byte(red):02x}{_clamp_byte(green):02x}{_clamp_byte(blue):02x}"


def _rgb_to_hs(red: int, green: int, blue: int) -> list[float]:
    hue, saturation, _value = colorsys.rgb_to_hsv(red / 255, green / 255, blue / 255)
    return [round(hue * 360, 3), round(saturation * 100, 3)]


def _color_now(attrs: dict) -> tuple[str, int | None]:
    rgbw = attrs.get("rgbw_color")
    if isinstance(rgbw, list) and len(rgbw) >= 4:
        return _hex_color(*rgbw[:3]), _clamp_byte(rgbw[3])
    rgbww = attrs.get("rgbww_color")
    if isinstance(rgbww, list) and len(rgbww) >= 5:
        return _hex_color(*rgbww[:3]), _clamp_byte(rgbww[3])
    rgb = attrs.get("rgb_color")
    if isinstance(rgb, list) and len(rgb) >= 3:
        return _hex_color(*rgb[:3]), None
    hs = attrs.get("hs_color")
    if isinstance(hs, list) and len(hs) >= 2:
        red, green, blue = colorsys.hsv_to_rgb((float(hs[0]) % 360) / 360, float(hs[1]) / 100, 1)
        return _hex_color(red * 255, green * 255, blue * 255), None
    return "#ffffff", None


def _bits(state: dict) -> int:
    raw = _attrs(state).get("supported_features") or 0
    try:
        return int(raw)
    except (TypeError, ValueError):
        return 0


def _num(value, default=None):
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


def _button(label: str, service: str) -> dict:
    return {"type": "button", "label": label, "service": service}


def _range(label: str, service: str, param: str, value, low, high, step) -> dict:
    current = _num(value, low)
    current = min(high, max(low, current))
    return {
        "type": "range",
        "label": label,
        "service": service,
        "param": param,
        "min": low,
        "max": high,
        "step": step,
        "value": current,
    }


def _select(label: str, service: str, param: str, options: list, value: str) -> dict:
    clean = [str(option) for option in options if option is not None]
    return {
        "type": "select",
        "label": label,
        "service": service,
        "param": param,
        "options": clean,
        "value": value if value in clean else (clean[0] if clean else ""),
    }


def _visible(state: dict) -> bool:
    entity_id = state.get("entity_id") or ""
    if not ENTITY_ID.fullmatch(entity_id):
        return False
    domain = _domain(entity_id)
    if domain in HIDDEN_DOMAINS:
        return False
    attrs = _attrs(state)
    if attrs.get("entity_category") == "config" and entity_id not in approved:
        return False
    if any(part in entity_id for part in SKIP_NAME_PARTS) and entity_id not in approved:
        return False
    device_class = attrs.get("device_class") or ""
    if device_class in BLOCKED_CLASSES:
        return False
    return True


def _controls(state: dict) -> list[dict]:
    domain = _domain(state["entity_id"])
    attrs = _attrs(state)
    features = _bits(state)
    controls: list[dict] = []

    if domain in {"light", "switch", "input_boolean", "remote"}:
        controls += [_button("Turn on", "turn_on"), _button("Turn off", "turn_off")]
    elif domain == "fan":
        controls += [_button("Turn on", "turn_on"), _button("Turn off", "turn_off")]
        if features & FAN_SET_SPEED or attrs.get("percentage") is not None:
            controls.append(_range("Speed", "set_percentage", "percentage", attrs.get("percentage") or 0, 0, 100, 1))
    elif domain == "lock":
        controls += [_button("Lock", "lock"), _button("Unlock", "unlock")]
        if features & LOCK_OPEN:
            controls.append(_button("Open", "open"))
    elif domain == "cover":
        controls += [_button("Open", "open_cover"), _button("Close", "close_cover")]
        if features & COVER_STOP:
            controls.append(_button("Stop", "stop_cover"))
        if features & COVER_SET_POSITION or attrs.get("current_position") is not None:
            controls.append(_range("Position", "set_cover_position", "position", attrs.get("current_position") or 0, 0, 100, 1))
    elif domain == "valve":
        controls += [_button("Open", "open_valve"), _button("Close", "close_valve")]
        if features & VALVE_STOP:
            controls.append(_button("Stop", "stop_valve"))
        if features & VALVE_SET_POSITION or attrs.get("current_position") is not None:
            controls.append(_range("Position", "set_valve_position", "position", attrs.get("current_position") or 0, 0, 100, 1))
    elif domain == "climate":
        modes = attrs.get("hvac_modes") or []
        if modes:
            controls.append(_select("Mode", "set_hvac_mode", "hvac_mode", modes, state.get("state") or ""))
        if attrs.get("temperature") is not None or attrs.get("min_temp") is not None:
            low = _num(attrs.get("min_temp"), 5)
            high = _num(attrs.get("max_temp"), 35)
            controls.append(_range("Temperature", "set_temperature", "temperature", attrs.get("temperature") or low, low, high, _num(attrs.get("target_temp_step"), 0.5)))
    elif domain == "water_heater":
        modes = attrs.get("operation_list") or []
        if modes:
            controls.append(_select("Mode", "set_operation_mode", "operation_mode", modes, attrs.get("operation_mode") or ""))
        if attrs.get("temperature") is not None or attrs.get("min_temp") is not None:
            low = _num(attrs.get("min_temp"), 30)
            high = _num(attrs.get("max_temp"), 60)
            controls.append(_range("Temperature", "set_temperature", "temperature", attrs.get("temperature") or low, low, high, 1))
    elif domain == "humidifier":
        controls += [_button("Turn on", "turn_on"), _button("Turn off", "turn_off")]
        low = _num(attrs.get("min_humidity"), 0)
        high = _num(attrs.get("max_humidity"), 100)
        controls.append(_range("Humidity", "set_humidity", "humidity", attrs.get("humidity") or low, low, high, 1))
    elif domain == "media_player":
        if not features or features & MEDIA_TURN_ON:
            controls.append(_button("Turn on", "turn_on"))
        if not features or features & MEDIA_TURN_OFF:
            controls.append(_button("Turn off", "turn_off"))
        if not features or features & MEDIA_PLAY:
            controls.append(_button("Play", "media_play"))
        if not features or features & MEDIA_PAUSE:
            controls.append(_button("Pause", "media_pause"))
        if features & MEDIA_STOP:
            controls.append(_button("Stop", "media_stop"))
        if attrs.get("source_list"):
            controls.append(_select("Source", "select_source", "source", attrs["source_list"], attrs.get("source") or ""))
        if features & MEDIA_VOLUME_SET or attrs.get("volume_level") is not None:
            percent = round(float(attrs.get("volume_level") or 0) * 100)
            controls.append(_range("Volume", "volume_set", "volume_pct", percent, 0, 100, 1))
    elif domain == "vacuum":
        if not features or features & VACUUM_START:
            controls.append(_button("Start", "start"))
        if features & VACUUM_PAUSE:
            controls.append(_button("Pause", "pause"))
        if not features or features & VACUUM_STOP:
            controls.append(_button("Stop", "stop"))
        if not features or features & VACUUM_RETURN:
            controls.append(_button("Return", "return_to_base"))
        if features & VACUUM_TURN_ON:
            controls.append(_button("Turn on", "turn_on"))
        if features & VACUUM_TURN_OFF:
            controls.append(_button("Turn off", "turn_off"))
    elif domain in {"button", "input_button"}:
        controls.append(_button("Press", "press"))
    elif domain == "scene":
        controls.append(_button("Activate", "turn_on"))
    elif domain == "script":
        controls.append(_button("Run", "turn_on"))
    elif domain == "automation":
        controls += [_button("Enable", "turn_on"), _button("Disable", "turn_off"), _button("Run", "trigger")]
    elif domain in {"number", "input_number"}:
        low = _num(attrs.get("min"), 0)
        high = _num(attrs.get("max"), 100)
        step = _num(attrs.get("step"), 1)
        controls.append(_range("Value", "set_value", "value", state.get("state"), low, high, step))
    elif domain in {"select", "input_select"}:
        options = attrs.get("options") or []
        if options:
            controls.append(_select("Option", "select_option", "option", options, state.get("state") or ""))

    if domain == "light":
        modes = _modes(attrs)
        if modes & BRIGHTNESS_MODES or attrs.get("brightness") is not None:
            percent = 0
            if attrs.get("brightness") is not None:
                percent = round(float(attrs["brightness"]) / 255 * 100)
            controls.append(_range("Brightness", "turn_on", "brightness_pct", percent, 1, 100, 1))
        if modes & COLOR_MODES:
            hex_color, white = _color_now(attrs)
            has_white = bool(modes & {"rgbw", "rgbww"})
            controls.append(
                {
                    "type": "color",
                    "label": "Color",
                    "service": "turn_on",
                    "value": hex_color,
                    "white": white if white is not None else (0 if has_white else None),
                }
            )
        if "color_temp" in modes and attrs.get("min_color_temp_kelvin") and attrs.get("max_color_temp_kelvin"):
            controls.append(
                _range(
                    "Color temperature",
                    "turn_on",
                    "color_temp_kelvin",
                    attrs.get("color_temp_kelvin") or attrs["min_color_temp_kelvin"],
                    float(attrs["min_color_temp_kelvin"]),
                    float(attrs["max_color_temp_kelvin"]),
                    50,
                )
            )
    return controls


def _detail(state: dict) -> str:
    attrs = _attrs(state)
    parts = []
    if attrs.get("current_temperature") is not None:
        parts.append(f"{attrs['current_temperature']}°")
    if attrs.get("brightness") is not None:
        parts.append(f"{round(float(attrs['brightness']) / 255 * 100)}%")
    if _modes(attrs) & COLOR_MODES:
        parts.append(_color_now(attrs)[0])
    if attrs.get("current_position") is not None:
        parts.append(f"{attrs['current_position']}%")
    if str(state.get("entity_id", "")).startswith("media_player.") and attrs.get("volume_level") is not None:
        parts.append(f"volume {round(float(attrs['volume_level']) * 100)}%")
    if attrs.get("media_title"):
        parts.append(str(attrs["media_title"]))
    return " · ".join(parts)


def _item(state: dict) -> dict:
    entity_id = state["entity_id"]
    attrs = _attrs(state)
    return {
        "entity_id": entity_id,
        "domain": _domain(entity_id),
        "name": attrs.get("friendly_name") or entity_id,
        "state": state.get("state") or "",
        "unit": attrs.get("unit_of_measurement") or "",
        "detail": _detail(state),
        "verified": entity_id in approved,
        "note": approved.get(entity_id, ""),
        "radio": entity_id in RADIO_ENTITIES,
        "controls": _controls(state),
    }


def _checked_data(state: dict, service: str, data: dict) -> dict:
    allowed = SERVICE_PARAMS.get(service, set())
    # Color temperature is a second optional argument of light.turn_on.
    if service == "turn_on" and _domain(state["entity_id"]) == "light":
        allowed = allowed | {"color_temp_kelvin", "rgb", "white"}
    unknown = set(data) - allowed
    if unknown:
        raise HTTPException(400, "Unexpected service data")
    attrs = _attrs(state)
    payload = {}
    for key, value in data.items():
        if key in {"rgb", "white"}:
            continue
        if key in {"brightness_pct", "percentage", "position", "humidity"}:
            number = _num(value)
            if number is None:
                raise HTTPException(400, f"{key} must be a number")
            payload[key] = int(round(number))
        elif key in {"temperature", "value", "color_temp_kelvin"}:
            number = _num(value)
            if number is None:
                raise HTTPException(400, f"{key} must be a number")
            payload[key] = number
        elif key == "hvac_mode":
            mode = str(value)
            if mode not in (attrs.get("hvac_modes") or []):
                raise HTTPException(400, "That mode is not offered by the device")
            payload[key] = mode
        elif key == "operation_mode":
            mode = str(value)
            if mode not in (attrs.get("operation_list") or []):
                raise HTTPException(400, "That mode is not offered by the device")
            payload[key] = mode
        elif key == "option":
            option = str(value)
            if option not in [str(item) for item in (attrs.get("options") or [])]:
                raise HTTPException(400, "That option is not offered by the device")
            payload[key] = option
        elif key == "source":
            source = str(value)
            if source not in [str(item) for item in (attrs.get("source_list") or [])]:
                raise HTTPException(400, "That source is not offered by the device")
            payload[key] = source
        elif key == "volume_pct":
            number = _num(value)
            if number is None:
                raise HTTPException(400, "Volume must be a number")
            payload["volume_level"] = round(max(0, min(100, number)) / 100, 2)
        elif key == "media_content_id":
            station = str(value).strip()
            if not RADIO_ID.fullmatch(station):
                raise HTTPException(400, "Unexpected station")
            payload[key] = station
        elif key == "media_content_type":
            kind = str(value).strip()
            if kind not in RADIO_TYPES:
                raise HTTPException(400, "Unexpected station")
            payload[key] = kind
    if "rgb" in data:
        payload.update(_color_payload(attrs, _rgb_channels(data["rgb"]), data.get("white")))
    return payload


def _rgb_channels(value) -> list[int]:
    if not isinstance(value, (list, tuple)) or len(value) != 3:
        raise HTTPException(400, "Color must be three numbers")
    channels = []
    for part in value:
        number = _num(part)
        if number is None:
            raise HTTPException(400, "Color must be three numbers")
        channels.append(max(0, min(255, int(round(number)))))
    return channels


def _color_payload(attrs: dict, rgb: list[int], white) -> dict:
    """Same color fields the Home Assistant light dialog sends."""
    modes = _modes(attrs)
    if "rgbw" in modes:
        current = attrs.get("rgbw_color") if isinstance(attrs.get("rgbw_color"), list) else [0, 0, 0, 0]
        fallback = current[3] if len(current) > 3 else 0
        level = _clamp_byte(white if white is not None else fallback)
        return {"rgbw_color": [*rgb, level]}
    if "rgbww" in modes:
        level = _clamp_byte(white, 0)
        return {"rgbww_color": [*rgb, level, 0]}
    if "rgb" in modes:
        return {"rgb_color": rgb}
    return {"hs_color": _rgb_to_hs(*rgb)}


async def _refresh_if_needed() -> None:
    if not session.refresh_token or time.time() < session.expires_at - 60:
        return
    async with httpx.AsyncClient(timeout=20) as client:
        response = await client.post(
            f"{session.base_url}/auth/token",
            data={
                "grant_type": "refresh_token",
                "refresh_token": session.refresh_token,
                "client_id": session.client_id,
            },
        )
    if response.status_code >= 400:
        session.clear()
        raise HTTPException(401, "Session expired. Sign in again.")
    body = response.json()
    session.access_token = body["access_token"]
    session.refresh_token = body.get("refresh_token") or session.refresh_token
    session.expires_at = time.time() + float(body.get("expires_in") or 1800)


async def _ha(method: str, path: str, **kwargs) -> httpx.Response:
    _require_login()
    await _refresh_if_needed()
    timeout = 30 if path.startswith("/api/camera_proxy/") else 20
    try:
        async with httpx.AsyncClient(timeout=timeout) as client:
            response = await client.request(
                method,
                f"{session.base_url}{path}",
                headers={"Authorization": f"Bearer {session.access_token}"},
                **kwargs,
            )
    except httpx.HTTPError:
        raise HTTPException(503, "Home Assistant is not reachable") from None
    if response.status_code == 401:
        raise HTTPException(401, "Home Assistant rejected the token")
    if response.status_code >= 400:
        raise HTTPException(response.status_code, response.text[:300] or "Home Assistant request failed")
    return response


async def _service(entity_id: str, service: str, data: dict | None = None) -> dict:
    if not ENTITY_ID.fullmatch(entity_id):
        raise HTTPException(400, "Unexpected entity id")
    domain = _domain(entity_id)
    if service not in DOMAIN_SERVICES.get(domain, set()):
        raise HTTPException(400, "This action is not available for this device")
    response = await _ha("GET", f"/api/states/{entity_id}")
    state = response.json()
    if not _visible(state):
        raise HTTPException(400, "This entity is not available from the panel")
    payload = {"entity_id": entity_id}
    payload.update(_checked_data(state, service, data or {}))
    if service == "play_media" and "media_content_id" not in payload:
        raise HTTPException(400, "Pick a station")
    await _ha("POST", f"/api/services/{domain}/{service}", json=payload)
    return {"ok": True, "entity_id": entity_id, "service": service}


@app.get("/")
def index() -> FileResponse:
    return FileResponse(ROOT / "index.html", headers={"Cache-Control": "no-store"})


@app.get("/api/session")
def get_session() -> dict:
    return {"logged_in": session.logged_in}


@app.post("/api/login")
async def login(body: LoginBody) -> dict:
    base_url = _base_url(body.base_url)
    client_id = base_url + "/"
    token = body.token.strip()
    if token:
        session.base_url = base_url
        session.client_id = client_id
        session.access_token = token
        session.refresh_token = ""
        session.expires_at = time.time() + 10 * 365 * 24 * 3600
        await _ha("GET", "/api/")
        return {"ok": True}

    username = body.username.strip()
    password = body.password
    if not username or not password:
        raise HTTPException(400, "Enter a username and password, or a long-lived token")

    async with httpx.AsyncClient(timeout=20) as client:
        flow = await client.post(
            f"{base_url}/auth/login_flow",
            json={"client_id": client_id, "handler": ["homeassistant", None], "redirect_uri": client_id},
        )
        if flow.status_code >= 400:
            raise HTTPException(flow.status_code, "Login flow was rejected")
        flow_id = flow.json().get("flow_id")
        if not flow_id:
            raise HTTPException(502, "Home Assistant did not return a login flow")
        step = await client.post(
            f"{base_url}/auth/login_flow/{flow_id}",
            json={"client_id": client_id, "username": username, "password": password},
        )
        if step.status_code >= 400:
            raise HTTPException(401, "Sign-in failed")
        step_body = step.json()
        if step_body.get("type") != "create_entry":
            raise HTTPException(401, f"Sign-in failed: {step_body.get('errors') or step_body.get('type')}")
        code = step_body.get("result")
        token_response = await client.post(
            f"{base_url}/auth/token",
            data={"grant_type": "authorization_code", "code": code, "client_id": client_id},
        )
    if token_response.status_code >= 400:
        raise HTTPException(401, "Home Assistant did not issue a token")
    token_body = token_response.json()
    session.base_url = base_url
    session.client_id = client_id
    session.access_token = token_body["access_token"]
    session.refresh_token = token_body.get("refresh_token") or ""
    session.expires_at = time.time() + float(token_body.get("expires_in") or 1800)
    return {"ok": True}


@app.post("/api/logout")
def logout() -> dict:
    session.clear()
    return {"ok": True}


def _camera_id(entity_id: str) -> str:
    if not entity_id.startswith("camera.") or not ENTITY_ID.fullmatch(entity_id):
        raise HTTPException(400, "Unexpected entity id")
    return entity_id


def _ws_url() -> str:
    base = session.base_url
    if base.startswith("https://"):
        return "wss://" + base[len("https://") :] + "/api/websocket"
    if base.startswith("http://"):
        return "ws://" + base[len("http://") :] + "/api/websocket"
    raise HTTPException(400, "Address must start with http:// or https://")


async def _open_ha_ws():
    _require_login()
    try:
        ws = await websockets.connect(_ws_url(), open_timeout=10, max_size=2_000_000)
        await asyncio.wait_for(ws.recv(), 10)
        await ws.send(json.dumps({"type": "auth", "access_token": session.access_token}))
        auth = json.loads(await asyncio.wait_for(ws.recv(), 10))
    except HTTPException:
        raise
    except Exception as exc:
        raise HTTPException(503, "Home Assistant is not reachable") from exc
    if auth.get("type") != "auth_ok":
        await ws.close()
        raise HTTPException(401, "Home Assistant rejected the token")
    return ws


async def _ha_ws_call(command: dict, timeout: float = 20) -> dict:
    ws = await _open_ha_ws()
    try:
        await ws.send(json.dumps({"id": 1, **command}))
        deadline = time.time() + timeout
        while time.time() < deadline:
            raw = await asyncio.wait_for(ws.recv(), deadline - time.time())
            msg = json.loads(raw)
            if msg.get("id") != 1 or msg.get("type") != "result":
                continue
            if not msg.get("success", False):
                error = msg.get("error") or {}
                raise HTTPException(502, error.get("message") or "Camera stream failed")
            result = msg.get("result") or {}
            return result if isinstance(result, dict) else {}
    except HTTPException:
        raise
    except Exception as exc:
        raise HTTPException(503, "Home Assistant is not reachable") from exc
    finally:
        await ws.close()
    raise HTTPException(504, "Camera stream timed out")


class RtcBridge:
    def __init__(self) -> None:
        self.ws = None
        self.session_id = ""
        self.answer = ""
        self.candidates: list[dict] = []
        self.error = ""
        self.ready = asyncio.Event()
        self.next_id = 2

    async def on_event(self, event: dict) -> None:
        kind = event.get("type")
        if kind == "session":
            self.session_id = str(event.get("session_id") or "")
            if self.session_id:
                rtc_bridges[self.session_id] = self
        elif kind == "answer":
            self.answer = str(event.get("answer") or "")
            self.ready.set()
        elif kind == "candidate":
            candidate = event.get("candidate")
            if isinstance(candidate, dict):
                self.candidates.append(candidate)
        elif kind == "error":
            self.error = str(event.get("message") or "WebRTC failed")
            self.ready.set()


rtc_bridges: dict[str, RtcBridge] = {}
hls_paths: dict[str, str] = {}


async def _camera_modes(entity_id: str) -> list[str]:
    result = await _ha_ws_call({"type": "camera/capabilities", "entity_id": entity_id})
    modes = result.get("frontend_stream_types") or []
    return [str(mode) for mode in modes]


def _rewrite_hls(text: str, entity_id: str, playlist_path: str) -> str:
    base = playlist_path.rsplit("/", 1)[0] + "/"

    def point(uri: str) -> str:
        uri = uri.strip().strip('"')
        if uri.startswith(("http://", "https://")):
            parsed = urllib.parse.urlparse(uri)
            path = parsed.path + (f"?{parsed.query}" if parsed.query else "")
        else:
            path = urllib.parse.urljoin(base, uri)
        return f"/api/camera/{entity_id}/media?src={urllib.parse.quote(path, safe='')}"

    lines = []
    for line in text.splitlines():
        if line.startswith("#") and 'URI="' in line:
            start = line.index('URI="') + 5
            end = line.index('"', start)
            line = line[:start] + point(line[start:end]) + line[end:]
            lines.append(line)
        elif line and not line.startswith("#"):
            lines.append(point(line))
        else:
            lines.append(line)
    return "\n".join(lines) + "\n"


def _safe_hls_path(src: str) -> str:
    path = urllib.parse.unquote(src)
    if not path.startswith("/api/hls/") or ".." in path:
        raise HTTPException(400, "Unexpected stream path")
    return path


async def _read_hls(entity_id: str, path: str) -> Response:
    response = await _ha("GET", path)
    media = response.headers.get("content-type", "application/vnd.apple.mpegurl")
    body = response.content
    if "mpegurl" in media or body.lstrip().startswith(b"#EXTM3U"):
        text = _rewrite_hls(body.decode("utf-8", "replace"), entity_id, path.split("?", 1)[0])
        return Response(content=text, media_type="application/vnd.apple.mpegurl", headers={"Cache-Control": "no-store"})
    return Response(content=body, media_type=media.split(";")[0], headers={"Cache-Control": "no-store"})


@app.get("/hls.min.js")
def hls_script() -> FileResponse:
    return FileResponse(ROOT / "vendor" / "hls.min.js", media_type="text/javascript", headers={"Cache-Control": "no-store"})


@app.get("/api/camera/{entity_id}/live")
async def camera_live(entity_id: str) -> dict:
    entity_id = _camera_id(entity_id)
    try:
        modes = await _camera_modes(entity_id)
    except HTTPException as exc:
        if exc.status_code != 502:
            raise
        modes = []
    if "hls" in modes:
        return {"mode": "hls", "url": f"/api/camera/{entity_id}/playlist.m3u8"}
    if "web_rtc" in modes:
        return {"mode": "web_rtc", "entity_id": entity_id}
    return {"mode": "mjpeg", "url": f"/api/camera/{entity_id}/mjpeg"}


@app.post("/api/camera/webrtc/config")
async def webrtc_config(body: EntityBody) -> dict:
    entity_id = _camera_id(body.entity_id.strip())
    return await _ha_ws_call({"type": "camera/webrtc/get_client_config", "entity_id": entity_id})


@app.post("/api/camera/webrtc/offer")
async def webrtc_offer(body: WebRtcOfferBody) -> dict:
    entity_id = _camera_id(body.entity_id.strip())
    bridge = RtcBridge()
    ws = await _open_ha_ws()
    bridge.ws = ws

    async def read() -> None:
        try:
            async for raw in ws:
                msg = json.loads(raw)
                if msg.get("id") == 1 and msg.get("type") == "event":
                    await bridge.on_event(msg.get("event") or {})
                elif msg.get("id") == 1 and msg.get("type") == "result" and not msg.get("success", True):
                    error = msg.get("error") or {}
                    bridge.error = error.get("message") or "WebRTC failed"
                    bridge.ready.set()
        except Exception:
            if not bridge.ready.is_set():
                bridge.error = "Camera stream closed"
                bridge.ready.set()

    task = asyncio.create_task(read())
    await ws.send(json.dumps({"id": 1, "type": "camera/webrtc/offer", "entity_id": entity_id, "offer": body.offer}))
    try:
        await asyncio.wait_for(bridge.ready.wait(), 20)
    except TimeoutError:
        task.cancel()
        await ws.close()
        raise HTTPException(504, "Camera stream timed out") from None
    if bridge.error or not bridge.answer or not bridge.session_id:
        task.cancel()
        await ws.close()
        raise HTTPException(502, bridge.error or "Camera did not return a stream")
    return {
        "session_id": bridge.session_id,
        "answer": bridge.answer,
        "candidates": bridge.candidates,
    }


@app.post("/api/camera/webrtc/candidate")
async def webrtc_candidate(body: WebRtcCandidateBody) -> dict:
    bridge = rtc_bridges.get(body.session_id)
    if bridge is None or bridge.ws is None:
        raise HTTPException(404, "Camera session is not open")
    message_id = bridge.next_id
    bridge.next_id += 1
    await bridge.ws.send(
        json.dumps(
            {
                "id": message_id,
                "type": "camera/webrtc/candidate",
                "entity_id": _camera_id(body.entity_id.strip()),
                "session_id": body.session_id,
                "candidate": body.candidate,
            }
        )
    )
    return {"ok": True}


@app.get("/api/camera/webrtc/candidates")
def webrtc_candidates(session_id: str, after: int = 0) -> dict:
    bridge = rtc_bridges.get(session_id)
    if bridge is None:
        raise HTTPException(404, "Camera session is not open")
    return {"candidates": bridge.candidates[after:], "next": len(bridge.candidates)}


@app.get("/api/camera/{entity_id}/playlist.m3u8")
async def camera_playlist(entity_id: str) -> Response:
    entity_id = _camera_id(entity_id)
    path = hls_paths.get(entity_id)
    if not path:
        result = await _ha_ws_call({"type": "camera/stream", "entity_id": entity_id})
        path = str(result.get("url") or "")
        if not path.startswith("/api/hls/"):
            raise HTTPException(502, "Camera did not return a stream")
        hls_paths[entity_id] = path
    try:
        return await _read_hls(entity_id, path)
    except HTTPException:
        hls_paths.pop(entity_id, None)
        raise


@app.get("/api/camera/{entity_id}/media")
async def camera_media(entity_id: str, src: str) -> Response:
    entity_id = _camera_id(entity_id)
    return await _read_hls(entity_id, _safe_hls_path(src))


@app.get("/api/camera/{entity_id}/mjpeg")
async def camera_mjpeg(entity_id: str) -> StreamingResponse:
    entity_id = _camera_id(entity_id)
    _require_login()
    await _refresh_if_needed()
    client = httpx.AsyncClient(timeout=httpx.Timeout(10, read=None))
    try:
        response = await client.send(
            client.build_request(
                "GET",
                f"{session.base_url}/api/camera_proxy_stream/{entity_id}",
                headers={"Authorization": f"Bearer {session.access_token}"},
            ),
            stream=True,
        )
    except httpx.HTTPError:
        await client.aclose()
        raise HTTPException(503, "Home Assistant is not reachable") from None
    if response.status_code >= 400:
        detail = (await response.aread())[:300].decode("utf-8", "replace")
        await response.aclose()
        await client.aclose()
        raise HTTPException(response.status_code, detail or "Camera stream failed")
    media = response.headers.get("content-type", "multipart/x-mixed-replace")

    async def chunks():
        try:
            async for chunk in response.aiter_bytes():
                yield chunk
        finally:
            await response.aclose()
            await client.aclose()

    return StreamingResponse(chunks(), media_type=media, headers={"Cache-Control": "no-store"})


async def _radio_stations(entity_id: str) -> list[dict]:
    cached = _radio_cache.get(entity_id)
    if cached and time.time() - cached[0] < 300:
        return cached[1]
    result = await _ha_ws_call(
        {
            "type": "media_player/browse_media",
            "entity_id": entity_id,
            "media_content_type": "music",
            "media_content_id": "media-source://radio_browser/popular",
        }
    )
    stations = []
    for child in result.get("children") or []:
        if not child.get("can_play"):
            continue
        station = str(child.get("media_content_id") or "")
        kind = str(child.get("media_content_type") or "")
        title = str(child.get("title") or "").strip()
        if not title or kind not in RADIO_TYPES or not RADIO_ID.fullmatch(station):
            continue
        stations.append({"title": title, "media_content_id": station, "media_content_type": kind})
    stations.sort(key=lambda item: item["title"].lower())
    _radio_cache[entity_id] = (time.time(), stations)
    return stations


@app.get("/api/radio/{entity_id}")
async def radio(entity_id: str) -> dict:
    if entity_id not in RADIO_ENTITIES:
        raise HTTPException(400, "This speaker has no radio list")
    return {"entity_id": entity_id, "stations": await _radio_stations(entity_id)}


@app.get("/api/devices")
async def devices() -> dict:
    response = await _ha("GET", "/api/states")
    grouped: dict[str, list[dict]] = {}
    for state in response.json():
        if not _visible(state):
            continue
        item = _item(state)
        grouped.setdefault(item["domain"], []).append(item)
    for items in grouped.values():
        items.sort(key=lambda item: (not item["verified"], item["name"].lower()))

    def rank(domain: str) -> tuple:
        if domain in GROUP_ORDER:
            return (0, GROUP_ORDER.index(domain))
        return (1, domain)

    groups = []
    for domain in sorted(grouped, key=rank):
        groups.append(
            {
                "domain": domain,
                "title": GROUP_TITLES.get(domain, domain.replace("_", " ").title()),
                "compact": domain in COMPACT_DOMAINS,
                "items": grouped[domain],
            }
        )
    return {"groups": groups}


@app.post("/api/call")
async def call(body: CallBody) -> dict:
    return await _service(body.entity_id.strip(), body.service.strip(), body.data)


@app.post("/api/on")
async def turn_on(body: EntityBody) -> dict:
    return await _service(body.entity_id.strip(), "turn_on")


@app.post("/api/off")
async def turn_off(body: EntityBody) -> dict:
    return await _service(body.entity_id.strip(), "turn_off")


@app.post("/api/approve")
def approve(body: ApproveBody) -> dict:
    _require_login()
    entity_id = body.entity_id.strip()
    if not ENTITY_ID.fullmatch(entity_id) or _domain(entity_id) in BLOCKED_DOMAINS:
        raise HTTPException(400, "Unexpected entity id")
    note = body.note.strip() or "Checked from the panel."
    approved[entity_id] = note
    _save_approved()
    return {"ok": True, "entity_id": entity_id, "note": note}


@app.post("/api/unapprove")
def unapprove(body: EntityBody) -> dict:
    _require_login()
    approved.pop(body.entity_id, None)
    _save_approved()
    return {"ok": True, "entity_id": body.entity_id}
