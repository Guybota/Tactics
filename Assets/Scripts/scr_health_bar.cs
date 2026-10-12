using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

public class Health_bar : MonoBehaviour
{
    public UnityEngine.UI.Image health_bar;

    public void Update_health(float health, float max_health)
    {
        health_bar.fillAmount = health / max_health;
    }
}
