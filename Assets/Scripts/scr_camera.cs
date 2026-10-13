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
    public Camera main_camera;

    public float scroll_speed = 8;
    public float camera_speed =  0.005f;
    public float min_x = 0;
    public float max_x = 576;
    public float min_y = 0;
    public float max_y = 576;
    public float maxZoom = 500;

    private bool locked;
    private float targetZoom, zoomVelocity, lastAppliedZoom;
    private Vector3 dragWorldAnchor, keyboardVelocity, keyboardSmoothing;
    private Vector2 zoomPointer;
    private bool zoomInitialized;
    public float ZoomTarget => targetZoom;
    public bool CanNavigate => !locked && !is_following && !LandscapeOrientation.InputBlocked;

    private void Awake()
    {
        if (main_camera == null) main_camera = GetComponent<Camera>();
        ResetNavigation();
        if (GetComponent<WorldTouchInput>() == null) gameObject.AddComponent<WorldTouchInput>();
    }

    // Update is called once per frame
    void Update()
    {
        if (is_following && unit != null)
        {
            transform.position = new Vector3(unit.transform.position.x, unit.transform.position.y, -10);
            return;
        }
        if (WorldTouchInput.SuppressMouse) return;
        var keys = new Vector2((Input.GetButton("Right") ? 1 : 0) - (Input.GetButton("Left") ? 1 : 0),
            (Input.GetButton("Up") ? 1 : 0) - (Input.GetButton("Down") ? 1 : 0));
        ApplyNavigation(Input.mouseScrollDelta.y, Input.mousePosition, keys, Input.GetMouseButtonDown(1),
            Input.GetMouseButton(1), Input.GetMouseButtonUp(1), WorldPointer.IsOverUI(Input.mousePosition), Time.unscaledDeltaTime);
    }
    public void ApplyTouchNavigation(Vector2 previousFocus, Vector2 focus, float zoomRatio)
    {
        if (main_camera == null || !CanNavigate) return;
        if (float.IsNaN(zoomRatio) || float.IsInfinity(zoomRatio) || zoomRatio <= 0) return;
        var anchor = main_camera.ScreenToWorldPoint(previousFocus);
        main_camera.orthographicSize = Mathf.Clamp(main_camera.orthographicSize * Mathf.Pow(zoomRatio, GamePreferences.ZoomSensitivity), 150, Mathf.Max(150, maxZoom));
        var afterZoom = main_camera.ScreenToWorldPoint(previousFocus);
        transform.position += new Vector3(anchor.x - afterZoom.x, anchor.y - afterZoom.y, 0);
        var current = main_camera.ScreenToWorldPoint(focus);
        transform.position += new Vector3(anchor.x - current.x, anchor.y - current.y, 0) * GamePreferences.PanSpeed;
        ClampPosition(); ResetNavigation();
    }

    // Input and navigation are separate so mouse, touch adapters and tests share the same motion.
    public void ApplyNavigation(float wheel, Vector2 pointer, Vector2 keys, bool beginDrag, bool dragging,
        bool endDrag, bool overUI, float deltaTime)
    {
        if (main_camera == null || locked || is_following || LandscapeOrientation.InputBlocked || deltaTime <= 0) return;
        float dt = Mathf.Min(deltaTime, .1f);
        float upper = Mathf.Max(150, maxZoom);
        if (!zoomInitialized || Mathf.Abs(main_camera.orthographicSize - lastAppliedZoom) > .01f)
        { targetZoom = main_camera.orthographicSize; lastAppliedZoom = targetZoom; zoomInitialized = true; zoomVelocity = 0; }
        if (wheel != 0 && !overUI)
        {
            targetZoom = Mathf.Clamp(targetZoom * Mathf.Exp(-wheel * Mathf.Max(0, scroll_speed) * .004f * GamePreferences.ZoomSensitivity), 150, upper);
            zoomPointer = pointer;
        }
        targetZoom = Mathf.Clamp(targetZoom, 150, upper);
        if (Mathf.Abs(main_camera.orthographicSize - targetZoom) > .01f)
        {
            var before = main_camera.ScreenToWorldPoint(zoomPointer);
            main_camera.orthographicSize = Mathf.SmoothDamp(main_camera.orthographicSize, targetZoom, ref zoomVelocity, .1f, Mathf.Infinity, dt);
            var after = main_camera.ScreenToWorldPoint(zoomPointer);
            transform.position += new Vector3(before.x - after.x, before.y - after.y, 0);
            ClampPosition();
        }
        else { main_camera.orthographicSize = targetZoom; zoomVelocity = 0; }
        lastAppliedZoom = main_camera.orthographicSize;
        if (endDrag || !dragging) is_mouse_moving = false;
        if (beginDrag && !overUI) { is_mouse_moving = true; dragWorldAnchor = main_camera.ScreenToWorldPoint(pointer); }
        if (is_mouse_moving && dragging)
        {
            var current = main_camera.ScreenToWorldPoint(pointer);
            transform.position += new Vector3(dragWorldAnchor.x - current.x, dragWorldAnchor.y - current.y, 0);
            ClampPosition(); keyboardVelocity = keyboardSmoothing = Vector3.zero;
        }
        else
        {
            float speed = camera_speed * 100000 * GamePreferences.PanSpeed * main_camera.orthographicSize / 350f;
            var desired = new Vector3(keys.x, keys.y, 0).normalized * speed;
            keyboardVelocity = Vector3.SmoothDamp(keyboardVelocity, desired, ref keyboardSmoothing, .08f, Mathf.Infinity, dt);
            transform.position += keyboardVelocity * dt;
            ClampPosition();
        }
        is_keyboard_moving = keys.sqrMagnitude > 0;
    }
    private void ClampPosition()
    {
        var p = transform.position;
        transform.position = new Vector3(Mathf.Clamp(p.x, min_x, max_x), Mathf.Clamp(p.y, min_y, max_y), p.z);
    }
    private void ResetNavigation()
    {
        is_mouse_moving = false; is_keyboard_moving = false;
        keyboardVelocity = keyboardSmoothing = Vector3.zero; zoomVelocity = 0;
        if (main_camera != null) { targetZoom = lastAppliedZoom = main_camera.orthographicSize; zoomInitialized = true; }
    }

    public void SetMax(int maxX, int maxY)
    {
        max_x = maxX;
        max_y = maxY;
    }
    
    public void Lock_camera()
    {
        ResetNavigation();
        locked = true;
    }

    public void Unlock_camera()
    {
        ResetNavigation();
        locked = false;
    }
    
    public void Follow_unit(Unit unit)
    {
        ResetNavigation();
        this.unit = unit;
        is_following = true;
    }

    public void Clear_follow()
    {
        unit = null;
        is_following = false;
        ResetNavigation();
    }
}
