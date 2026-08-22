#!/usr/bin/env bash
set -euo pipefail

# Build the Shuffull API image and push it to your container registry (Docker Hub / GHCR) so TrueNAS can
# pull it. Run on your dev/build machine.
#
# Usage:
#   ./scripts/build-and-push.sh <dockerhub-user>/        # Docker Hub  -> <user>/shuffull-api
#   ./scripts/build-and-push.sh ghcr.io/<user>/          # GHCR
#   DOCKER_REGISTRY=<dockerhub-user>/ ./scripts/build-and-push.sh
#
# Prereq: log in once so the push is authorized:
#   docker login                 # Docker Hub (paste an access token at the prompt)
#   docker login ghcr.io         # GHCR

cd "$(dirname "$0")/.."

REG="${1:-${DOCKER_REGISTRY:-}}"
if [ -z "$REG" ]; then
  echo "ERROR: pass your registry prefix (e.g. maxc2018/ or ghcr.io/<user>/) as the first argument," >&2
  echo "       or set DOCKER_REGISTRY. It must match DOCKER_REGISTRY in .env.production (gen reads it there)." >&2
  exit 1
fi
case "$REG" in */) ;; *) REG="$REG/" ;; esac   # ensure a trailing slash
export DOCKER_REGISTRY="$REG"

# The image only needs DOCKER_REGISTRY at build time (all runtime config is injected, never baked); feed the
# prod env when present so the compose file's other ${VAR}s don't warn. The exported REG wins for the image tag.
ENV_ARG=()
[ -f .env.production ] && ENV_ARG=(--env-file .env.production)

echo "==> Building ${DOCKER_REGISTRY}shuffull-api"
docker compose "${ENV_ARG[@]}" build api

echo "==> Pushing ${DOCKER_REGISTRY}shuffull-api"
docker compose "${ENV_ARG[@]}" push api

echo "==> Done. Then: ./scripts/gen-truenas-compose.sh  (uses DOCKER_REGISTRY from .env.production)"
