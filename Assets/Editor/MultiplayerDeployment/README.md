# Explicit development deployment and Unity SDK check

These Editor helpers run only when explicitly invoked. There are no startup hooks.
The supported project and environment are pinned to the public configuration in
`Server/Deployment/development.json`. They neither modify production nor configure billing.

`UgsMultiplayerDeployment.Inspect()` reads the project's environments and deployment
metadata. `EnsureDevelopment()` creates the nondefault development environment if
absent. `Deploy(archivePath, expectedSha256, environmentId)` checks the archive digest
and target environment before creating the module. It refuses to overwrite an
existing module with a different digest. Inspect progress through
`UgsMultiplayerDeployment.Status`. Administrative credentials stay in memory.

To run the client check, enter Play Mode and explicitly call
`UgsUnityClientSmoke.Begin()`, then inspect `UgsUnityClientSmoke.Status` until
`phase` is `passed` or `failed`. Leave Play Mode after completion.

The check uses the game's real Unity UGS SDK client and its authenticated profile,
creates one additional anonymous guest and one development match, exchanges turns,
simulates a lost action reply, restores the account-scoped disk journal and retries
the same command. It also reopens through `UgsMatchConnector`. The guest credentials
remain in memory; SDK credentials retain their normal SDK-managed persistence.
The public journal remains under the game's `Application.persistentDataPath/OnlineMatches`.
Development accounts and matches are test artifacts and are not deleted automatically.
These checks consume ordinary UGS service usage.

`UgsOnlineBoardSmoke.Begin(matchId)` explicitly opens an existing active development
test match through the actual scene handoff, checks confirmed turn/income/revision,
and advances a turn only if the current SDK identity owns it. It then verifies the
server revision and blocks an attempt to play the opponent's turn.

For the full menu check, first create a test lobby using the Online menu. After a
Play Mode restart, `UgsOnlineBoardSmoke.CompleteMenuRoundtrip()` resumes that last
waiting invitation, joins a fresh anonymous test guest, refreshes and opens through
the menu, and submits a board turn. Read `UgsOnlineBoardSmoke.Status` for its result.
It consumes ordinary UGS requests and deliberately keeps development test artifacts.
