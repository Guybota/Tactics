using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class scr_camera : MonoBehaviour
{
    public bool is_mouse_moving = false;
    public bool is_keyboard_moving = false;
    public bool is_following = false;
    public Unit unit;
    private Vector3 og_moouse_position;
    private Vector3 move_vector;
    public Camera main_camera;

    public float scroll_speed = 8;
    public float camera_speed =  0.005f;
    public float min_x = 0;
    public float max_x = 576;
    public float min_y = 0;
    public float max_y = 576;
    public float maxZoom = 500;

    private bool locked;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (!is_following && !locked)
        {
            if ((main_camera.orthographicSize <= maxZoom || Input.mouseScrollDelta.y > 0)
                && (main_camera.orthographicSize >= 150 || Input.mouseScrollDelta.y < 0))
            {
                main_camera.orthographicSize = Mathf.Clamp(main_camera.orthographicSize - Input.mouseScrollDelta.y * scroll_speed, 150, maxZoom);
            }

            if (Input.GetMouseButtonDown(1) && is_mouse_moving == false)
            {
                is_mouse_moving = true;
                og_moouse_position = Input.mousePosition;
            }

            if (Input.GetMouseButtonUp(1) && is_mouse_moving == true)
            {
                is_mouse_moving = false;
            }

            move_vector = new Vector3(
                (Input.GetButton("Right") ? 1 : 0) - (Input.GetButton("Left") ? 1 : 0),
                (Input.GetButton("Up") ? 1 : 0) - (Input.GetButton("Down") ? 1 : 0), 0);

            if (is_mouse_moving == true)
            {
                Vector3 mouseVector = (Input.mousePosition - og_moouse_position) * camera_speed;
                Vector3 moveVector = new Vector3(Math.Clamp(mouseVector.x, min_x - transform.position.x, max_x - transform.position.x), Math.Clamp(mouseVector.y, min_y - transform.position.y, max_y - transform.position.y));
                transform.position += moveVector;
            }
            else
            {
                Vector3 keyVector = Vector3.Normalize(move_vector) * camera_speed * 275 * 60 * Time.deltaTime;
                Vector3 moveVector = new Vector3(Math.Clamp(keyVector.x, min_x - transform.position.x, max_x - transform.position.x), Math.Clamp(keyVector.y, min_y - transform.position.y, max_y - transform.position.y));
                transform.position += moveVector;
            }
        }
        else if (unit != null)
        {
            Vector3 vector = new Vector3(unit.transform.position.x, unit.transform.position.y, -10);
            transform.position = vector;
        }
    }

    public void SetMax(int maxX, int maxY)
    {
        max_x = maxX;
        max_y = maxY;
    }
    
    public void Lock_camera()
    {
        is_mouse_moving = false;
        is_keyboard_moving = false;
        move_vector = Vector3.zero;

        locked = true;
    }

    public void Unlock_camera()
    {
        locked = false;
    }
    
    public void Follow_unit(Unit unit)
    {
        this.unit = unit;
        is_following = true;
    }

    public void Clear_follow()
    {
        unit = null;
        is_following = false;
    }
}
