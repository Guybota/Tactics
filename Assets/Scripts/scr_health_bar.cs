using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

[DefaultExecutionOrder(100)]
public class Health_bar : MonoBehaviour
{
    public UnityEngine.UI.Image health_bar;
    private Unit owner;
    private Canvas canvas;
    private int defaultOrder;
    private bool hasPresentationSorting;
    private int presentationLayer, presentationOrder;

    private void Awake()
    {
        owner = GetComponentInParent<Unit>();
        canvas = GetComponentInChildren<Canvas>(true);
        if (canvas != null) defaultOrder = canvas.sortingOrder;
        RefreshSorting();
    }
    // Run after Map's selection sorting so the heart remains above both sprite layers.
    private void LateUpdate() => RefreshSorting();

    public void SetPresentationSorting(int layer, int order)
    {
        hasPresentationSorting = true;
        presentationLayer = layer; presentationOrder = order;
        RefreshSorting();
    }
    public void ClearPresentationSorting()
    {
        hasPresentationSorting = false;
        RefreshSorting();
    }
    private void RefreshSorting()
    {
        if (canvas == null || owner == null) return;
        var body = owner.sprite;
        var color = owner.color;
        int order = Mathf.Max(body != null ? body.sortingOrder : defaultOrder - 1,
            color != null ? color.sortingOrder : defaultOrder - 1) + 1;
        canvas.sortingLayerID = hasPresentationSorting ? presentationLayer
            : body != null ? body.sortingLayerID : canvas.sortingLayerID;
        canvas.sortingOrder = Mathf.Max(defaultOrder, hasPresentationSorting ? Mathf.Max(order, presentationOrder) : order);
    }

    public void Update_health(float health, float max_health)
    {
        health_bar.fillAmount = health / max_health;
        RefreshSorting();
    }
}
