using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class scr_action_menu : MonoBehaviour
{
    public GameObject wait_button;
    public GameObject attack_button;
    public GameObject cancel_button;
    public GameObject capture_button;
    public GameObject build_button;

    public void Setup(bool can_attack, bool can_capture, bool can_build)
    {
        attack_button.SetActive(can_attack);
        capture_button.SetActive(can_capture);
        build_button.SetActive(can_build);
    }
}
