using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Tactics.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class CustomMapIntegrationTests
{
    private Component menu;
    private bool background;
    private readonly List<string> ownedMaps = new List<string>();
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static Type Script(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static object Get(object target, string field) => target.GetType().GetField(field, Members).GetValue(target);
    private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Members).Invoke(target, args);
    private static Component Find(string name) => (Component)UnityEngine.Object.FindFirstObjectByType(Script(name));
    private object Repository(string root = null) => Activator.CreateInstance(Script("CustomMapRepository"), root ?? Application.persistentDataPath);
    private CustomMapData Data(int players = 2)
    {
        var map = new CustomMapData { id = Guid.NewGuid().ToString("N"), name = "Test map", width = 12, height = 10, playerCount = players };
        for (int i = 0; i < 120; i++) map.cells.Add(new MapCellData());
        for (int i = 0; i < players; i++) map.starts.Add(new MapStartData { slot = i, x = i % 2 == 0 ? 1 : 10, y = i < 2 ? 1 : 8 });
        map.Cell(4, 4).terrain = TerrainKind.Forest; map.Cell(5, 4).terrain = TerrainKind.Mountain;
        map.Cell(6, 4).castle = true; map.Cell(7, 4).castle = true; map.Cell(7, 4).castleOwner = 1;
        return map;
    }
    private void Own(CustomMapData data) => ownedMaps.Add(data.id);
    [UnitySetUp]
    public IEnumerator SetUp()
    {
        background = Application.runInBackground; Application.runInBackground = true;
        SceneManager.LoadScene("Main Menu"); yield return null; yield return null; menu = Find("MainMenu");
    }
    [UnityTearDown]
    public IEnumerator TearDown()
    {
        var files = new SaveFileStore(Path.Combine(Application.persistentDataPath, "Maps"));
        foreach (var id in ownedMaps) files.Delete(id); ownedMaps.Clear();
        SceneManager.LoadScene("Main Menu"); yield return null; yield return null;
        Application.runInBackground = background;
    }
    private IEnumerator Editor()
    {
        Call(menu, "PressStartCreateMap"); yield return null; yield return null;
    }
    private static object Button(GameObject gameObject)
        => gameObject.GetComponent(Type.GetType("UnityEngine.UI.Button, UnityEngine.UI", true));
    private static void Click(GameObject gameObject)
    {
        var button = Button(gameObject);
        var click = button.GetType().GetProperty("onClick").GetValue(button);
        click.GetType().GetMethod("Invoke").Invoke(click, null);
    }
    [UnityTest]
    public IEnumerator MapLibrarySelectsReadyMapsThroughNativeButton()
    {
        var data = Data(3); Own(data); Call(Repository(), "Write", data);
        Call(menu, "PressPlay"); Click(GameObject.Find("Choose map")); yield return null;
        var row = GameObject.Find("Map " + data.id); Assert.That(row, Is.Not.Null);
        Assert.That(Button(row).GetType().GetProperty("interactable").GetValue(Button(row)), Is.True);
        Click(row); yield return null;
        Assert.That(GameObject.Find("Custom maps Overlay"), Is.Null);
        var selected = (CustomMapData)Script("MainMenu").GetProperty("SelectedCustomMap").GetValue(null);
        Assert.That(selected.id, Is.EqualTo(data.id));
        Assert.That(((GameObject)Get(menu, "player3_info")).activeSelf, Is.True);
        Assert.That(((GameObject)Get(menu, "player4_info")).activeSelf, Is.False);
        Assert.That(((Component)Get(menu, "width_slider")).gameObject.activeInHierarchy, Is.False);
    }
    [UnityTest]
    public IEnumerator DraftLibraryEntryIsDisabledForPlayButCanBeLoadedInEditor()
    {
        var data = Data(); data.starts.Clear(); Own(data); Call(Repository(), "Write", data);
        Call(menu, "PressPlay"); Click(GameObject.Find("Choose map")); yield return null;
        var row = GameObject.Find("Map " + data.id);
        Assert.That(Button(row).GetType().GetProperty("interactable").GetValue(Button(row)), Is.False);
        yield return Editor(); var creator = Find("MapCreator"); Call(creator, "ApplyMap", Data());
        Click(GameObject.Find("Load map")); yield return null;
        row = GameObject.Find("Map " + data.id);
        Assert.That(Button(row).GetType().GetProperty("interactable").GetValue(Button(row)), Is.True);
        Assert.That(Find("MapEditorMenu").GetType().GetProperty("IsModalOpen").GetValue(Find("MapEditorMenu")), Is.True);
        Click(row); yield return null;
        Assert.That(((CustomMapData)Call(creator, "CaptureMap")).starts, Is.Empty);
        Assert.That(Find("MapEditorMenu").GetType().GetProperty("IsModalOpen").GetValue(Find("MapEditorMenu")), Is.False);
    }
    [UnityTest]
    public IEnumerator BotPlaysCustomMapThroughSharedExecutorAndReturnsHumanTurn()
    {
        Call(menu, "SelectCustomMap", Data()); Script("MainMenu").GetMethod("SetBotPlayer").Invoke(null, new object[] { 0, true });
        Call(menu, "PressStart"); yield return null; yield return null;
        var controller = Find("scr_game_contoller"); int revision = (int)controller.GetType().GetProperty("MatchRevision").GetValue(controller);
        float deadline = Time.realtimeSinceStartup + 12;
        while ((int)Get(controller, "turn") == 0 && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(Get(controller, "turn"), Is.EqualTo(1));
        Assert.That(controller.GetType().GetProperty("MatchRevision").GetValue(controller), Is.GreaterThan(revision));
        Assert.That(controller.GetType().GetProperty("IsBotTurn").GetValue(controller), Is.False);
    }
    [UnityTest]
    public IEnumerator CustomMapPreservesAllThreeModesAndParticipantCounts()
    {
        for (int count = 2; count <= 4; count++) for (int mode = 0; mode < 3; mode++)
        {
            Call(menu, "SelectCustomMap", Data(count));
            Script("MainMenu").GetField("build_mode").SetValue(null, mode > 0);
            Script("MainMenu").GetField("battle_royale_mode").SetValue(null, mode == 2);
            Call(menu, "PressStart"); yield return null; yield return null;
            var state = (MatchState)Call(Find("scr_game_contoller"), "GetStateSnapshot");
            Assert.That(state.Players.Count, Is.EqualTo(count)); Assert.That(state.BuildMode, Is.EqualTo(mode > 0));
            Assert.That(state.BattleRoyaleMode, Is.EqualTo(mode == 2)); Assert.That(state.Cells[7, 4].CastleOwner, Is.EqualTo(1));
            Assert.That(state.Players[1].Money, Is.EqualTo(mode == 0 ? 1000 : 4000));
            SceneManager.LoadScene("Main Menu"); yield return null; yield return null; menu = Find("MainMenu");
        }
    }
    [UnityTest]
    public IEnumerator EditorSavesAndLoadsTerrainStartsAndCastleOwners()
    {
        yield return Editor(); var creator = Find("MapCreator"); var data = Data(4); Own(data);
        Call(creator, "ApplyMap", data); Call(creator, "SetMapName", "Round trip map");
        object[] save = { null }; Assert.That(Call(creator, "TrySaveMap", save), Is.True, (string)save[0]);
        var captured = (CustomMapData)Call(creator, "CaptureMap");
        Assert.That(JsonUtility.ToJson(Call(Repository(), "Read", data.id)), Is.EqualTo(JsonUtility.ToJson(captured)));
        var map = Get(creator, "gameMap"); var tile = Call(map, "GetTileAtPosition", 4, 4);
        Call(creator, "SelectTerrain", "mountain_tile"); Call(creator, "PaintTile", tile);
        object[] load = { data.id, null }; Assert.That(Call(creator, "TryLoadMap", load), Is.True, (string)load[1]);
        Assert.That(JsonUtility.ToJson(Call(creator, "CaptureMap")), Is.EqualTo(JsonUtility.ToJson(captured)));
        Assert.That(creator.GetType().GetProperty("HasUnsavedChanges").GetValue(creator), Is.False);
        Assert.That(GameObject.Find("Participant starts").transform.childCount, Is.EqualTo(4));
    }
    [UnityTest]
    public IEnumerator OwnerButtonControlsNewCastlesAndKeepsAuthoredGeometry()
    {
        yield return Editor(); var creator = Find("MapCreator");
        var button = GameObject.Find("Castle owner"); var rect = (RectTransform)button.transform;
        var position = rect.anchoredPosition; var size = rect.sizeDelta; var scale = rect.localScale;
        var map = Get(creator, "gameMap"); Call(creator, "SelectTerrain", "castle");
        // Trigger the actual button callback; UI type lives in a separate assembly.
        var uiButton = button.GetComponent(Type.GetType("UnityEngine.UI.Button, UnityEngine.UI", true));
        object onClick = uiButton.GetType().GetProperty("onClick").GetValue(uiButton);
        for (int owner = -1; owner <= 3; owner++)
        {
            Call(creator, "PaintTile", Call(map, "GetTileAtPosition", owner + 2, 4));
            Assert.That(((CustomMapData)Call(creator, "CaptureMap")).Cell(owner + 2, 4).castleOwner, Is.EqualTo(owner));
            onClick.GetType().GetMethod("Invoke").Invoke(onClick, null);
        }
        Assert.That(creator.GetType().GetProperty("CastleOwnerBrush").GetValue(creator), Is.EqualTo(-1));
        Call(creator, "SelectTerrain", "forest_tile"); Call(creator, "PaintTile", Call(map, "GetTileAtPosition", 2, 4));
        Assert.That(((CustomMapData)Call(creator, "CaptureMap")).Cell(2, 4).castleOwner, Is.EqualTo(-1));
        Assert.That(rect.anchoredPosition, Is.EqualTo(position));
        Assert.That(rect.sizeDelta, Is.EqualTo(size)); Assert.That(rect.localScale, Is.EqualTo(scale));
    }
    [UnityTest]
    public IEnumerator StartBrushMovesMarkersAndMountainPaintingMakesMapADraft()
    {
        yield return Editor(); var creator = Find("MapCreator"); var data = Data(); Call(creator, "ApplyMap", data);
        var map = Get(creator, "gameMap"); Call(creator, "SelectStart", 0); Call(creator, "PaintTile", Call(map, "GetTileAtPosition", 3, 3));
        var result = (CustomMapData)Call(creator, "CaptureMap"); Assert.That(result.starts.Count, Is.EqualTo(2)); Assert.That(result.StartFor(0).x, Is.EqualTo(3));
        Call(creator, "SelectTerrain", "mountain_tile"); Call(creator, "PaintTile", Call(map, "GetTileAtPosition", 3, 3));
        Assert.That(CustomMapValidator.PlayIssues((CustomMapData)Call(creator, "CaptureMap"))[0], Does.Contain("mountain"));
        Call(creator, "SelectEraseStart"); Call(creator, "PaintTile", Call(map, "GetTileAtPosition", 3, 3));
        Assert.That(((CustomMapData)Call(creator, "CaptureMap")).StartFor(0), Is.Null);
    }
    [UnityTest]
    public IEnumerator MissingMapDoesNotMutateEditor()
    {
        yield return Editor(); var creator = Find("MapCreator"); string before = JsonUtility.ToJson(Call(creator, "CaptureMap"));
        object[] args = { Guid.NewGuid().ToString("N"), null };
        Assert.That(Call(creator, "TryLoadMap", args), Is.False); Assert.That(args[1], Is.Not.Null.And.Not.Empty);
        Assert.That(JsonUtility.ToJson(Call(creator, "CaptureMap")), Is.EqualTo(before));
    }
    [UnityTest]
    public IEnumerator DraftCanBeSavedButMenuRefusesToStartIt()
    {
        var map = Data(); map.starts.Clear(); Own(map); Call(Repository(), "Write", map);
        Call(menu, "PressPlay"); Call(menu, "SelectCustomMap", map); Call(menu, "PressStart"); yield return null;
        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Main Menu")); Assert.That(GameObject.Find("Cannot start map"), Is.Not.Null);
        var read = (CustomMapData)Call(Repository(), "Read", map.id); Assert.That(read.starts, Is.Empty);
    }
    [UnityTest]
    public IEnumerator SwitchingToGeneratedRestoresDimensionsAndParticipantControls()
    {
        Call(menu, "PressPlay"); Call(menu, "SelectCustomMap", Data(4)); yield return null;
        Assert.That((int)Script("MainMenu").GetField("map_width").GetValue(null), Is.EqualTo(12));
        Assert.That(((GameObject)Get(menu, "player4_info")).activeSelf, Is.True);
        Call(menu, "SelectCustomMap", new object[] { null }); yield return null;
        Assert.That(Script("MainMenu").GetProperty("SelectedCustomMap").GetValue(null), Is.Null);
        Assert.That((int)Script("MainMenu").GetField("map_width").GetValue(null), Is.EqualTo(10));
        Assert.That(((Component)Get(menu, "width_slider")).gameObject.activeInHierarchy, Is.True);
        Call(menu, "Remove_player", 4); Assert.That(((GameObject)Get(menu, "player4_info")).activeSelf, Is.False);
    }
    [UnityTest]
    public IEnumerator FourParticipantMapStartsWithChosenRacesBotsAndOwnedCastles()
    {
        var data = Data(4); Call(menu, "SelectCustomMap", data);
        Script("MainMenu").GetMethod("SetBotPlayer").Invoke(null, new object[] { 3, true });
        var races = (IList)Script("MainMenu").GetField("classes").GetValue(null);
        Script("MainMenu").GetMethod("SelectRace").Invoke(null, new object[] { 3, races[1] });
        Call(menu, "PressStart"); yield return null; yield return null;
        var controller = Find("scr_game_contoller"); var teams = (IList)Get(controller, "teams"); Assert.That(teams.Count, Is.EqualTo(4));
        var state = (MatchState)Call(controller, "GetStateSnapshot");
        for (int i = 0; i < 4; i++) Assert.That(state.Players[i].Units[0].Position, Is.EqualTo(new GridPosition(data.StartFor(i).x, data.StartFor(i).y)));
        Assert.That(Get(teams[3], "isBot"), Is.True); Assert.That(Get(Get(teams[3], "raceClass"), "className"), Is.EqualTo(Get(races[1], "className")));
        Assert.That(state.Cells[4, 4].Terrain, Is.EqualTo(TerrainKind.Forest)); Assert.That(state.Cells[5, 4].Terrain, Is.EqualTo(TerrainKind.Mountain));
        Assert.That(state.Cells[6, 4].CastleOwner, Is.EqualTo(-1)); Assert.That(state.Cells[7, 4].CastleOwner, Is.EqualTo(1));
        var snapshot = Activator.CreateInstance(Script("GameSave"), controller, "MapSnapshot"); string expected = JsonUtility.ToJson(Get(snapshot, "controller"));
        object[] load = { snapshot, null }; Assert.That(Call(controller, "TryLoadSnapshot", load), Is.True, (string)load[1]);
        var restored = Activator.CreateInstance(Script("GameSave"), controller, "MapRestored");
        Assert.That(JsonUtility.ToJson(Get(restored, "controller")), Is.EqualTo(expected));
    }
    [Test]
    public void RepositoryUsesSeparateDirectoryAndRejectsInvalidOverwrite()
    {
        string root = Path.Combine(Path.GetTempPath(), "TacticsMapTest" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "LegacySave.json"), "untouched");
            var map = Data(); var repository = Repository(root); Call(repository, "Write", map);
            map.name = "Renamed map"; Call(repository, "Write", map);
            Assert.That(File.ReadAllText(Path.Combine(root, "LegacySave.json")), Is.EqualTo("untouched"));
            Assert.That(((CustomMapData)Call(repository, "Read", map.id)).name, Is.EqualTo("Renamed map"));
            map.cells.Clear(); Assert.Throws<TargetInvocationException>(() => Call(repository, "Write", map));
            Assert.That(((CustomMapData)Call(repository, "Read", map.id)).cells.Count, Is.EqualTo(120));
        }
        finally { Directory.Delete(root, true); }
    }
    [UnityTest]
    public IEnumerator LibraryEditsDraftAndSavingUpdatesItsOriginalId()
    {
        var data = Data(); data.starts.Clear(); Own(data); Call(Repository(), "Write", data);
        Call(menu, "PressCreateMap"); Click(GameObject.Find("Manage saved maps")); yield return null; yield return null;
        var edit = GameObject.Find("Edit map " + data.id); Assert.That(edit, Is.Not.Null);
        Assert.That(Button(edit).GetType().GetMethod("IsInteractable").Invoke(Button(edit), null), Is.True);
        Click(edit); yield return null; yield return null;
        var creator = Find("MapCreator"); Assert.That(creator, Is.Not.Null);
        Assert.That(JsonUtility.ToJson(Call(creator, "CaptureMap")), Is.EqualTo(JsonUtility.ToJson(data)));
        Call(creator, "SetMapName", "Edited draft"); object[] save = { null };
        Assert.That(Call(creator, "TrySaveMap", save), Is.True, (string)save[0]);
        Assert.That(((CustomMapData)Call(Repository(), "Read", data.id)).name, Is.EqualTo("Edited draft"));
    }
    [UnityTest]
    public IEnumerator RenamePreservesMapAndRefreshesGameSelection()
    {
        var data = Data(3); Own(data); Call(Repository(), "Write", data); Call(menu, "SelectCustomMap", data);
        Call(menu, "PressPlay"); Click(GameObject.Find("Choose map")); yield return null;
        Click(GameObject.Find("Rename map " + data.id)); yield return null;
        var input = GameObject.Find("Rename map input").GetComponent(Type.GetType("TMPro.TMP_InputField, Unity.TextMeshPro", true));
        input.GetType().GetProperty("text").SetValue(input, "  New name  "); Click(GameObject.Find("Confirm rename map")); yield return null; yield return null;
        data.name = "New name";
        Assert.That(JsonUtility.ToJson(Call(Repository(), "Read", data.id)), Is.EqualTo(JsonUtility.ToJson(data)));
        Assert.That(((CustomMapData)Script("MainMenu").GetProperty("SelectedCustomMap").GetValue(null)).name, Is.EqualTo("New name"));
        Assert.That(GameObject.Find("Map " + data.id), Is.Not.Null);
    }
    [UnityTest]
    public IEnumerator DeleteRequiresConfirmationAndRemovesOnlyChosenMapAndBackup()
    {
        var data = Data(); var other = Data(); Own(data); Own(other); var repository = Repository();
        Call(repository, "Write", data); Call(repository, "Write", data); Call(repository, "Write", other);
        Call(menu, "SelectCustomMap", data); Call(menu, "PressPlay"); Click(GameObject.Find("Choose map")); yield return null;
        Click(GameObject.Find("Delete map " + data.id)); yield return null;
        Assert.That(Call(repository, "Exists", data.id), Is.True); Click(GameObject.Find("Cancel delete map")); yield return null;
        Click(GameObject.Find("Delete map " + data.id)); yield return null;
        Assert.That((bool)Script("UiBackAction").GetMethod("TryCloseTop").Invoke(null, null), Is.True); yield return null;
        Assert.That(Call(repository, "Exists", data.id), Is.True); Assert.That(GameObject.Find("Custom maps Overlay"), Is.Not.Null);
        Click(GameObject.Find("Delete map " + data.id)); yield return null; Click(GameObject.Find("Confirm delete map")); yield return null; yield return null;
        Assert.That(Call(repository, "Exists", data.id), Is.False); Assert.That(Call(repository, "Exists", other.id), Is.True);
        Assert.That(File.Exists(Path.Combine(Application.persistentDataPath, "Maps", data.id + ".json.bak")), Is.False);
        Assert.That(Script("MainMenu").GetProperty("SelectedCustomMap").GetValue(null), Is.Null);
        Assert.That(GameObject.Find("Map " + data.id), Is.Null);
        Assert.Throws<TargetInvocationException>(() => Call(repository, "Delete", "../" + other.id));
        Assert.That(Call(repository, "Exists", other.id), Is.True);
    }
    [UnityTest]
    public IEnumerator DeletingOpenMapKeepsEditorContentAsNewUnsavedDraft()
    {
        var data = Data(); Own(data); Call(Repository(), "Write", data); yield return Editor();
        var creator = Find("MapCreator"); Call(creator, "ApplyMap", data); Click(GameObject.Find("Load map")); yield return null;
        Click(GameObject.Find("Delete map " + data.id)); yield return null; Click(GameObject.Find("Confirm delete map")); yield return null;
        var current = (CustomMapData)Call(creator, "CaptureMap"); Assert.That(current.id, Is.Not.EqualTo(data.id));
        current.id = data.id; Assert.That(JsonUtility.ToJson(current), Is.EqualTo(JsonUtility.ToJson(data)));
        Assert.That(creator.GetType().GetProperty("HasUnsavedChanges").GetValue(creator), Is.True);
        Click(GameObject.Find("Close")); yield return null;
        Assert.That(Find("MapEditorMenu").GetType().GetProperty("IsModalOpen").GetValue(Find("MapEditorMenu")), Is.False);
    }
}
