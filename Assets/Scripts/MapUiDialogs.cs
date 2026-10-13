using UnityEngine;

public sealed class MapUiDialogs : MonoBehaviour
{
    public CustomMapLibrary library;
    public MapDialogView error, validation, unsaved, rename, delete;
    private void Awake()
    {
        library.dialog.Hide(); error.Hide(); validation.Hide(); unsaved.Hide(); rename.Hide(); delete.Hide();
    }
}
