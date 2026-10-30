# API contract golden files

What the API puts on the wire for the endpoints other repos read, written by `ApiContractGoldenFilesTest` with the
API's real JSON options. The test fails when the API's output stops matching these files.

Copies live in the consumers, and their tests parse them:

- `ShuffullApp/tests/contracts/shuffull-api/` (vitest, through the app's zod schemas)
- `YoutubeFunnel/contracts/shuffull-api/` (xunit, through the funnel's real HTTP clients)

To change a shape on purpose:

1. `SHUFFULL_UPDATE_GOLDEN=1 dotnet test Shuffull.Core.Tests --filter ApiContractGoldenFilesTest`
2. From the hub: `scripts/sync-contract-fixtures.sh` copies the files to both consumers (`--check` only compares).
3. Run the app's and the funnel's tests. A consumer test that fails is an installed app or a running funnel that
   would break, so keep the change backward compatible, or ship the consumer first.

`youtube-ratings-ack.json` and `songs-tags.json` are request bodies (funnel → API); every other file is a response.
