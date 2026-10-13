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
- `DurableMatchService`: creates private 1v1 invitations, joins and initializes once, checks
  authenticated seats, and commits state/revision/compact command receipts using storage CAS.
- `CloudSaveMatchStore`: stores one private record per match and uses the exact Cloud Save
  write lock for updates. The Cloud Code module supplies authenticated identity from context.
- `UgsMatchTransport` / `UgsCloudModuleClient`: SDK-backed calls to the five module endpoints;
  authentication and service initialization happen only on explicit online operations.
- `OnlineSessionFileJournal`: atomically saves public recovery state and the exact pending
  command before network submission, scoped to project, environment, account and match.
- The Unity controller executes its local actions through `LocalMatchSession`. The explicit
  local scene bridge preserves existing saves, bot integration and legacy component edits.
- `OnlineMatchMenu`: private create/join/resume flow with army selection, optional castle
  building/storm, copyable invitation and an explicit open-board action.
- `OnlineMatchRuntime`: passes the confirmed session and authenticated player slot to the
  match scene. Online projection checks the local catalogue digest against the server version.
- Online controller projection builds the server terrain/participants and applies only
  confirmed snapshots. Human actions await server replies; only the authenticated slot's
  turn is controllable. Bots, local turn initialization and local save replacement are blocked.
- An online HUD offers refresh, retry of the same pending command and invitation copying.
  The board polls every five seconds while active, backs off on failure, and pauses automatic
  polling during menus, pending actions, combat animations and blocked input.

The scene bridge is temporary: `LocalSession` imports local component data before reading or
executing actions. A remote session must never import this data. The SDK transport and
recovery connector and online scene/menu integration are implemented. Opening the Online
menu itself does not initialize services or make network calls. A waiting lobby polls only
while its panel is visible, with five-second intervals and up to thirty seconds after errors.

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

## Backend verification and deployment package

`Server/Tactics.Multiplayer.Backend` contains the storage-independent coordinator and a
local file provider. `Server/Tactics.Multiplayer.CloudCode` contains the deployable C# module
and its actual SDK adapter. See their READMEs for detailed build and storage semantics.

```powershell
dotnet run --project Tools/PersistentMultiplayerSmoke/PersistentMultiplayerSmoke.csproj
dotnet run --project Server/Tactics.Multiplayer.CloudCode/Tests/CloudCodeSmoke.csproj --no-restore
& ./Server/Tactics.Multiplayer.CloudCode/Package.ps1
```

The package command creates a Linux-compatible `.ccm` archive under a fresh local build
directory, with the real catalogue and `default-1v1` map embedded. It does not publish it.
SDK packages are pinned: Unity Authentication 3.8.0, Unity Cloud Code 2.10.4; server SDK
CloudCode.Core 0.0.7 and CloudCode.Apis 0.0.27. No billing settings have been changed.

The module exposes `CreateMatch`, `JoinMatch`, `GetLobby`, `GetMatch` and `SubmitAction`.
`UgsMatchConnector.OpenAsync` restores the account-scoped journal, refreshes confirmed state,
and retries an uncertain action using its original command ID. It leaves unknown actions
recoverable when storage or network confirmation fails.

## Next implementation boundary

1. Add persistent player match lists, readiness and lobby cleanup. This increment resumes
   the last invitation scoped to project/environment/account, or reopens another by invitation.
2. Add creation request idempotency, account linking and explicit content-version retention
   before a public beta. Joining currently starts the two-player match immediately.

The private-state reader verifies required fields, including `RandomState`, and rejects
duplicate JSON keys. A missing RNG field is not interpreted as a genuine zero random state.

`AuthoritativeMatch` remains an in-memory verification implementation. The durable backend
has a 4096-receipt/4-MiB raw-record bound and refuses excess commands instead of deleting
receipts. Cloud Save creation relies on server-generated UUID uniqueness plus an absence
check; it does not provide a documented atomic create-if-absent operation. Updates use CAS.
The SDK/module unit tests use a fake cloud service; local file tests verify persistence and
concurrency. Live UGS checks now complement those tests.

Verified for this increment: 77 multiplayer EditMode cases, 26 local action/save/bot
PlayMode cases, 33 persistent-backend assertions, 20 official-SDK adapter/module assertions,
and successful server builds without warnings or errors.

## Development deployment and live verification — 2026-10-13

`TacticsMultiplayer` is published in the project's new **development** environment.
The existing production environment and billing configuration were not changed.
Public configuration, archive digest and test match identifiers are recorded in
[`Server/Deployment/development.json`](../Server/Deployment/development.json).

The standalone `Tools/UgsLiveSmoke` verifier passed **43 assertions using 20 HTTP
requests** against real Authentication, Cloud Code and private Cloud Save storage.
It exercised two authenticated players, access isolation, stale/spoofed actions,
concurrent writes, duplicate requests and retry from a new HTTP client.

An additional check ran in Unity Play Mode through the actual `UgsCloudModuleClient`,
`UgsMatchTransport`, `OnlineMatchSession`, `OnlineSessionFileJournal` and
`UgsMatchConnector`. It created and joined a real match, simulated response loss
after a committed action, restored the pending command from disk, retried it once,
exchanged both players' turns, and reopened the confirmed session. The final state
was revision **4**, turn **2**, with no pending command.

To repeat the checks explicitly, see `Tools/UgsLiveSmoke/README.md` and
`Assets/Editor/MultiplayerDeployment/README.md`. They create development test
accounts/matches and incur ordinary UGS usage. Nothing runs automatically.
## Playable private 1v1 flow — 2026-10-13

1. On the main menu, choose **Online**, select an army and **Create match**.
2. Use **Copy invitation** and pass it to the other player. In their Online menu,
   they paste it and choose **Join**. Use separate computers or browser profiles;
   two instances sharing the same cached anonymous identity are the same player.
3. Each player chooses **Open match** when the lobby is active. Only the player's own
   turn accepts actions; opponent changes appear through the board's refresh/polling.
4. After returning to the main menu or restarting, choose **Resume last match**.
   If an action's reply was lost, the exact pending command is preserved on disk;
   use **Retry** on the board or reopen the match to resolve it safely.

This flow uses **development**, the server's `default-1v1` map and the anonymous
UGS profile cached on that installation. Account linking and a complete match list
remain future work. Losing that anonymous profile loses its access to existing matches.
Local save/load cannot replace or export an online game; online progress is automatic.
Creation does not yet have an idempotency key, so an ambiguous creation retry can
allocate a second waiting lobby. Action retries are idempotent.

Verified for this scene/menu increment: **41 EditMode tests** and **37 PlayMode tests**
passed. They cover immutable match identity, recovery, delayed/rejected actions,
ownership, confirmed attack presentation, menu entry/back/input/layout and existing
local action/save/bot behavior. These targeted suites are separate from the live checks.

The live board check reopened the earlier SDK match through the scene handoff, submitted
a turn and checked the persisted server response at revision **5**, turn **3**. The full
menu check created a new lobby via the real UI operation, resumed its saved invitation,
joined a second authenticated guest, refreshed/opened through the menu and submitted
a board turn. The resulting server state was revision **2**, turn **1**. Both checks passed
against development. Screenshots were inspected for menu layout and HUD overlap.

The client now rejects replies changing the map size, participant races, immutable
terrain/rules or existing unit identities before persisting them. Dynamic recruits,
deaths, castle ownership and storm flags remain valid confirmed changes.

## Editable online UI — 2026-10-13

Every online visual is now authored in the scene before Play Mode. In **Main Menu**,
edit **Online multiplayer button** inside the existing first menu, or expand
**Online match menu → Online menu paper → Viewport → Online menu content** for
the setup, army selectors, invitation fields, waiting lobby, status and Back button.
The overlay and waiting lobby start inactive; enable them temporarily to preview edits.
In **Main Scene**, edit **Online status** and its status/Refresh/Retry/Copy children.
It also starts inactive and is enabled when an online board opens.

`MainMenu`, `OnlineMatchMenu`, the board controller and `OnlineMatchHud` expose their
scene references in the Inspector. Button On Click callbacks are persistent and visible.
Runtime code changes match data and control availability while preserving the authored
RectTransforms, fonts, sprites and base colors. Native layout/scroll components handle
smaller screens with Transform Scale at one; adjust the paper's LayoutElement preferred
dimensions for its outer size. The explicit editor menu **Tactics → UI → Add editable
online interface to scenes** only authors missing views and leaves existing views intact.

Verification for this authoring change: **2 EditMode tests** confirm that every menu/HUD
reference exists in the saved scenes; **12 PlayMode tests** cover online menu/board
integration, native layouts, persistent callbacks and preservation of manual visual edits.

## WebGL publication verification — 2026-10-13

The WebGL release from source commit `e1b662151f6a30401a7b2a233421ac83632e48b1`
built successfully using **Web - Desktop - Release**: zero build errors, 20.08 MiB.
The compiler reported existing C#/package shader warnings; this is not a warning-free build.

Before publishing, `Tools/PrepareItchWeb.ps1` explicitly enables Unity's
`config.autoSyncPersistentDataPath` in the generated HTML and writes public source
metadata to `release.json`. This makes recovery files persist to browser IndexedDB
on changes. Browser storage can still be disabled/cleared by the user; autosync
does not guarantee a write has completed if the browser terminates immediately.

Two browser clients on distinct localhost origins created/joined a real development
match, opened the same board, exchanged both turns and observed synchronized state.
Reloading the first client retained its authenticated identity and invitation;
**Resume last match** reopened the confirmed board at round 2 with 2000 money.

Browser copy behavior: Unity's copy button sets its internal copy buffer. Press
**Ctrl+C** (or **Cmd+C** on macOS) after **Copy invitation** to copy it to the system
clipboard, then paste into the other client's invitation field. This keyboard path
was verified; the button alone did not populate the browser clipboard during the test.

The build was uploaded with Butler to `guybota/lords-of-conflict:web`, user version
`multiplayer-20261013-e1b6621`, build `2120142`, replacing the prior WebGL build.
The multiplayer backend continues to use **development**.

Publication status checked at **2026-10-13 14:48 UTC**: Butler reports build
`2120142` as `queued`. The current completed channel head is still `2116176`;
the public browser page was observed serving that earlier build. Upload and Git
push succeeded; browser activation is pending itch.io's processing queue.
