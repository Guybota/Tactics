# Multiplayer foundation smoke

Runs the actual scene-independent C# rules and multiplayer classes as a .NET 8
console application. No Unity Editor, external NuGet packages or cloud account is
required. The local NuGet configuration clears package sources.

From the repository root:

```powershell
dotnet restore Tools/MultiplayerSmoke/MultiplayerSmoke.csproj --configfile Tools/MultiplayerSmoke/NuGet.Config
dotnet run --project Tools/MultiplayerSmoke/MultiplayerSmoke.csproj --no-restore
```

The process fails with a nonzero exit code if any assertion fails. It exercises
catalogue JSON export/import, headless map initialization, state ownership,
authenticated actions from two online sessions, a response lost after commit,
idempotent retry, stale requests, identity spoofing, concurrent commands, client
snapshot ordering, RNG privacy and exact private-state restore.

When `Server/Content/match-content.json` exists, the proof also loads the real
Unity-exported catalogue with the .NET serializer, validates every damage matchup
and recruit, and checks that JSON and `ToContent`/`FromContent` roundtrips preserve
its published version and a normalized SHA-256 digest. An absent default file is
reported as skipped; the synthetic fixture still runs. Pass an explicit catalogue
path after `--` to require testing another export.

The transport is an **in-process fake**; request envelopes and responses pass
through field-based JSON serialization. Requests decode `MatchCommandData` before
reaching the authenticated authority. This does not demonstrate real networking, Cloud Code deployment,
authentication-provider integration or durable command receipts. The authority's
lock serializes requests in one process only. A production backend must persist
state, revision and receipts atomically and obtain identity from authentication.
