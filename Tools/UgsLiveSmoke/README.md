# Live development smoke test

This .NET 8 executable uses the installed official Unity client SDK's REST
contracts without requiring Unity or administrative credentials. A normal run
only builds and prints usage. Real requests require `--run-live` and public
project/environment configuration after the development module is deployed.

```powershell
dotnet run --project Tools/UgsLiveSmoke/UgsLiveSmoke.csproj
dotnet run --project Tools/UgsLiveSmoke/UgsLiveSmoke.csproj -- --run-live --project-id <UUID> --environment <name> --environment-id <UUID>
```

The actual run creates two fresh anonymous player profiles and one private match
in the selected environment. Tokens remain in process memory and are never
printed or written to disk. The verifier checks the `envId:` JWT audience against
the supplied environment ID before any module invocation. It uses at most 50
HTTP requests and performs no repeated polling.

The verification covers private create/join, outsider isolation, duplicate join,
confirmed wait/end-turn actions, slot spoofing, stale revision, two concurrent
real module calls, and an exact command retry using a new HTTP client with the
same authenticated identity. Historical receipts must return the current board.
Every public response is checked for accidental RNG/identity/receipt leakage.
Private RNG is deliberately not observable; this proves public state stability,
not direct access to or comparison of the server's random stream.

Sources used to construct requests in the installed official SDK:

- Authentication `Runtime/Network/AuthenticationNetworkClient.cs`: public auth
  host, anonymous path, `ProjectId` and `UnityEnvironment` headers.
- Authentication `Runtime/AuthenticationServiceInternal.cs`: environment ID from
  the access token audience's `envId:` entry.
- Cloud Code `Runtime/com.unity.services.cloudcode.internal/Apis/CloudCodeApiRequests.cs`:
  module URL and bearer authorization.
- Cloud Code `Models/RunModuleArguments.cs` and `Models/RunModuleResponse.cs`:
  `{ "params": ... }` input and `{ "output": ... }` output.

Anonymous accounts and the match are development test artifacts. This tool does
not delete them automatically or modify production environments.
