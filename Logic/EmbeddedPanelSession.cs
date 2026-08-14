namespace PackRat.Logic;

public sealed class EmbeddedPanelSession
{
    private int? _ownerId;

    public bool IsHidden { get; private set; }

    public void Open(int ownerId)
    {
        if (_ownerId == ownerId)
            return;

        _ownerId = ownerId;
        IsHidden = false;
    }

    public void Hide()
    {
        IsHidden = true;
    }

    public void Show()
    {
        IsHidden = false;
    }
}
