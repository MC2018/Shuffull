#!/usr/bin/env bash
set -euo pipefail

# Pull every image the API stack uses, up front. Run this BEFORE a TrueNAS Custom App install/update so the
# install doesn't time out mid-download.
#
# This is the API-side counterpart to YoutubeFunnel/scripts/pull-images.sh, which covers only the funnel's
# eight images plus postgres/gluetun — it has never pulled shuffull-api. That gap is why the box can sit on a
# weeks-old API image while a "pull everything" run reports success (verified 2026-08-22: the deployed
# shuffull-api was still the 2026-07-29 build).
#
# The generated TrueNAS compose now also sets `pull_policy: always` on the API service, so a redeploy pulls
# even if this was skipped. Running it first just keeps the download out of the install's timeout window.
#
# Usage:
#   ./scripts/pull-images.sh                 # defaults to the maxc2018/ (Docker Hub) registry
#   ./scripts/pull-images.sh maxc2018/       # explicit registry prefix (note trailing slash)
#   sudo ./scripts/pull-images.sh maxc2018/  # on TrueNAS, where docker needs root

# Registry prefix: arg 1 > $DOCKER_REGISTRY env var > the maxc2018/ Docker Hub default.
REG="${1:-${DOCKER_REGISTRY:-maxc2018/}}"
case "$REG" in */) ;; *) REG="$REG/" ;; esac

IMAGES=(
  "${REG}shuffull-api:latest"
  "postgres:16-alpine"
)

echo "Registry prefix: ${REG}"
fail=0
for img in "${IMAGES[@]}"; do
  echo "==> docker pull $img"
  if ! docker pull "$img"; then
    echo "!! failed to pull $img" >&2
    fail=1
  fi
done

if [ "$fail" -ne 0 ]; then
  echo "One or more pulls failed (see above)." >&2
  exit 1
fi
echo "Done. All images pulled."
