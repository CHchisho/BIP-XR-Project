"""Automated tests based on ha-api-v2.md (13 Oct 2026).

By default ALL tests only read data. A single optional lamp test can send
commands, but only after HA_ENABLE_WRITE_TESTS=1 is set explicitly.
"""

import math
import os
import time

import pytest
import requests


# Only entities mentioned in the project's ha-api-v2.md. No sirens, alarms,
# or physical door-lock commands are issued by this test suite.
READ_ENTITIES = [
    ("light.switch", {"on", "off"}),
    ("light.aqara_dimmer_switch_h2_2", {"on", "off"}),
    ("light.rgbw_controller_2", {"on", "off"}),
    ("switch.aqara_lumi_plug_aeu001_switch_2", {"on", "off"}),
    ("switch.metered_wall_plug_switch", {"on", "off"}),
    ("cover.aqara_curtain_driver_e1", {"open", "closed", "opening", "closing"}),
    ("cover.aqara_roller_shade_driver_e1", {"open", "closed", "opening", "closing"}),
    ("climate.thermostat", {"heat", "off"}),
]

ALLOWED_TEST_LIGHTS = {
    "light.switch",
    "light.aqara_dimmer_switch_h2_2",
    "light.rgbw_controller_2",
}


def api_get(session, base_url, endpoint):
    try:
        return session.get(f"{base_url}{endpoint}", timeout=8)
    except requests.RequestException as exc:
        pytest.fail(
            f"API is unreachable: {exc}. Connect to the lab Wi-Fi; "
            "homeassistant.local is only accessible on the local network."
        )


def api_post(session, base_url, endpoint, payload):
    try:
        return session.post(f"{base_url}{endpoint}", json=payload, timeout=8)
    except requests.RequestException as exc:
        pytest.fail(f"API write request failed: {exc}")


def read_entity(session, base_url, entity_id):
    response = api_get(session, base_url, f"/api/states/{entity_id}")
    assert response.status_code == 200, (
        f"{entity_id}: HTTP {response.status_code}; Response: {response.text[:160]}"
    )
    data = response.json()
    assert data["entity_id"] == entity_id
    assert isinstance(data.get("attributes"), dict)
    assert isinstance(data.get("state"), str)
    return data


def test_without_token_returns_401(open_session, base_url):
    """Documented in v2: GET /api/ without a token returns HTTP 401."""
    response = api_get(open_session, base_url, "/api/")
    assert response.status_code == 401


def test_invalid_token_returns_401(open_session, base_url):
    response = api_get_with_fake_token(open_session, base_url)
    assert response.status_code == 401


def api_get_with_fake_token(session, base_url):
    return api_get_with_header(session, base_url, "Bearer definitely_invalid_test_token")


def api_get_with_header(session, base_url, auth):
    try:
        return session.get(
            f"{base_url}/api/", headers={"Authorization": auth}, timeout=8
        )
    except requests.RequestException as exc:
        pytest.fail(f"API is unreachable: {exc}")


def test_valid_token_works(ha_session, base_url):
    response = api_get(ha_session, base_url, "/api/")
    assert response.status_code == 200


@pytest.mark.parametrize("entity_id,valid_states", READ_ENTITIES, ids=[x[0] for x in READ_ENTITIES])
def test_device_state_api(ha_session, base_url, entity_id, valid_states):
    data = read_entity(ha_session, base_url, entity_id)
    if data["state"] == "unavailable":
        pytest.skip(f"{entity_id} is currently unavailable (device offline)")
    assert data["state"] in valid_states, (
        f"{entity_id}: Unexpected state {data['state']!r}"
    )


def test_power_sensor_returns_number(ha_session, base_url):
    data = read_entity(ha_session, base_url, "sensor.aqara_dimmer_switch_h2_power")
    if data["state"] == "unavailable":
        pytest.skip("Power sensor is currently unavailable")
    watts = float(data["state"])
    assert math.isfinite(watts)


def test_unknown_entity_is_not_found(ha_session, base_url):
    response = api_get(ha_session, base_url, "/api/states/light.__xr_test_nonexistent__")
    assert response.status_code == 404


@pytest.mark.write
def test_optional_real_lamp_switch_and_restore(ha_session, base_url):
    """Opt-in only: change one approved lamp and restore its original on/off state.

    Run only with the team's permission while physically present in the lab.
    Other attributes (e.g. color / exact brightness) may not be restored.
    """
    if os.getenv("HA_ENABLE_WRITE_TESTS") != "1":
        pytest.skip("Write test is disabled; set HA_ENABLE_WRITE_TESTS=1 to enable it")

    entity_id = os.getenv("HA_TEST_LIGHT_ID", "light.rgbw_controller_2")
    if entity_id not in ALLOWED_TEST_LIGHTS:
        pytest.fail("HA_TEST_LIGHT_ID must refer to an approved light entity listed in API v2")

    original = read_entity(ha_session, base_url, entity_id)
    if original["state"] not in ("on", "off"):
        pytest.skip(f"Lamp state cannot be tested safely: {original['state']}")

    target = "off" if original["state"] == "on" else "on"

    def set_lamp(state):
        response = api_post(
            ha_session,
            base_url,
            f"/api/services/light/turn_{state}",
            {"entity_id": entity_id},
        )
        assert response.status_code == 200, (
            f"Switching failed: HTTP {response.status_code} {response.text[:160]}"
        )

    def wait_for_state(expected, timeout_seconds=12):
        deadline = time.monotonic() + timeout_seconds
        while time.monotonic() < deadline:
            data = read_entity(ha_session, base_url, entity_id)
            if data["state"] == expected:
                return
            time.sleep(1)
        pytest.fail(f"{entity_id} did not reach the expected state {expected!r}")

    try:
        set_lamp(target)
        wait_for_state(target)
    finally:
        # Best effort restoration, even if the check above fails.
        set_lamp(original["state"])
        wait_for_state(original["state"])
