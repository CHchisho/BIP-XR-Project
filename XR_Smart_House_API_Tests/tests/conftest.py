import os

import pytest
import requests


@pytest.fixture(scope="session")
def base_url():
    return os.getenv("HA_BASE_URL", "http://homeassistant.local:8123").rstrip("/")


@pytest.fixture(scope="session")
def ha_session():
    token = os.getenv("HA_TOKEN", "").strip()
    if not token:
        pytest.skip("HA_TOKEN is missing: set it as an environment variable while connected to the lab network")

    session = requests.Session()
    session.headers.update({
        "Authorization": f"Bearer {token}",
        "Content-Type": "application/json",
    })
    yield session
    session.close()


@pytest.fixture(scope="session")
def open_session():
    with requests.Session() as session:
        yield session
