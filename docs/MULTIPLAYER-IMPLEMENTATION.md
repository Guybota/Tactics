# Multiplayer foundation

## Implemented

- `IMatchSession`: asynchronous action submission, revisions and copied snapshots.
- `LocalMatchSession`: owns execution state and delegates the rules to `MatchEngine`.
- `OnlineMatchSession`: consumes confirmed public snapshots through `IMatchTransport`,
  serializes requests, retains an uncertain command for retry, and ignores older snapshots.
- `AuthoritativeMatch`: an in-memory authority for authenticated player slots. A command ID
  identifies one exact payload; duplicate requests receive the original receipt. State,
  revision and receipts are changed under one process-local lock.
- `MatchStateData`: private server storage DTO, including the random stream.
- `MatchSnapshotData`: public flat-grid DTO, excluding the random stream.
- `MatchContentData`: validated unit, race, leader and damage catalogue.
- `MatchCommandData`: versioned field-based request DTOs, including all six action kinds,
  without a caller-provided authenticated identity.
- `MatchInitializer`: creates a 2–4-player match without Unity objects and applies opening
  income and leader bonuses once.
- The Unity controller executes its local actions through `LocalMatchSession`. The explicit
  local scene bridge preserves existing saves, bot integration and legacy component edits.

The scene bridge is temporary: `LocalSession` imports local component data before reading or
executing actions. A remote session must never import this data. The online session is not
yet connected to the Unity scene or menus.

## Export the current game catalogue

In Unity, run **Tools → Tactics → Multiplayer → Export Server Content**.

The exporter writes `Server/Content/match-content.json`. It uses the same `RaceCatalog`
and `UnityMatchContent` as live matches, preserves baseline stats before leader bonuses,
sorts entries, and stamps a deterministic SHA-256 content version. Re-export after changing
the units, recruitment lists or damage table. The backend must validate the catalogue and
pin its version for the lifetime of a match.

## Standalone verification

From the repository root, with .NET 8 installed:

```powershell
dotnet restore Tools/MultiplayerSmoke/MultiplayerSmoke.csproj --configfile Tools/MultiplayerSmoke/NuGet.Config
dotnet run --project Tools/MultiplayerSmoke/MultiplayerSmoke.csproj --no-restore
```

The console compiles the actual Core source files without Unity or external NuGet packages.
It verifies two client sessions, combat, lost replies after commit, repeat requests,
authenticated slot checks, competing revisions, snapshot ordering and private-state restore.
Its transport runs inside one process; it is not a test of real internet connections.

Unity EditMode tests cover the contracts, authority, online session and export of the real
catalogue. PlayMode tests verify the local scene integration, revisions and save reloads.

## Next implementation boundary

1. Implement the Cloud Code authentication boundary and `IMatchTransport` provider.
2. Persist state, revision, catalogue/map versions and command receipts atomically in Cloud
   Save using storage write locks. A C# lock is insufficient across Cloud Code instances.
3. Persist pending client commands across process restarts, so a timeout followed by closing
   the app can resume using the same command ID.
4. Implement durable match creation, invitations, participant readiness and player match lists.
5. Bind the Unity scene to online confirmed state; adapt UI callers to asynchronous completion
   rather than treating a queued request as an accepted action.
6. Add create/join/resume UI and test two independent builds against a development backend.

The private-state DTO assumes a complete trusted record. The future storage reader must
check required JSON fields, including `RandomState`, before decoding: a missing field must
not be interpreted as a genuine zero random state.

No Unity services were installed or deployed by this increment. `AuthoritativeMatch` stores
receipts in memory and refuses new commands at its explicit capacity instead of evicting
receipts. It is a verification implementation, not durable production storage. Server-only
results contain RNG and must be converted to public snapshot DTOs before transmission.
