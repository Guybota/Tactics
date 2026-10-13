# TacticsMultiplayer Cloud Code module

Unity Cloud Code C# module targeting .NET 8, with the production scene-independent
rules referenced through `Tactics.Multiplayer.Backend`. Official packages are
pinned to `Com.Unity.Services.CloudCode.Core` **0.0.7** and
`Com.Unity.Services.CloudCode.Apis` **0.0.27**. Versions and SDK method signatures
were verified against the official NuGet packages, not inferred from Unity client
SDKs. Module assembly name is **TacticsMultiplayer**.

The module embeds `Server/Content/match-content.json` and the vetted
`Server/Content/maps/default.json`; map ID `default-1v1`, map version `1`.
Creation accepts server catalogue/map identifiers, never a player-supplied board,
unit stats, identity or RNG seed.

## Functions

- `CreateMatch(request: MatchCreateRequest)` → `MatchLobbyReply`.
- `JoinMatch(request: MatchJoinRequest)` → `MatchLobbyReply`.
- `GetLobby(matchId: string)` → `MatchLobbyReply`.
- `GetMatch(matchId: string)` → `MatchTransportReply`.
- `SubmitAction(command: MatchCommandData)` → `MatchTransportReply`.

The invocation's `IExecutionContext.PlayerId` is the only caller identity. Calls
without a player context are rejected. The `ServiceToken` is used solely on the
server for Cloud Save Private Custom Data. Public replies contain neither the
private RNG nor identity bindings or command receipts.

## Persistence and its limits

Each match uses custom ID `tactics_match_<server UUID>` and one private key,
`record`. The complete state, revision, identity bindings and compact receipts are
written together in one `SetPrivateCustomItemAsync` call. Updates always supply
the `WriteLock` returned by `GetPrivateCustomItemsAsync`. A 409 conflict is a
definite noncommit that the backend can reread/retry. Ambiguous SDK failures throw
a sanitized exception, preserving the client's pending command identifier after
a possible commit. They must never become a normal rejected-action response.

Cloud Save documents **5 MiB per custom entity per access class**, including
Private Data. The shared codec bounds raw match records to 4 MiB; the adapter also
checks the escaped JSON string value against the 5 MiB quota before writing.
Compact receipts remain bounded by the backend's configured capacity. Receipts
are not evicted, because forgetting them could execute a repeated command twice.

**Creation limitation:** Cloud Save does not document an atomic create-if-absent
operation. Creating requires omitting the lock; omission also overwrites an
existing value. Therefore the backend exclusively generates fresh cryptographic
UUIDs, and the adapter checks that the private record is absent before its first
write. An existing record is refused without a write. This relies on UUID
uniqueness; the preflight itself is not an atomic uniqueness constraint. Never
expose this creation adapter with a caller-selected match ID.

Creation retries after an uncertain response can allocate another waiting lobby;
match actions have persisted command receipts, while lobby creation does not yet
have a client creation idempotency key.

## Local verification and packaging

From the repository root:

```powershell
dotnet restore Server/Tactics.Multiplayer.CloudCode/Tests/CloudCodeSmoke.csproj --configfile Server/Tactics.Multiplayer.CloudCode/NuGet.Config --packages Server/Tactics.Multiplayer.CloudCode/.packages
dotnet build Server/Tactics.Multiplayer.CloudCode/Tests/CloudCodeSmoke.csproj --no-restore -m:1 -nr:false
dotnet run --project Server/Tactics.Multiplayer.CloudCode/Tests/CloudCodeSmoke.csproj --no-restore --no-build
& ./Server/Tactics.Multiplayer.CloudCode/Package.ps1
```

The SDK smoke uses official SDK interfaces with a fake Cloud Save service. It
checks private access, service-token use, strict update locks, refusal to overwrite
an existing creation record, module identity enforcement, embedded content,
persisted receipt retry across service invocations and sanitized ambiguous errors.
It makes no authenticated remote service call.

`Package.ps1` restores packages into the workspace cache, publishes for
`linux-x64` without bundling the runtime, and creates a fresh local
`artifacts/<build UUID>/TacticsMultiplayer.ccm`. No existing output is deleted.
It does not log in, deploy, create a cloud project or change billing. The module
was explicitly deployed to **development** on 2026-10-13 and passed real two-client
UGS checks plus Unity runtime SDK and disk recovery checks. Public deployment
metadata is in `Server/Deployment/development.json`; the implementation report is
in `docs/MULTIPLAYER-IMPLEMENTATION.md`. Production and billing were unchanged.

Official references:

- [Cloud Code module structure and execution context](https://docs.unity.com/en-us/cloud-code/modules/how-to-guides/module-structure).
- [Cloud Code C# service integration](https://docs.unity.com/en-us/cloud-code/modules/how-to-guides/unity-services-integration).
- [Cloud Save from Cloud Code](https://docs.unity.com/en-us/cloud-save/tutorials/cloud-code).
- [Cloud Save write locks and creation semantics](https://docs.unity.com/en-us/cloud-save/concepts/write-locks).
- [Cloud Save Custom Data access classes and size limits](https://docs.unity.com/en-us/cloud-save/concepts/game-data).
- [Cloud Code linux-x64 packaging workflow](https://docs.unity.com/en-us/cloud-code/modules/how-to-guides/manual-workflow).
