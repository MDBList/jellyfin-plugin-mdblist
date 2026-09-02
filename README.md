# jellyfin-plugin-mdblist

A Jellyfin server plugin that syncs watched status and ratings two-way with
[MDBList](https://mdblist.com), and pushes library/collection membership.
Uses MDBList's incremental sync API (`/sync/last_activities` +
`/sync/journal`) for cursor-based updates rather than full-library
reconciliation on every run.

This is a port of the design proven in
[kodi-mdblist-scrobbler](https://github.com/linaspurinis/kodi-mdblist-scrobbler),
an equivalent Kodi addon.

## Status

Early development. See the implementation plan for the phased build-out.

## Local development

Requires the .NET 9 SDK (`brew install dotnet@9` on macOS) and Docker.

```sh
cd dev
docker compose up -d
```

This starts a local Jellyfin 10.11.11 server on `http://localhost:8096`.
