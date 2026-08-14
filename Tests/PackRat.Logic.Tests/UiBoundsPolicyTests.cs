using PackRat.Logic;
using Xunit;

namespace PackRat.Logic.Tests;

public sealed class UiBoundsPolicyTests
{
    [Theory]
    [InlineData(1920, 1080, 1.0f)]
    [InlineData(2560, 1440, 0.8f)]
    [InlineData(3840, 2160, 1.5f)]
    public void ClampKeepsAllCardEdgesInsideSafeArea(float width, float height, float scale)
    {
        var safe = new FloatRect(0, 0, width, height);
        var desired = new FloatRect(-180 * scale, height - 760 * scale, 520 * scale, 720 * scale);

        var actual = UiBoundsPolicy.Clamp(desired, safe);

        Assert.True(actual.Left >= safe.Left);
        Assert.True(actual.Bottom >= safe.Bottom);
        Assert.True(actual.Right <= safe.Right);
        Assert.True(actual.Top <= safe.Top);
    }
}
