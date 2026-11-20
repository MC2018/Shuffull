# API contract golden files

What the API puts on the wire for the endpoints below, written by `ApiContractGoldenFilesTest` with the API's real JSON
options (`AddShuffullControllers`). The test fails when the API's output stops matching these files, and, for the
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
| `listens` | `POST /api/v1/listens` | app (ShuffullApp#57) |
| `listens-stats` | `GET /api/v1/listens/stats` | app (ShuffullApp#115) |
| `recap` | `GET /api/v1/recap/{year}` | app (ShuffullApp#116) |
| `genre-jams` | `GET /api/v1/genre-jams` | app (ShuffullApp#214) |
| `genre-jams-save` | `PUT /api/v1/genre-jams/{id}` | app (ShuffullApp#214) |
| `song-duplicate-reports` | `POST /api/v1/song-duplicate-reports` | app (ShuffullApp#219) |
| `song-duplicate-reports-mine` | `GET /api/v1/song-duplicate-reports/mine` | app (ShuffullApp#219) |
| `song-duplicate-reports-withdraw` | `DELETE /api/v1/song-duplicate-reports/{id}` | app (ShuffullApp#219) |
| `song-duplicate-reports-queue` | `GET /api/v1/song-duplicate-reports/queue` (curator) | app (ShuffullApp#219) |
| `song-duplicate-reports-resolve` | `POST /api/v1/song-duplicate-reports/resolve` (curator) | app (ShuffullApp#219) |
| `songs-retag` | `POST /api/v1/songs/retag` | app |
| `songs-retag-stale` | `POST /api/v1/songs/retag-stale` | app |
| `tags` | `GET /api/v1/tags` | app |
| `songs-pending-tags` | `GET /api/v1/songs/pending-tags` | funnel |
| `songs-library` | `GET /api/v1/songs/library` | funnel |
| `youtube-ratings` | `GET /api/v1/youtube-ratings` | funnel |
| `replacements` | `GET /api/v1/replacements` | funnel |
| `songs-tags` | `POST /api/v1/songs/{id}/tags` (request body) | funnel sends |
| `youtube-ratings-ack` | `POST /api/v1/youtube-ratings/ack` (request body) | funnel sends |
| `songs-pending-tags-holds` | `POST /api/v1/songs/pending-tags/holds` (request body) | funnel sends |
| `song-imports-details` | `POST /api/v1/song-imports` (the multipart `details` part, read with Newtonsoft) | funnel sends |
| `song-lyrics` | `PUT /api/v1/song-lyrics` (request body) | funnel sends |
| `listens-request` | `POST /api/v1/listens` (request body) | app sends (ShuffullApp#57) |
| `users-authenticate-request` | `POST /api/v1/users/authenticate` and `POST /api/v1/users` (request body) | app sends |
| `songs-list-request` | `POST /api/v1/songs/list` (request body) | app sends |
| `playlists-list-request` | `POST /api/v1/playlists/list` (request body) | app sends |
| `user-songs-create` | `PUT /api/v1/user-songs` (request body) | app sends |
| `user-songs-last-played` | `POST /api/v1/user-songs/last-played` (request body) | app sends |
| `user-songs-like` | `POST /api/v1/user-songs/{id}/like` (request body) | app sends |
| `songs-update` | `PUT /api/v1/songs/{id}` (request body) | app sends |
| `songs-keep-request` | `POST /api/v1/songs/keep` (request body) | app sends |
| `songs-retag-request` | `POST /api/v1/songs/retag` (request body) | app sends |
| `replacements-request` | `POST /api/v1/replacements` (request body) | app sends |
| `genre-jams-save-request` | `PUT /api/v1/genre-jams/{id}` (request body) | app sends (ShuffullApp#214) |
| `song-duplicate-reports-request` | `POST /api/v1/song-duplicate-reports` (request body) | app sends (ShuffullApp#219) |
| `song-duplicate-reports-resolve-request` | `POST /api/v1/song-duplicate-reports/resolve` (request body) | app sends (ShuffullApp#219) |

Only the endpoints in the table are pinned. Calls that send no body are not: playlist create, add song, remove song and
delete, Genre Jam delete, and `songs/retag-stale`, which carry only route and query values (shuffull-workspace#92).

Copies live in the consumers, and their tests parse them:

- `ShuffullApp/tests/contracts/shuffull-api/` (vitest, through the app's zod schemas)
- `YoutubeFunnel/contracts/shuffull-api/` (xunit, through the funnel's real HTTP clients)

To change a shape on purpose:

1. `SHUFFULL_UPDATE_GOLDEN=1 dotnet test Shuffull.Core.Tests --filter ApiContractGoldenFilesTest`
2. From the hub: `scripts/sync-contract-fixtures.sh` copies the files to both consumers (`--check` only compares).
3. Run the app's and the funnel's tests. A consumer test that fails is an installed app or a running funnel that
   would break, so keep the change backward compatible, or ship the consumer first.
