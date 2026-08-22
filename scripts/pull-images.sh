#!/usr/bin/env bash
set -euo pipefail

# Pull every image the API stack uses, up front. Run this BEFORE a TrueNAS Custom App install/update so the
# install doesn't time out mid-download.
#
# The API-side counterpart to YoutubeFunnel/scripts/pull-images.sh (which covers the funnel's own images).
#
# Pull the DATABASE image from here, not mssql. Both stacks migrated to postgres:16-alpine to reclaim RAM,
# and nothing references SQL Server any more — but a hand-rolled pull loop carried
# mcr.microsoft.com/mssql/server:2022-latest well past the migration, spending 1.7 GB per run on an image
# the stack cannot use while never fetching postgres at all.
#
# The generated TrueNAS compose also sets `pull_policy: always` on the API service, so a redeploy pulls even
# if this was skipped. Running it first just keeps the download out of the install's timeout window.
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
