using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Tactics.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The menu can be exercised with a fake connection without signing in or polling UGS.
public interface IOnlineMatchMenuConnection
{
    string PlayerId { get; }
    Task InitializeAsync();
    Task<MatchLobbyReply> CreateAsync(MatchCreateRequest request);
    Task<MatchLobbyReply> JoinAsync(MatchJoinRequest request);
    Task<MatchLobbyReply> GetLobbyAsync(string matchId);
    Task<OnlineMatchSession> OpenAsync(string matchId);
}

[DisallowMultipleComponent]
public sealed class OnlineMatchMenu : MonoBehaviour
{
    private const string EnvironmentName = "development";
    private MainMenu menu;
    private IOnlineMatchMenuConnection connection;
    private Action<MatchLobbyReply, OnlineMatchSession> launch;
    [Header("Scene views — edit their RectTransforms and visual components")]
    [SerializeField] private RectTransform overlay, panel, setup, lobbyView;
    [SerializeField] private TMP_Text heading, description, status;
    [SerializeField] private TMP_InputField invitation, lobbyInvitation;
    [SerializeField] private ScrollRect viewScroll;
    [SerializeField] private Button create, join, resume, build, storm, copy, refresh, open, another;
    [Tooltip("Same order as the main menu's race catalogue.")]
    [SerializeField] private List<Button> raceButtons = new List<Button>();
    [SerializeField] private Color selectedArmyTint = new Color(.78f, .90f, 1);
    [SerializeField] private Color errorColor = new Color(.55f, .13f, .08f);
    private Color normalStatusColor;
    private Color[] armyColors;
    private bool initialized;
    private MatchLobbyReply lobby;
    private bool building, battleRoyale, requestRunning;
    private int raceIndex, viewVersion, pollFailures;
    private float nextPoll;

    public bool IsVisible => overlay != null && overlay.gameObject.activeSelf;
    public bool IsBusy => requestRunning;
    public MatchLobbyReply CurrentLobby => lobby;

    public void Initialize(MainMenu target, IOnlineMatchMenuConnection onlineConnection = null,
        Action<MatchLobbyReply, OnlineMatchSession> openMatch = null)
    {
        if (initialized) return;
        if (overlay == null || panel == null || setup == null || lobbyView == null || heading == null
            || description == null || status == null || invitation == null || lobbyInvitation == null
            || new[] { create, join, resume, build, storm, copy, refresh, open, another }.Any(b => b == null)
            || raceButtons.Count != MainMenu.classes.Count || raceButtons.Any(b => b == null))
            throw new InvalidOperationException("The editable Online UI references are missing from this scene.");
        menu = target ?? throw new ArgumentNullException(nameof(target));
        connection = onlineConnection ?? new Connection();
        launch = openMatch ?? (Action<MatchLobbyReply, OnlineMatchSession>)OnlineMatchRuntime.Enter;
        normalStatusColor = status.color;
        armyColors = raceButtons.Select(b => ((Image)b.targetGraphic).color).ToArray();
        overlay.GetComponent<UiBackAction>().Back = menu.PressBack;
        initialized = true;
        ShowLobby(null);
        UpdateControls();
        Hide();
    }

    public void Show()
    {
        if (overlay == null) return;
        viewVersion++;
        overlay.gameObject.SetActive(true);
        overlay.SetAsLastSibling();
        if (viewScroll != null) { viewScroll.StopMovement(); viewScroll.horizontalNormalizedPosition = .5f; viewScroll.verticalNormalizedPosition = 1; }
        nextPoll = Time.unscaledTime + 5;
        ShowLobby(lobby);
        if (lobby == null) SetStatus("Create a private match, or enter an invitation from a friend.");
        UpdateControls();
    }

    public void Hide()
    {
        viewVersion++;
        if (overlay != null) overlay.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!IsVisible || requestRunning || lobby == null || lobby.Status != MatchLobbyStatus.Waiting
            || Time.unscaledTime < nextPoll || MobileUiMode.UIInputBlocked) return;
        _ = RefreshLobbyAsync();
    }

    // Persistent callbacks are visible on the scene Buttons' On Click events.
    public async void CreateFromButton() => await CreateAsync();
    public async void JoinFromButton() => await JoinAsync();
    public async void ResumeFromButton() => await ResumeAsync();
    public async void RefreshFromButton() => await RefreshLobbyAsync();
    public async void OpenFromButton() => await OpenBoardAsync();
    public void ChooseArmy(int index) { if (requestRunning) return; raceIndex = Mathf.Clamp(index, 0, raceButtons.Count - 1); UpdateControls(); }
    public void ToggleBuildMode() { building = !building; if (!building) battleRoyale = false; UpdateControls(); }
    public void ToggleStormMode() { battleRoyale = !battleRoyale; if (battleRoyale) building = true; UpdateControls(); }

    public async Task CreateAsync()
    {
        if (!BeginRequest(out int version)) return;
        SetStatus("Creating your match...");
        try
        {
            var race = SelectedRace();
            var result = await connection.CreateAsync(new MatchCreateRequest { SchemaVersion = MatchCreateRequest.CurrentVersion,
                MapId = "default-1v1", RaceId = race.Id, BuildMode = building, BattleRoyaleMode = battleRoyale });
            AcceptLobby(result, version);
        }
        catch (Exception) { Failure(version, "Could not confirm creation. Check your connection and try again."); }
        finally { EndRequest(); }
    }

    public async Task JoinAsync()
    {
        string matchId = NormalizeInvitation(invitation.text);
        if (matchId == null) { SetStatus("Enter the 32-character invitation copied by your friend.", true); return; }
        if (!BeginRequest(out int version)) return;
        SetStatus("Joining the match...");
        try
        {
            var result = await connection.JoinAsync(new MatchJoinRequest { SchemaVersion = MatchJoinRequest.CurrentVersion,
                MatchId = matchId, RaceId = SelectedRace().Id });
            AcceptLobby(result, version);
        }
        catch (Exception) { Failure(version, "Could not join. Check your connection and try again."); }
        finally { EndRequest(); }
    }

    public async Task ResumeAsync()
    {
        if (!BeginRequest(out int version)) return;
        SetStatus("Finding your last online match...");
        try
        {
            await connection.InitializeAsync();
            string matchId = PlayerPrefs.GetString(InvitationKey(), "");
            if (NormalizeInvitation(matchId) == null)
            { Failure(version, "No saved invitation for this player. Enter an invitation to rejoin a match."); return; }
            AcceptLobby(await connection.GetLobbyAsync(matchId), version);
        }
        catch (Exception) { Failure(version, "Could not resume. Your invitation was kept; try again."); }
        finally { EndRequest(); }
    }

    public async Task RefreshLobbyAsync()
    {
        if (lobby == null || !BeginRequest(out int version)) return;
        string matchId = lobby.MatchId;
        try
        {
            var result = await connection.GetLobbyAsync(matchId);
            if (result != null && result.Success) pollFailures = 0;
            else pollFailures++;
            AcceptLobby(result, version);
        }
        catch (Exception)
        { pollFailures++; Failure(version, "Connection interrupted. Your invitation is saved. Retry the lobby."); }
        finally
        {
            nextPoll = Time.unscaledTime + Mathf.Min(30, 5 * Mathf.Pow(2, Mathf.Min(pollFailures, 3)));
            EndRequest();
        }
    }

    public async Task OpenBoardAsync()
    {
        if (lobby == null || lobby.Status == MatchLobbyStatus.Waiting || !BeginRequest(out int version)) return;
        var confirmedLobby = lobby;
        SetStatus("Opening your match...");
        try
        {
            var session = await connection.OpenAsync(confirmedLobby.MatchId);
            if (Alive(version)) launch(confirmedLobby, session);
        }
        catch (Exception) { Failure(version, "Could not open the match. Your recovery data was kept. Try again."); }
        finally { EndRequest(); }
    }

    public void CopyInvitation()
    {
        if (lobby == null) return;
        GUIUtility.systemCopyBuffer = lobby.MatchId;
        SetStatus("Invitation copied. Send it to the other player.");
    }

    public void ChooseAnotherMatch()
    {
        if (requestRunning) return;
        lobby = null; pollFailures = 0; viewVersion++;
        ShowLobby(null);
        SetStatus("Your last invitation is saved. Create or join another match.");
        UpdateControls();
    }

    private bool BeginRequest(out int version)
    {
        version = viewVersion;
        if (!IsVisible || requestRunning) return false;
        requestRunning = true;
        UpdateControls();
        return true;
    }

    private void EndRequest()
    {
        requestRunning = false;
        if (this != null && overlay != null) UpdateControls();
    }

    private bool Alive(int version) => this != null && IsVisible && version == viewVersion;
    private void Failure(int version, string message) { if (Alive(version)) SetStatus(message, true); }

    private void AcceptLobby(MatchLobbyReply reply, int version)
    {
        if (reply == null) { Failure(version, "The lobby could not be read. Try again."); return; }
        reply.Validate();
        if (!reply.Success) { Failure(version, reply.Error ?? "The match is unavailable. Check the invitation and retry."); return; }
        // Save an invitation even if the user closed the panel while the request ran.
        Remember(reply.MatchId);
        if (!Alive(version)) return;
        lobby = reply;
        nextPoll = Time.unscaledTime + 5;
        ShowLobby(reply);
        UpdateControls();
    }

    private void Remember(string matchId)
    {
        string key = InvitationKey();
        if (PlayerPrefs.GetString(key, "") == matchId) return;
        PlayerPrefs.SetString(key, matchId);
        PlayerPrefs.Save();
    }

    private string InvitationKey()
    {
        if (string.IsNullOrWhiteSpace(connection.PlayerId)) throw new InvalidOperationException("Player authentication is required.");
        using (var hash = SHA256.Create())
        {
            string scope = Application.cloudProjectId + "\n" + EnvironmentName + "\n" + connection.PlayerId;
            return "Tactics.LastOnlineInvitation." + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(scope))).Replace("-", "");
        }
    }

    private RaceClass SelectedRace()
    {
        if (MainMenu.classes == null || MainMenu.classes.Count == 0) throw new InvalidOperationException("No armies are configured.");
        return MainMenu.classes[Mathf.Clamp(raceIndex, 0, MainMenu.classes.Count - 1)];
    }

    private static string NormalizeInvitation(string input)
    {
        string value = (input ?? "").Trim().ToLowerInvariant();
        return Guid.TryParseExact(value, "N", out var id) ? id.ToString("N") : null;
    }

    private void ShowLobby(MatchLobbyReply reply)
    {
        bool hasLobby = reply != null;
        setup.gameObject.SetActive(!hasLobby); lobbyView.gameObject.SetActive(hasLobby);
        heading.text = hasLobby ? "Private match" : "Online matches";
        description.text = hasLobby ? "Clareira · 1v1\nYou are player " + (reply.PlayerSlot + 1)
            : "Private 1v1 matches\nYour progress stays saved between turns.";
        if (!hasLobby) return;
        lobbyInvitation.SetTextWithoutNotify(reply.MatchId);
        open.GetComponentInChildren<TMP_Text>().text = reply.Status == MatchLobbyStatus.Completed ? "View result" : "Open match";
        if (reply.Status == MatchLobbyStatus.Waiting)
            SetStatus("Waiting for another player. Copy the invitation and send it to a friend.");
        else if (reply.Status == MatchLobbyStatus.Completed) SetStatus("This match has finished. Open it to see the result.");
        else SetStatus(reply.Snapshot.Turn % reply.Snapshot.Players.Count == reply.PlayerSlot
            ? "Both players are ready. It is your turn." : "Both players are ready. It is the other player's turn.");
    }

    private void UpdateControls()
    {
        if (create == null) return;
        bool editable = !requestRunning;
        create.interactable = join.interactable = resume.interactable = build.interactable = storm.interactable = editable;
        invitation.interactable = editable;
        for (int index = 0; index < raceButtons.Count; index++)
        {
            raceButtons[index].interactable = editable;
            ((Image)raceButtons[index].targetGraphic).color = index == raceIndex ? armyColors[index] * selectedArmyTint : armyColors[index];
        }
        build.GetComponentInChildren<TMP_Text>().text = "Build castles: " + (building ? "On" : "Off");
        storm.GetComponentInChildren<TMP_Text>().text = "Storm: " + (battleRoyale ? "On" : "Off");
        copy.interactable = lobby != null;
        refresh.interactable = another.interactable = editable;
        open.interactable = editable && lobby != null && lobby.Status != MatchLobbyStatus.Waiting;
    }

    private void SetStatus(string message, bool error = false)
    {
        if (status == null) return;
        status.text = message;
        status.color = error ? errorColor : normalStatusColor;
    }

    private sealed class Connection : IOnlineMatchMenuConnection
    {
        private readonly UgsCloudModuleClient client = new UgsCloudModuleClient(EnvironmentName);
        private readonly UgsMatchConnector connector;
        public Connection() { connector = new UgsMatchConnector(client); }
        public string PlayerId => client.PlayerId;
        public Task InitializeAsync() => client.InitializeAsync();
        public Task<MatchLobbyReply> CreateAsync(MatchCreateRequest request) => connector.CreateAsync(request);
        public Task<MatchLobbyReply> JoinAsync(MatchJoinRequest request) => connector.JoinAsync(request);
        public Task<MatchLobbyReply> GetLobbyAsync(string matchId) => connector.Transport.GetLobbyAsync(matchId);
        public Task<OnlineMatchSession> OpenAsync(string matchId) => connector.OpenAsync(matchId);
    }
}
