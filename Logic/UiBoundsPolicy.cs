namespace PackRat.Logic;

public readonly struct FloatRect
{
    public FloatRect(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public float X { get; }
    public float Y { get; }
    public float Width { get; }
    public float Height { get; }
    public float Left => X;
    public float Right => X + Width;
    public float Bottom => Y;
    public float Top => Y + Height;
}

public static class UiBoundsPolicy
{
    public static FloatRect Clamp(FloatRect desired, FloatRect safeArea)
    {
        var width = Math.Min(desired.Width, safeArea.Width);
        var height = Math.Min(desired.Height, safeArea.Height);
        var x = Math.Max(safeArea.Left, Math.Min(desired.X, safeArea.Right - width));
        var y = Math.Max(safeArea.Bottom, Math.Min(desired.Y, safeArea.Top - height));
        return new FloatRect(x, y, width, height);
    }
}
