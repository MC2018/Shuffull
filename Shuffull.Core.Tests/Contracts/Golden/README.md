# API contract golden files

What the API puts on the wire for the endpoints below, written by `ApiContractGoldenFilesTest` with the API's real JSON
options (`AddShuffullControllers`). The test fails when the API's output stops matching these files, and, for the two
request bodies, when the API stops binding them.

| File | Endpoint | Read by |
|---|---|---|
| `songs-list` | `POST /api/v1/songs/list` | app |
| `songs-changed` | `GET /api/v1/songs/changed` | app |
| `user-songs` | `GET /api/v1/user-songs` | app |
| `playlists` | `GET /api/v1/playlists` | app |
| `playlists-list` | `POST /api/v1/playlists/list` | app |
| `playlists-create` | `PUT /api/v1/playlists` | app |
| `users-me` | `GET /api/v1/users/me` | app |
| `users-authenticate` | `POST /api/v1/users/authenticate` and `POST /api/v1/users` | app |
| `songs-keep` | `POST /api/v1/songs/keep` | app |
| `songs-retag` | `POST /api/v1/songs/retag` | app |
| `songs-pending-tags` | `GET /api/v1/songs/pending-tags` | funnel |
| `songs-library` | `GET /api/v1/songs/library` | funnel |
| `youtube-ratings` | `GET /api/v1/youtube-ratings` | funnel |
| `replacements` | `GET /api/v1/replacements` | funnel |
| `songs-tags` | `POST /api/v1/songs/{id}/tags` (request body) | funnel sends |
| `youtube-ratings-ack` | `POST /api/v1/youtube-ratings/ack` (request body) | funnel sends |

Not covered yet: the app's `GET /api/v1/tags` and the curator-only `POST /api/v1/songs/retag-stale`.

Copies live in the consumers, and their tests parse them:

- `ShuffullApp/tests/contracts/shuffull-api/` (vitest, through the app's zod schemas)
- `YoutubeFunnel/contracts/shuffull-api/` (xunit, through the funnel's real HTTP clients)

To change a shape on purpose:

1. `SHUFFULL_UPDATE_GOLDEN=1 dotnet test Shuffull.Core.Tests --filter ApiContractGoldenFilesTest`
2. From the hub: `scripts/sync-contract-fixtures.sh` copies the files to both consumers (`--check` only compares).
3. Run the app's and the funnel's tests. A consumer test that fails is an installed app or a running funnel that
   would break, so keep the change backward compatible, or ship the consumer first.
