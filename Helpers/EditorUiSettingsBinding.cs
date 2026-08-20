using UnityEngine;
using UnityEngine.UI;

namespace PackRat.Helpers;

/// <summary>
/// Holds the validated controls from the editor-authored backpack settings modal. The prefab owns
/// presentation and layout while <c>StorageMenuPatch</c> continues to own settings state and rows.
/// </summary>
internal sealed class EditorUiSettingsBinding
{
    public RectTransform Root { get; set; }
    public CanvasGroup RootCanvasGroup { get; set; }
    public Button BlockerButton { get; set; }
    public RectTransform Card { get; set; }
    public CanvasGroup CardCanvasGroup { get; set; }
    public Button CloseButton { get; set; }
    public Text SessionStatusValue { get; set; }
    public RectTransform Tabs { get; set; }
    public Button GeneralButton { get; set; }
    public Button ThemeButton { get; set; }
    public Button TiersButton { get; set; }
    public Button LayoutButton { get; set; }
    public Button RoutingButton { get; set; }
    public Button MetricsButton { get; set; }
    public RectTransform Content { get; set; }
    public ScrollRect ScrollRect { get; set; }
    public RectTransform GeneralPage { get; set; }
    public RectTransform ThemePage { get; set; }
    public RectTransform TiersPage { get; set; }
    public RectTransform LayoutPage { get; set; }
    public RectTransform RoutingPage { get; set; }
    public RectTransform MetricsPage { get; set; }
}
