# jellyfin-plugin-mdblist

A Jellyfin server plugin that syncs watched status and ratings two-way with
[MDBList](https://mdblist.com), pushes library/collection membership, and
reports live playback progress via scrobbling. Uses MDBList's incremental
sync API (`/sync/last_activities` + `/sync/journal`) for cursor-based
updates rather than full-library reconciliation on every run.

This is a port of the design proven in
[kodi-mdblist-scrobbler](https://github.com/linaspurinis/kodi-mdblist-scrobbler),
an equivalent Kodi addon.

## Features

- Two-way watched-status sync
- Two-way ratings sync
- Collection/library push (Jellyfin → MDBList)
- Live playback scrobbling
- Incremental, cursor-based sync with automatic full-reconciliation fallback
- A 24h scheduled full sync plus a 15-minute activity-gated cheap poll
- Per-category toggles, and a "sync after library scan" option

## Installing

1. In Jellyfin, go to **Dashboard → Plugins → Repositories → Add Repository**.
2. Add this repository URL:
   ```
   https://raw.githubusercontent.com/linaspurinis/jellyfin-plugin-mdblist/gh-pages/manifest.json
   ```
3. Go to **Dashboard → Plugins → Catalog**, find **MDBList**, and install it.
4. Restart Jellyfin.
5. Go to **Dashboard → Plugins → My Plugins → MDBList** to open its config page:
   - Pick the Jellyfin user to link.
   - Click **Connect to MDBList** and follow the device code flow (visit the
     shown URL, enter the code, approve on MDBList).
   - Choose which categories to sync (watched status, ratings, collection,
     live scrobbling) and save.

## Local development

Requires the .NET 9 SDK (`brew install dotnet@9` on macOS) and Docker.

```sh
cd dev
docker compose up -d
```

This starts a local Jellyfin 10.11.11 server on `http://localhost:8096`.
