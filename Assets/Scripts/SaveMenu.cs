using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;

public class SaveMenu : MonoBehaviour
{
    public TMP_InputField SaveName;
    public GameObject warningText;
    public string lastInput = "";

    private void Start()
    {
        warningText.SetActive(false);
    }
}
