using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(-150), DisallowMultipleComponent, RequireComponent(typeof(Canvas))]
public sealed class MobileUiRoot : MonoBehaviour
{
    private int controlCount = -1;
    private void Awake()
    {
        InstallTargets();
    }
    private void Update()
    { MobileUiMode.UpdateTouchGate(Input.touchCount, LandscapeOrientation.InputBlocked); }
    private void LateUpdate()
    {
        if (!MobileUiMode.Enabled) return;
        if (Selectable.allSelectableCount != controlCount) InstallTargets();
    }
    private void InstallTargets()
    {
        controlCount = Selectable.allSelectableCount;
        if (!MobileUiMode.Enabled) return;
        foreach (var control in GetComponentsInChildren<Selectable>(true))
            if (control.GetComponent<MobileTouchTarget>() == null) control.gameObject.AddComponent<MobileTouchTarget>();
        var events = UnityEngine.EventSystems.EventSystem.current;
        if (events != null) events.pixelDragThreshold = Mathf.CeilToInt(12 * MobileUiMode.Density);
    }
}
