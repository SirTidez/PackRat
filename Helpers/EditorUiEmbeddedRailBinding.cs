using UnityEngine;
using UnityEngine.UI;

namespace PackRat.Helpers;

/// <summary>
/// Owns the validated, editor-authored collapse and restore rails extracted from the embedded
/// backpack prefab. The rest of the prefab remains presentation-only until its controls are bound.
/// </summary>
internal sealed class EditorUiEmbeddedRailBinding
{
    public EditorUiEmbeddedRailBinding(EditorUiPane sourcePane, RectTransform host, RectTransform collapseRail,
        Button hideButton, RectTransform collapsedHandle, Button showButton)
    {
        SourcePane = sourcePane;
        Host = host;
        CollapseRail = collapseRail;
        HideButton = hideButton;
        CollapsedHandle = collapsedHandle;
        ShowButton = showButton;
    }

    public EditorUiPane SourcePane { get; }
    public RectTransform Host { get; }
    public RectTransform CollapseRail { get; }
    public Button HideButton { get; }
    public RectTransform CollapsedHandle { get; }
    public Button ShowButton { get; }

    /// <summary>
    /// Keeps exactly one rail visible while leaving the surrounding storage session untouched.
    /// </summary>
    public void ApplyHiddenState(bool hidden)
    {
        if (Host != null)
            Host.gameObject.SetActive(true);
        if (CollapseRail != null)
            CollapseRail.gameObject.SetActive(!hidden);
        if (CollapsedHandle != null)
            CollapsedHandle.gameObject.SetActive(hidden);
    }
}
