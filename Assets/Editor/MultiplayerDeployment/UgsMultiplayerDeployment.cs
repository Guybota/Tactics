using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Services.Core.Editor;
using Unity.Services.Core.Editor.Environments;
using UnityEditor;
using UnityEngine.Networking;

// Explicit Editor operations. Never runs automatically and never exposes credentials.
public static class UgsMultiplayerDeployment
{
    private const string ExpectedProject = "a5e13aaf-bbbb-44ff-965e-fc9773bbada6";
    private const string EnvironmentName = "development";
    private const string ModuleName = "TacticsMultiplayer";
    private const string StatusKey = "Tactics.UgsMultiplayerDeployment";
    private static bool running;
    public static string Status => SessionState.GetString(StatusKey, "idle");

    public static void Inspect() => Start(InspectAsync);
    public static void EnsureDevelopment() => Start(EnsureDevelopmentAsync);
    public static void Deploy(string archivePath, string expectedSha256, string expectedEnvironmentId)
        => Start(() => DeployAsync(archivePath, expectedSha256, expectedEnvironmentId));

    private static void Start(Func<Task> action)
    {
        if (running) throw new InvalidOperationException("A deployment operation is already running.");
        RequireProject();
        running = true;
        Set(new { phase = "running" });
        Run(action);
    }

    private static async void Run(Func<Task> action)
    {
        try { await action(); }
        catch (RemoteFailure failure) { Set(new { phase = "failed", httpStatus = failure.StatusCode, error = failure.SafeError }); }
        catch (Exception error) { Set(new { phase = "failed", errorType = error.GetType().Name }); }
        finally { running = false; }
    }

    private static async Task InspectAsync()
    {
        await EnvironmentsApi.Instance.RefreshAsync();
        var environments = EnvironmentsApi.Instance.Environments.ToArray();
        var development = environments.FirstOrDefault(e => e.Name == EnvironmentName);
        if (development.Id == Guid.Empty)
        {
            Set(new { phase = "development_missing", projectId = ExpectedProject,
                environments = environments.Select(e => new { name = e.Name, id = e.Id.ToString(), isDefault = e.IsDefault }) });
            return;
        }
        string token = await Token();
        var result = await Request(UnityWebRequest.Get(ModuleUrl(development.Id)), token, true);
        Set(new { phase = "inspected", projectId = ExpectedProject, environment = EnvironmentName,
            environmentId = development.Id.ToString(), moduleExists = result != null,
            metadata = result == null ? null : PublicMetadata(result) });
    }

    private static async Task EnsureDevelopmentAsync()
    {
        await EnvironmentsApi.Instance.RefreshAsync();
        if (!EnvironmentsApi.Instance.Environments.Any(e => e.Name == EnvironmentName))
        {
            string token = await Token();
            using (var request = new UnityWebRequest("https://services.unity.com/api/unity/v1/projects/" + ExpectedProject + "/environments", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{\"name\":\"development\"}"));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                await Request(request, token);
            }
        }
        await InspectAsync();
    }

    private static async Task DeployAsync(string archivePath, string expectedSha256, string expectedEnvironmentId)
    {
        string file = Path.GetFullPath(archivePath);
        string allowedRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", "Server", "Tactics.Multiplayer.CloudCode", "artifacts")) + Path.DirectorySeparatorChar;
        if (!file.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase) || Path.GetExtension(file) != ".ccm")
            throw new ArgumentException("Archive must be a prepared workspace module.");
        byte[] contents = File.ReadAllBytes(file);
        string digest;
        using (var sha = SHA256.Create()) digest = BitConverter.ToString(sha.ComputeHash(contents)).Replace("-", "").ToLowerInvariant();
        if (!string.Equals(digest, expectedSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The archive changed.");
        await EnvironmentsApi.Instance.RefreshAsync();
        var environment = EnvironmentsApi.Instance.Environments.Single(e => e.Name == EnvironmentName);
        if (environment.IsDefault || environment.Id.ToString() != expectedEnvironmentId)
            throw new InvalidOperationException("Deployment must target the confirmed non-default development environment.");
        string token = await Token();
        var existing = await Request(UnityWebRequest.Get(ModuleUrl(environment.Id)), token, true);
        // Only create a new module here. Replacing one requires a separate reviewed
        // update path with backup; an identical already-deployed archive is harmless.
        if (existing != null)
        {
            if ((string)existing["tags"]?["tacticsArchiveSha256"] != digest)
                throw new InvalidOperationException("A different module already exists in development.");
            Set(new { phase = "already_deployed", environmentId = environment.Id.ToString(), archiveSha256 = digest, metadata = PublicMetadata(existing) });
            return;
        }
        var sections = new List<IMultipartFormSection>
        {
            new MultipartFormDataSection("name", ModuleName),
            new MultipartFormDataSection("language", "CS"),
            new MultipartFormDataSection("tags", new JObject { ["tacticsArchiveSha256"] = digest, ["tacticsDeployment"] = "development" }.ToString(Formatting.None)),
            new MultipartFormFileSection("file", contents, ModuleName + ".ccm", "application/zip")
        };
        await Request(UnityWebRequest.Post(CollectionUrl(environment.Id), sections), token);
        var confirmed = await Request(UnityWebRequest.Get(ModuleUrl(environment.Id)), token);
        Set(new { phase = "deployed", projectId = ExpectedProject, environment = EnvironmentName,
            environmentId = environment.Id.ToString(), archiveSha256 = digest, metadata = PublicMetadata(confirmed) });
    }

    private static string CollectionUrl(Guid environmentId)
        => "https://services.unity.com/api/cloud-code/v1/projects/" + ExpectedProject + "/environments/" + environmentId + "/modules";
    private static string ModuleUrl(Guid environmentId) => CollectionUrl(environmentId) + "/" + ModuleName;
    private static object PublicMetadata(JObject value) => new
    {
        name = (string)value["name"], language = (string)value["language"],
        hasError = (bool?)value["hasError"], errorMessage = (string)value["errorMessage"],
        endpoints = (value["endpoints"] as JObject)?.Properties().Select(p => p.Name).ToArray(),
        modified = (string)value["dateModified"]
    };

    private static async Task<string> Token()
    {
        string value = await new AccessTokens().GetServicesGatewayTokenAsync();
        if (string.IsNullOrEmpty(value)) throw new InvalidOperationException("Unity Editor authentication is required.");
        return value;
    }

    private static async Task<JObject> Request(UnityWebRequest request, string token, bool allowMissing = false)
    {
        using (request)
        {
            request.timeout = 60;
            request.SetRequestHeader("Authorization", "Bearer " + token);
            request.SetRequestHeader("x-client-id", "com.unity.services.cloudcode@2.10.4");
            var operation = request.SendWebRequest();
            var completion = new TaskCompletionSource<bool>();
            operation.completed += _ => completion.TrySetResult(true);
            if (operation.isDone) completion.TrySetResult(true);
            await completion.Task;
            if (allowMissing && request.responseCode == 404) return null;
            string body = request.downloadHandler?.text;
            if (request.result != UnityWebRequest.Result.Success)
            {
                // Do not surface headers, URLs with credentials, or arbitrary body data.
                string safeError = request.error;
                try { var problem = JObject.Parse(body ?? "{}"); safeError = (string)problem["title"] ?? (string)problem["error"] ?? safeError; }
                catch (JsonException) { }
                throw new RemoteFailure(request.responseCode, safeError);
            }
            return string.IsNullOrWhiteSpace(body) ? new JObject() : JObject.Parse(body);
        }
    }

    private static void RequireProject()
    {
        if (CloudProjectSettings.projectId != ExpectedProject) throw new InvalidOperationException("Unexpected Unity project.");
    }
    private static void Set(object value) => SessionState.SetString(StatusKey, JsonConvert.SerializeObject(value));
    private sealed class RemoteFailure : Exception
    {
        public readonly long StatusCode;
        public readonly string SafeError;
        public RemoteFailure(long code, string safeError) { StatusCode = code; SafeError = safeError; }
    }
}
