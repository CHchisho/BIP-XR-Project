# XR Smart House – Automated API Tests

This small test project validates the **Home Assistant REST API**, not the Unity UI
or the Meta Quest headset. It is based on the project's `ha-api-v2.md` (13 October 2026).

## Requirements

- A Windows PC connected to the **same lab Wi-Fi/LAN** as Home Assistant.
- Python 3 installed.
- A **long-lived access token** from an authorized Home Assistant account for protected API requests (ask the lab team for permission).
- **Never commit your token to GitHub or share it with anyone.**

## Step 1: Install dependencies (PowerShell)

Open a terminal in the extracted project folder:

```powershell
python -m pip install -r requirements.txt
```

## Step 2: Set your token for the current PowerShell session

```powershell
$env:HA_TOKEN = "YOUR_OWN_API_TOKEN"
$env:HA_BASE_URL = "http://homeassistant.local:8123"
```

**Important:** Enter the token only on your own machine. Do not include it in screenshots, chat messages, or commits.

## Step 3: Run the read-only tests

```powershell
python -m pytest -v --junitxml=test-results.xml
```

The project contains **14 tests**, including parameterized test cases:

1. No token: the server responds with HTTP 401.
2. Invalid token: HTTP 401.
3. Valid token: HTTP 200.
4. Eight device endpoints: JSON with `entity_id`, `state`, and `attributes`, and a plausible device state.
5. Power sensor: a numeric value in watts.
6. Nonexistent entity: HTTP 404.
7. Optional lamp write test (**SKIPPED** by default).

A device that reports `unavailable` causes the corresponding test case to be skipped.
A skipped test does **not** mean that the device passed testing.

### Understanding test results

- **PASSED:** The assertion was satisfied.
- **FAILED:** The assertion failed, for example because the API could not be reached or the returned state was unexpected.
- **SKIPPED:** The token was not provided, the device is offline, or the optional write test is disabled.

The generated `test-results.xml` file can be attached to the project documentation.

## Optional: Switch a real lamp (only with lab team approval)

This test changes the state of **a real device**, so it is disabled by default.
It sends only `light/turn_on` and `light/turn_off` to an approved lamp,
checks the reported state, and attempts to restore the original **on/off state**.
Its exact brightness or color may not be restored.

```powershell
$env:HA_ENABLE_WRITE_TESTS = "1"
$env:HA_TEST_LIGHT_ID = "light.rgbw_controller_2"
python -m pytest -v -m write
```

Disable write tests again afterward:

```powershell
Remove-Item Env:HA_ENABLE_WRITE_TESTS
```

No write tests for door locks, sirens, alarm panels, or thermostats are included.

## Limitations

- The lab's private network is not accessible from this development environment.
  The tests can be prepared and collected here, but **cannot be executed against the real lab devices** here.
- An HTTP 200 response means Home Assistant accepted the service request;
  it does not prove that the physical device actually moved or switched.
  The optional lamp test therefore reads the device state again afterward.
- This suite does not test VR rendering, controller interaction, or the Meta Quest connection.
