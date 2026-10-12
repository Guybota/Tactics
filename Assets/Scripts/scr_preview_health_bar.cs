
using System.Collections;
using UnityEngine;

public class Preview_health_bar : Health_bar
{
    public UnityEngine.UI.Image predicted_health_bar;
    public float transparency;
    public float changeSpeed = 5;
    private bool fadeaway = true;
    private float i = 0;

    private void Start()
    {

    }
    // Update is called once per frame
    void Update()
    {
        /**
        health_bar.color = new Color(0.05f, 1, 0, transparency);
        float pingpong = Mathf.PingPong((Time.deltaTime) * changeSpeed, 1);
        Debug.Log(pingpong);
        transparency = Mathf.Lerp(.3f, 1f, pingpong);
        **/

        if (fadeaway)
        {
            // set color with i as alpha
            health_bar.color = new Color(1, 1, 1, i);
            if (i <= 0)
                fadeaway = false;

            i -= Time.deltaTime;
        }
        else
        {
            // set color with i as alpha
            health_bar.color = new Color(1, 1, 1, i);
            if (i >= 1)
                fadeaway = true;

            i += Time.deltaTime;
        }
    }
    public void Update_predicted_health(float health, float predicted_health, float max_health)
    {
        health_bar.fillAmount = health / max_health;
        predicted_health_bar.fillAmount = max_health > 0 ? Mathf.Clamp01(predicted_health / max_health) : 0;
    }
}
