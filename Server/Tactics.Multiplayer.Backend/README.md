# Persistent multiplayer backend

`DurableMatchService` runs the shared `Tactics.Core` rules in .NET 8. All maps and
unit catalogues come from the server allowlist; requests contain selections and
actions, never a submitted board or player identity.

Private 1v1 lifecycle:

1. `CreateAsync(authenticatedPlayerId, MatchCreateRequest)` creates a waiting
   record with the creator in slot zero. The server-generated lowercase UUID
   is the invitation code.
2. `JoinAsync(authenticatedPlayerId, MatchJoinRequest)` reserves slot one and
   initializes the match in one compare-and-swap. Repeating the same join does
   not repeat initialization or opening income.
3. `GetLobbyAsync` returns waiting/active/completed status to participants.
   `ReadAsync` returns a public snapshot for a started match.
4. `SubmitAsync` validates authentication, exact command identity and revision,
   executes the shared rules, then writes state, revision and compact receipt
   together. An old receipt returns its original outcome with the current
   public snapshot, so clients never roll back to historical boards.

`IMatchStore` uses an opaque write lock for every update. A false CAS must mean
the write definitely did not happen. Store failures with an uncertain outcome
must throw: the client keeps the original command ID and safely retries it.
The coordinator retries conflicts at most eight times. The production adapter
must document any weaker creation primitive; creation keys are generated solely
by the server, with UUID collision resistance, and are never client supplied.

Each private match is one record, with at most 4096 command receipts and 4 MiB of
raw JSON. Limits refuse new commands instead of forgetting receipts. Receipts
include rejected valid actions, so a rejected request cannot become a later
accepted action when repeated. Active records validate every required field,
including the RNG even when zero, and pin rules, catalogue and map versions.
Deploy matching versioned content while old matches remain active.

`FileMatchStore` is a local verification provider using exclusive lock files and
atomic replacement. It demonstrates persistence and concurrency across service
instances; it does not deploy a cloud service. Run the executable verifier:

```powershell
dotnet run --project Tools/PersistentMultiplayerSmoke/PersistentMultiplayerSmoke.csproj
```

The verifier exercises invitation races, separate service instances, persisted
combat, a restart followed by an identical retry, slot spoofing, concurrent
actions, absent RNG/schema fields, pinned metadata corruption and receipt limits.
Its generated records stay under the ignored executable output directory.
