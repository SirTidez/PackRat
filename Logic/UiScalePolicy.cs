namespace PackRat.Logic;

/// <summary>
/// Pure policy helpers matching Unity CanvasScaler's Scale With Screen Size behavior. Keeping the
/// calculation independent from Unity makes the editor layout contract testable in both runtimes.
/// </summary>
public static class UiScalePolicy
{
    public static float GetScaleFactor(float screenWidth, float screenHeight, float referenceWidth,
        float referenceHeight, float matchWidthOrHeight)
    {
        if (screenWidth <= 0f || screenHeight <= 0f || referenceWidth <= 0f || referenceHeight <= 0f)
            return 1f;

        var match = Math.Clamp(matchWidthOrHeight, 0f, 1f);
        var widthScale = screenWidth / referenceWidth;
        var heightScale = screenHeight / referenceHeight;
        return (float)Math.Pow(widthScale, 1f - match) * (float)Math.Pow(heightScale, match);
    }

    public static (float Width, float Height) GetLogicalSize(float screenWidth, float screenHeight,
        float referenceWidth, float referenceHeight, float matchWidthOrHeight)
    {
        var scale = GetScaleFactor(screenWidth, screenHeight, referenceWidth, referenceHeight,
            matchWidthOrHeight);
        return (screenWidth / scale, screenHeight / scale);
    }

    public static bool Fits(float cardWidth, float cardHeight, float zoom, float logicalWidth,
        float logicalHeight, float edgeInset)
    {
        var scale = Math.Max(0f, zoom);
        var usableWidth = Math.Max(0f, logicalWidth - Math.Max(0f, edgeInset) * 2f);
        var usableHeight = Math.Max(0f, logicalHeight - Math.Max(0f, edgeInset) * 2f);
        return cardWidth * scale <= usableWidth && cardHeight * scale <= usableHeight;
    }

    /// <summary>
    /// Expands an authored framework by exactly the amount its runtime-owned content exceeds the
    /// authored content well. Neither axis is allowed to shrink: editor-authored spacing and
    /// controls remain stable when the supplied content is smaller than the design placeholder.
    /// </summary>
    public static (float Width, float Height) ExpandFrameworkToContent(float frameworkWidth,
        float frameworkHeight, float authoredContentWidth, float authoredContentHeight,
        float requiredContentWidth, float requiredContentHeight)
    {
        var width = Math.Max(0f, frameworkWidth) +
            Math.Max(0f, Math.Max(0f, requiredContentWidth) - Math.Max(0f, authoredContentWidth));
        var height = Math.Max(0f, frameworkHeight) +
            Math.Max(0f, Math.Max(0f, requiredContentHeight) - Math.Max(0f, authoredContentHeight));
        return (width, height);
    }

    /// <summary>
    /// Caps a requested uniform scale so a complete surface remains inside its available logical
    /// area. The result never enlarges the requested scale and does not introduce axis distortion.
    /// </summary>
    public static float FitUniformScale(float contentWidth, float contentHeight, float requestedScale,
        float availableWidth, float availableHeight)
    {
        var requested = Math.Max(0f, requestedScale);
        if (contentWidth <= 0f || contentHeight <= 0f || availableWidth <= 0f || availableHeight <= 0f)
            return requested;

        return Math.Min(requested, Math.Min(availableWidth / contentWidth, availableHeight / contentHeight));
    }
}
