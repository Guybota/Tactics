using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class UiBackAction : MonoBehaviour
{
    public Action Back;
    private static readonly List<UiBackAction> stack = new List<UiBackAction>();
    private void OnEnable() => stack.Add(this);
    private void OnDisable() => stack.Remove(this);
    public static bool TryCloseTop()
    {
        if (stack.Count == 0) return false;
        stack[stack.Count - 1].Back?.Invoke(); return true;
    }
}
