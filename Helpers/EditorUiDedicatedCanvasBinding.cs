using UnityEngine;
using UnityEngine.UI;

namespace PackRat.Helpers;

/// <summary>
/// Holds the validated PackRat-owned overlay canvas and its safe-area presentation host.
/// </summary>
internal sealed class EditorUiDedicatedCanvasBinding
{
    public RectTransform Root { get; set; }
    public Canvas Canvas { get; set; }
    public CanvasScaler Scaler { get; set; }
    public GraphicRaycaster Raycaster { get; set; }
    public RectTransform SafeAreaRoot { get; set; }
    public RectTransform PaneHost { get; set; }
    public CanvasGroup PaneHostCanvasGroup { get; set; }
}
