using System;
using System.Collections.Generic;
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
    private RectTransform overlay, panel, setup, lobbyView;
    private TMP_Text heading, description, status;
    private TMP_InputField invitation, lobbyInvitation;
    private Button create, join, resume, build, storm, copy, refresh, open, another;
    private readonly List<Button> raceButtons = new List<Button>();
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
        if (overlay != null) return;
        menu = target ?? throw new ArgumentNullException(nameof(target));
        connection = onlineConnection ?? new Connection();
        launch = openMatch ?? (Action<MatchLobbyReply, OnlineMatchSession>)OnlineMatchRuntime.Enter;
        BuildViews();
        InstallMenuButton();
        Hide();
    }

    public void Show()
    {
        if (overlay == null) return;
        viewVersion++;
        overlay.gameObject.SetActive(true);
        overlay.SetAsLastSibling();
        nextPoll = Time.unscaledTime + 5;
        ShowLobby(lobby);
        if (lobby == null) SetStatus("Create a private match, or enter an invitation from a friend.");
        UpdateControls();
        FitPanel();
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

    private void LateUpdate() { if (IsVisible) FitPanel(); }

    private void FitPanel()
    {
        var canvas = overlay.GetComponentInParent<Canvas>().rootCanvas.transform as RectTransform;
        if (canvas == null) return;
        float scale = Mathf.Min(1, Mathf.Max(0.1f, (canvas.rect.width - 40) / 900), Mathf.Max(0.1f, (canvas.rect.height - 40) / 880));
        panel.localScale = Vector3.one * scale;
    }

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
            ((Image)raceButtons[index].targetGraphic).color = index == raceIndex ? new Color(.78f, .90f, 1) : Color.white;
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
        status.color = error ? new Color(.55f, .13f, .08f) : CustomMapUi.Ink;
    }

    private void InstallMenuButton()
    {
        Button template = null;
        foreach (var candidate in menu.firstMenu.GetComponentsInChildren<Button>(true))
            for (int index = 0; index < candidate.onClick.GetPersistentEventCount(); index++)
                if (candidate.onClick.GetPersistentMethodName(index) == "PressPlay") template = candidate;
        if (template == null) throw new InvalidOperationException("The main menu Play button is missing.");
        var online = Instantiate(template, template.transform.parent);
        online.name = "Online multiplayer button";
        // Remove cloned persistent listeners too; RemoveAllListeners only removes runtime listeners.
        online.onClick = new Button.ButtonClickedEvent();
        online.onClick.AddListener(menu.PressOnline);
        online.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
        online.GetComponentInChildren<TMP_Text>().text = "Online";
    }

    private void BuildViews()
    {
        var canvas = menu.firstMenu.GetComponentInParent<Canvas>().rootCanvas;
        overlay = CustomMapUi.Rect(canvas.transform, "Online match menu", Vector2.zero, Vector2.zero);
        overlay.gameObject.SetActive(false);
        CustomMapUi.Stretch(overlay);
        CustomMapUi.Background(overlay, new Color(0, 0, 0, .7f));
        var sort = overlay.gameObject.AddComponent<Canvas>(); sort.overrideSorting = true; sort.sortingOrder = 90;
        overlay.gameObject.AddComponent<GraphicRaycaster>();
        overlay.gameObject.AddComponent<UiBackAction>().Back = menu.PressBack;
        panel = CustomMapUi.Rect(overlay, "Online menu paper", new Vector2(900, 880), Vector2.zero);
        CustomMapUi.PaperBackground(panel);
        heading = CustomMapUi.Label(panel, "Online menu title", "Online matches", new Vector2(780, 65), new Vector2(0, 375), 54);
        description = CustomMapUi.Label(panel, "Online menu description", "", new Vector2(780, 85), new Vector2(0, 310), 30);
        setup = CustomMapUi.Rect(panel, "Online match setup", Vector2.zero, Vector2.zero); CustomMapUi.Stretch(setup);
        int count = MainMenu.classes.Count;
        float width = 760f / Mathf.Max(1, count);
        for (int index = 0; index < count; index++)
        {
            int selected = index;
            raceButtons.Add(ActionButton(setup, "Online army " + index, MainMenu.classes[index].DisplayName,
                new Vector2(width - 10, 70), new Vector2(-380 + width * (index + .5f), 220),
                () => { raceIndex = selected; UpdateControls(); }));
        }
        build = ActionButton(setup, "Online build mode", "", new Vector2(350, 70), new Vector2(-185, 140),
            () => { building = !building; if (!building) battleRoyale = false; UpdateControls(); });
        storm = ActionButton(setup, "Online storm mode", "", new Vector2(350, 70), new Vector2(185, 140),
            () => { battleRoyale = !battleRoyale; if (battleRoyale) building = true; UpdateControls(); });
        create = ActionButton(setup, "Online create match", "Create match", new Vector2(720, 70), new Vector2(0, 55), () => { _ = CreateAsync(); });
        CustomMapUi.Label(setup, "Invitation prompt", "Have an invitation?", new Vector2(720, 45), new Vector2(0, -25), 32);
        invitation = InvitationInput(setup, "Online invitation input", new Vector2(530, 70), new Vector2(-95, -85), "Paste invitation");
        join = ActionButton(setup, "Online join match", "Join", new Vector2(170, 70), new Vector2(280, -85), () => { _ = JoinAsync(); });
        resume = ActionButton(setup, "Online resume match", "Resume last match", new Vector2(720, 70), new Vector2(0, -175), () => { _ = ResumeAsync(); });
        lobbyView = CustomMapUi.Rect(panel, "Online waiting lobby", Vector2.zero, Vector2.zero); CustomMapUi.Stretch(lobbyView);
        CustomMapUi.Label(lobbyView, "Your invitation label", "Invitation", new Vector2(720, 40), new Vector2(0, 245), 32);
        lobbyInvitation = InvitationInput(lobbyView, "Online lobby invitation", new Vector2(720, 70), new Vector2(0, 195), "");
        lobbyInvitation.readOnly = true;
        copy = ActionButton(lobbyView, "Online copy invitation", "Copy invitation", new Vector2(720, 70), new Vector2(0, 110), CopyInvitation);
        refresh = ActionButton(lobbyView, "Online refresh lobby", "Check lobby / retry", new Vector2(720, 70), new Vector2(0, 25), () => { _ = RefreshLobbyAsync(); });
        open = ActionButton(lobbyView, "Online open board", "Open match", new Vector2(720, 70), new Vector2(0, -60), () => { _ = OpenBoardAsync(); });
        another = ActionButton(lobbyView, "Online another match", "Choose another match", new Vector2(720, 70), new Vector2(0, -175), ChooseAnotherMatch);
        status = CustomMapUi.Label(panel, "Online menu status", "", new Vector2(720, 125), new Vector2(0, -285), 32);
        ActionButton(panel, "Online back", "Back", new Vector2(330, 70), new Vector2(0, -385), menu.PressBack);
        UpdateControls();
    }

    private static Button ActionButton(Transform parent, string name, string caption, Vector2 size, Vector2 position, UnityEngine.Events.UnityAction action)
    {
        var button = CustomMapUi.Button(parent, name, caption, size, position, action);
        TacticsUiSkin.Current.ApplyButton(button);
        ((Image)button.targetGraphic).color = Color.white;
        foreach (var outline in button.GetComponents<Outline>()) outline.enabled = false;
        button.gameObject.AddComponent<PaperButtonLabel>();
        return button;
    }

    private static TMP_InputField InvitationInput(Transform parent, string name, Vector2 size, Vector2 position, string placeholder)
    {
        var input = CustomMapUi.NameInput(parent, size, position);
        input.name = name; input.characterLimit = 64; input.lineType = TMP_InputField.LineType.SingleLine;
        input.textComponent.fontSize = 30;
        ((TMP_Text)input.placeholder).text = placeholder;
        input.GetComponent<Image>().color = new Color(.99f, .92f, .77f);
        input.gameObject.AddComponent<Outline>().effectColor = CustomMapUi.Ink;
        return input;
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
