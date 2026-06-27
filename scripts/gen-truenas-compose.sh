#!/usr/bin/env bash
set -euo pipefail

# Render a self-contained, secrets-filled compose to paste into the TrueNAS "Install via YAML" box.
#
# Unlike the funnel (which hand-maps a template), the site compose is fully env-parameterized, so
# `docker compose config` already resolves every ${VAR} inline from the prod env file -- that IS the
# generation step. The api service's `build:` block is stripped because TrueNAS pulls the pushed image
# (maxc2018/shuffull-api) and has no source build context.
#
# The output contains real secrets -> it is gitignored; never commit it.
#
# Usage:
#   ./scripts/gen-truenas-compose.sh                 # reads ./.env.production
#   ./scripts/gen-truenas-compose.sh path/to.env
#
# Prereqs: a filled-in env file (copy .env.example), and the image pushed first:
#   DOCKER_REGISTRY=maxc2018/ docker compose build api && docker compose push api

cd "$(dirname "$0")/.."

ENVFILE="${1:-.env.production}"
OUT="docker-compose.truenas.local.yml"

[ -f "$ENVFILE" ] || { echo "ERROR: env file '$ENVFILE' not found (copy .env.example and fill it)." >&2; exit 1; }

# `config` resolves all substitutions; the awk filter drops the 4-space "build:" key and its 6-space children.
docker compose --env-file "$ENVFILE" config \
  | awk '
      /^    build:[[:space:]]*$/ { inbuild = 1; next }
      inbuild && /^      / { next }
      inbuild { inbuild = 0 }
      { print }
    ' > "$OUT"

echo "Wrote $OUT"
echo "Paste it into TrueNAS Apps -> Discover -> Install via YAML. Create the shared network first:"
echo "  docker network create shuffull-funnel-net"
