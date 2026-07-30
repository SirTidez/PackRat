using UnityEngine;

namespace PackRat.Config;

/// <summary>
/// The visual preset used by PackRat-owned backpack UI. These values deliberately leave game
/// inventory slots, item quality markers, and vanilla menus under Schedule I's control.
/// </summary>
public enum BackpackUiTheme
{
    S1Blue,
    Midnight,
    Forest,
    Amber,
    Custom
}

/// <summary>
/// Semantic colour roles shared by the standalone backpack browser and its handover projection.
/// </summary>
public readonly struct BackpackUiThemePalette
{
    public readonly Color Card;
    public readonly Color Header;
    public readonly Color Accent;
    public readonly Color Control;
    public readonly Color ControlAlt;
    public readonly Color SelectedControl;
    public readonly Color Search;
    public readonly Color SearchFocused;
    public readonly Color ModalCard;
    public readonly Color ModalContent;
    public readonly Color Drawer;
    public readonly Color DrawerRow;
    public readonly Color PrimaryText;
    public readonly Color SecondaryText;

    public BackpackUiThemePalette(Color card, Color header, Color accent, Color control, Color controlAlt,
        Color selectedControl, Color search, Color searchFocused, Color modalCard, Color modalContent,
        Color drawer, Color drawerRow, Color primaryText, Color secondaryText)
    {
        Card = card;
        Header = header;
        Accent = accent;
        Control = control;
        ControlAlt = controlAlt;
        SelectedControl = selectedControl;
        Search = search;
        SearchFocused = searchFocused;
        ModalCard = modalCard;
        ModalContent = modalContent;
        Drawer = drawer;
        DrawerRow = drawerRow;
        PrimaryText = primaryText;
        SecondaryText = secondaryText;
    }
}

/// <summary>
/// Central palette registry for PackRat's S1-compatible UI presets.
/// </summary>
public static class BackpackUiThemes
{
    public static BackpackUiTheme Clamp(int value)
    {
        return (BackpackUiTheme)Mathf.Clamp(value, (int)BackpackUiTheme.S1Blue, (int)BackpackUiTheme.Custom);
    }

    public static string GetLabel(BackpackUiTheme theme)
    {
        return theme switch
        {
            BackpackUiTheme.Midnight => "MIDNIGHT",
            BackpackUiTheme.Forest => "FOREST",
            BackpackUiTheme.Amber => "AMBER",
            BackpackUiTheme.Custom => "CUSTOM",
            _ => "S1 BLUE"
        };
    }

    public static BackpackUiTheme Offset(BackpackUiTheme theme, int offset)
    {
        const int count = 5;
        var value = ((int)theme + offset) % count;
        return (BackpackUiTheme)(value < 0 ? value + count : value);
    }

    public static BackpackUiThemePalette Get(BackpackUiTheme theme)
    {
        return Get(theme, new Color32(35, 61, 86, 255));
    }

    public static BackpackUiThemePalette Get(BackpackUiTheme theme, Color customPrimary)
    {
        return theme switch
        {
            BackpackUiTheme.Midnight => new BackpackUiThemePalette(
                new Color32(15, 18, 32, 238), new Color32(41, 45, 82, 248), new Color32(145, 132, 246, 255),
                new Color32(24, 26, 47, 245), new Color32(29, 33, 60, 255), new Color32(76, 69, 151, 255),
                new Color32(11, 12, 24, 245), new Color32(53, 49, 103, 250), new Color32(15, 17, 34, 252),
                new Color32(24, 27, 49, 238), new Color32(12, 15, 28, 252), new Color32(31, 35, 62, 238),
                new Color32(244, 244, 255, 255), new Color32(194, 188, 239, 255)),
            BackpackUiTheme.Forest => new BackpackUiThemePalette(
                new Color32(13, 27, 24, 238), new Color32(28, 72, 61, 248), new Color32(85, 196, 157, 255),
                new Color32(16, 42, 36, 245), new Color32(21, 54, 46, 255), new Color32(40, 126, 101, 255),
                new Color32(8, 22, 19, 245), new Color32(26, 90, 72, 250), new Color32(10, 30, 26, 252),
                new Color32(17, 50, 43, 238), new Color32(8, 24, 21, 252), new Color32(24, 63, 53, 238),
                new Color32(239, 250, 246, 255), new Color32(166, 221, 203, 255)),
            BackpackUiTheme.Amber => new BackpackUiThemePalette(
                new Color32(31, 24, 17, 238), new Color32(87, 59, 35, 248), new Color32(244, 178, 74, 255),
                new Color32(48, 35, 22, 245), new Color32(61, 45, 28, 255), new Color32(159, 103, 39, 255),
                new Color32(24, 17, 11, 245), new Color32(108, 71, 31, 250), new Color32(36, 26, 16, 252),
                new Color32(56, 41, 26, 238), new Color32(29, 21, 13, 252), new Color32(75, 55, 34, 238),
                new Color32(255, 247, 234, 255), new Color32(238, 207, 161, 255)),
            BackpackUiTheme.Custom => CreateCustomPalette(customPrimary),
            _ => new BackpackUiThemePalette(
                new Color32(15, 21, 28, 238), new Color32(35, 61, 86, 248), new Color32(76, 173, 229, 255),
                new Color32(18, 30, 40, 245), new Color32(20, 35, 47, 255), new Color32(48, 128, 170, 255),
                new Color32(10, 15, 20, 245), new Color32(24, 74, 102, 250), new Color32(10, 23, 31, 252),
                new Color32(16, 32, 43, 238), new Color32(9, 19, 27, 252), new Color32(23, 42, 56, 238),
                new Color32(244, 247, 250, 255), new Color32(166, 205, 229, 255))
        };
    }

    private static BackpackUiThemePalette CreateCustomPalette(Color primary)
    {
        Color.RGBToHSV(primary, out var hue, out var saturation, out var value);
        saturation = Mathf.Clamp01(saturation);
        value = Mathf.Clamp(value, 0.2f, 0.95f);
        var header = Color.HSVToRGB(hue, saturation, value);
        var accent = Color.HSVToRGB(hue, Mathf.Clamp01(saturation * 0.82f + 0.12f),
            Mathf.Clamp01(value + 0.26f));
        var card = Color.HSVToRGB(hue, saturation * 0.45f, Mathf.Max(0.07f, value * 0.22f));
        var control = Color.HSVToRGB(hue, saturation * 0.60f, Mathf.Max(0.09f, value * 0.34f));
        var controlAlt = Color.HSVToRGB(hue, saturation * 0.56f, Mathf.Max(0.11f, value * 0.43f));
        var selected = Color.HSVToRGB(hue, Mathf.Clamp01(saturation * 0.86f + 0.08f),
            Mathf.Clamp01(value * 0.75f + 0.10f));
        var search = Color.HSVToRGB(hue, saturation * 0.28f, Mathf.Max(0.04f, value * 0.12f));
        var focusedSearch = Color.HSVToRGB(hue, saturation * 0.62f, Mathf.Max(0.14f, value * 0.55f));
        var modalCard = Color.HSVToRGB(hue, saturation * 0.48f, Mathf.Max(0.06f, value * 0.18f));
        var modalContent = Color.HSVToRGB(hue, saturation * 0.50f, Mathf.Max(0.10f, value * 0.39f));
        var drawer = Color.HSVToRGB(hue, saturation * 0.42f, Mathf.Max(0.05f, value * 0.16f));
        var drawerRow = Color.HSVToRGB(hue, saturation * 0.53f, Mathf.Max(0.12f, value * 0.48f));
        var primaryText = Color.HSVToRGB(hue, saturation * 0.10f, 0.98f);
        var secondaryText = Color.HSVToRGB(hue, saturation * 0.48f, 0.88f);
        return new BackpackUiThemePalette(card, header, accent, control, controlAlt, selected, search,
            focusedSearch, modalCard, modalContent, drawer, drawerRow, primaryText, secondaryText);
    }
}
