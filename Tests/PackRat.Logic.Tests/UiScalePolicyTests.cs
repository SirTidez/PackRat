using PackRat.Logic;
using Xunit;

namespace PackRat.Logic.Tests;

public sealed class UiScalePolicyTests
{
    public static TheoryData<float, float> SupportedResolutions => new()
    {
        { 1280f, 720f },
        { 1920f, 1080f },
        { 2560f, 1440f },
        { 3840f, 2160f },
        { 1920f, 1200f },
        { 1280f, 960f },
        { 3440f, 1440f },
        { 5120f, 1440f }
    };

    [Theory]
    [MemberData(nameof(SupportedResolutions))]
    public void HeightMatchPreservesTheConstrainedLogicalAxis(float width, float height)
    {
        var logical = UiScalePolicy.GetLogicalSize(width, height, 1920f, 1080f, 1f);

        Assert.Equal(1080f, logical.Height, 3);
        Assert.True(logical.Width >= 1440f);
    }

    [Theory]
    [MemberData(nameof(SupportedResolutions))]
    public void EveryEditorAuthoredPaneFitsAtMaximumSupportedZoom(float width, float height)
    {
        var logical = UiScalePolicy.GetLogicalSize(width, height, 1920f, 1080f, 1f);
        var zoomedCards = new[]
        {
            (Width: 448f, Height: 604f, Zoom: 1.5f),
            (Width: 420f, Height: 606f, Zoom: 1.5f),
            (Width: 420f, Height: 660f, Zoom: 1.5f),
            (Width: 620f, Height: 480f, Zoom: 1f)
        };

        foreach (var card in zoomedCards)
            Assert.True(UiScalePolicy.Fits(card.Width, card.Height, card.Zoom, logical.Width, logical.Height, 24f),
                $"{card.Width}x{card.Height} at {card.Zoom:0.00} did not fit {width}x{height}.");
    }

    [Fact]
    public void InvalidDimensionsFallBackToNeutralScale()
    {
        Assert.Equal(1f, UiScalePolicy.GetScaleFactor(0f, 1080f, 1920f, 1080f, 1f));
        Assert.Equal(1f, UiScalePolicy.GetScaleFactor(1920f, 1080f, 0f, 1080f, 1f));
    }

    [Fact]
    public void FrameworkExpandsByTheNativeGridOverflowOnEachAxis()
    {
        var expanded = UiScalePolicy.ExpandFrameworkToContent(
            frameworkWidth: 390f,
            frameworkHeight: 520f,
            authoredContentWidth: 354f,
            authoredContentHeight: 220f,
            requiredContentWidth: 384f,
            requiredContentHeight: 306f);

        Assert.Equal(420f, expanded.Width);
        Assert.Equal(606f, expanded.Height);
    }

    [Fact]
    public void FrameworkNeverShrinksBelowItsAuthoredSize()
    {
        var expanded = UiScalePolicy.ExpandFrameworkToContent(390f, 520f, 354f, 220f, 300f, 180f);

        Assert.Equal(390f, expanded.Width);
        Assert.Equal(520f, expanded.Height);
    }

    [Fact]
    public void UniformFitCapsZoomWithoutDistortingTheSurface()
    {
        var fitted = UiScalePolicy.FitUniformScale(540f, 702f, 1.5f, 1872f, 1032f);

        Assert.Equal(1032f / 702f, fitted, 4);
        Assert.True(fitted < 1.5f);
    }

    [Fact]
    public void UniformFitRetainsRequestedScaleWhenThereIsRoom()
    {
        Assert.Equal(0.85f, UiScalePolicy.FitUniformScale(420f, 606f, 0.85f, 1872f, 1032f), 4);
    }
}
