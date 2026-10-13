using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Tactics.Core;

public sealed class CustomMapRowView : MonoBehaviour
{
    public Button select, edit, rename, delete;
    public TMP_Text caption;
    public void Bind(CustomMapData map, bool forPlaying, Action selected, Action edited, Action renamed, Action deleted)
    {
        bool ready = CustomMapValidator.PlayIssues(map).Count == 0;
        caption.text = map.name + "\n" + map.width + " × " + map.height + " · " + map.playerCount + " players\n" + (ready ? "Ready" : "Draft");
        select.name = "Map " + map.id; edit.name = "Edit map " + map.id;
        rename.name = "Rename map " + map.id; delete.name = "Delete map " + map.id;
        select.interactable = !forPlaying || ready;
        select.onClick.AddListener(() => selected()); edit.onClick.AddListener(() => edited());
        rename.onClick.AddListener(() => renamed()); delete.onClick.AddListener(() => deleted());
    }
}
