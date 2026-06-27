#!/usr/bin/env bash
set -euo pipefail

# Build the Shuffull API image and push it to your container registry (Docker Hub / GHCR), so TrueNAS
# can pull it. Run on your dev/build machine. The registry prefix comes from .env.production
# (DOCKER_REGISTRY=maxc2018/  ->  image maxc2018/shuffull-api); override it for one run with
#   DOCKER_REGISTRY=ghcr.io/<user>/ ./scripts/build-and-push.sh
# (a shell env var wins over the env file).
#
# Prereq: log in once so the push is authorized:
#   docker login                 # Docker Hub (paste an access token at the prompt)
#   docker login ghcr.io         # GHCR
#
# Usage:
#   ./scripts/build-and-push.sh                 # reads ./.env.production
#   ./scripts/build-and-push.sh path/to.env

cd "$(dirname "$0")/.."

ENVFILE="${1:-.env.production}"
[ -f "$ENVFILE" ] || { echo "ERROR: env file '$ENVFILE' not found (copy .env.example and fill it)." >&2; exit 1; }

# The image only needs DOCKER_REGISTRY at build time (config is injected at runtime, never baked), but the
# compose file references the other vars too, so feed the prod env to avoid "variable not set" warnings.
echo "==> Building the shuffull-api image"
docker compose --env-file "$ENVFILE" build api

echo "==> Pushing to the registry"
docker compose --env-file "$ENVFILE" push api

echo "==> Done. Then on the dev box: ./scripts/gen-truenas-compose.sh"
echo "    and paste docker-compose.truenas.local.yml into TrueNAS -> Apps -> Install via YAML."
