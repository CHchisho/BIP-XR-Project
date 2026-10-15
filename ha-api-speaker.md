# Home Assistant API: speaker

## Network

The API exists only on the lab LAN.

- Base URL: `http://homeassistant.local:8123`
- `homeassistant.local` is mDNS. A normal DNS lookup does not find it. It does not answer from the public internet or from another Wi-Fi.
- `GET /api/` with no token returns **401**. That means the service is up.

Do not put the Wi-Fi password, the Home Assistant password, or a token in git.

Lights, switches, covers, locks, and the thermostat are in `ha-api-v2.md`. This page is the speaker.

## Auth

Send every `/api/` call with:

```http
Authorization: Bearer <token>
```

Create a **long-lived access token** in the Home Assistant profile: Security → Long-lived access tokens. Store it in an environment variable or a gitignored file. The UI login is not a token.

The station list uses the WebSocket below. Send the same token in the auth message, not as an HTTP header.

## Speaker

Checked from the lab panel on 13 Oct 2026. This is the Audio block in the Home Assistant dashboard, the card with Browse media.

| entity_id                | Name                  | Use                     |
| ------------------------ | --------------------- | ----------------------- |
| `media_player.famnest2`  | Sonos Stand DarkNest  | On, off, volume, radio. |

```http
GET /api/states/media_player.famnest2
```

`state` is `off`, `idle`, `playing`, or `paused`. `unavailable` means the speaker is not responding. Do not send a command.

`attributes.volume_level` is 0–1. After a station starts, `attributes.media_content_id` is the stream URL. `attributes.media_title` is often empty for a radio stream.

HTTP 200 means Home Assistant accepted the call. The body is often an empty array. Read the entity again to see the new state.

### On and off

```http
POST /api/services/media_player/turn_on
POST /api/services/media_player/turn_off
Content-Type: application/json

{"entity_id": "media_player.famnest2"}
```

### Volume

`volume_level` is 0–1. 0.2 is 20%.

```http
POST /api/services/media_player/volume_set

{"entity_id": "media_player.famnest2", "volume_level": 0.2}
```

### Radio

Stations are the Radio Browser → Popular folder from Browse media.

1. Open `ws://homeassistant.local:8123/api/websocket`.
2. The server sends `{"type": "auth_required"}`.
3. Send `{"type": "auth", "access_token": "<token>"}`. The next message is `{"type": "auth_ok"}`.
4. Send:

```json
{
  "id": 1,
  "type": "media_player/browse_media",
  "entity_id": "media_player.famnest2",
  "media_content_type": "music",
  "media_content_id": "media-source://radio_browser/popular"
}
```

The result `children` list is the stations. Use `title`, `media_content_id`, and `media_content_type` from a child with `"can_play": true`.

`media_content_id` looks like `media-source://radio_browser/98adecf7-2683-4408-9be7-02d3f9098eb8`. `media_content_type` is `audio/mpeg` or `audio/aac`. Keep the pair from the same child. Do not invent an id.

Play that station:

```http
POST /api/services/media_player/play_media

{
  "entity_id": "media_player.famnest2",
  "media_content_id": "media-source://radio_browser/98adecf7-2683-4408-9be7-02d3f9098eb8",
  "media_content_type": "audio/mpeg"
}
```

That example id is BBC World Service from Popular. Playing turns the speaker on if it was off. Read state afterwards: `playing`.
