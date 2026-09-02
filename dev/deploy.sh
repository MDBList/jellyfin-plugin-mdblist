#!/usr/bin/env bash
# Build the plugin and deploy it into the local dev Jellyfin server's plugin
# folder, then restart the container and tail its logs.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEV_DIR="$ROOT/dev"
PLUGIN_DIR="$DEV_DIR/config/plugins/MDBList"
PUBLISH_DIR="$ROOT/Jellyfin.Plugin.MDBList/bin/Debug/net9.0"

dotnet build "$ROOT/Jellyfin.Plugin.MDBList/Jellyfin.Plugin.MDBList.csproj" -c Debug

mkdir -p "$PLUGIN_DIR"
cp "$PUBLISH_DIR/Jellyfin.Plugin.MDBList.dll" "$PLUGIN_DIR/"
if [ -f "$PUBLISH_DIR/Jellyfin.Plugin.MDBList.pdb" ]; then
  cp "$PUBLISH_DIR/Jellyfin.Plugin.MDBList.pdb" "$PLUGIN_DIR/"
fi
cp "$DEV_DIR/meta.json" "$PLUGIN_DIR/"

cd "$DEV_DIR"
docker compose restart jellyfin

echo "Deployed. Tailing logs (Ctrl+C to stop)..."
docker compose logs -f jellyfin
