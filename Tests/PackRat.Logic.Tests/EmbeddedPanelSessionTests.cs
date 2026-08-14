using PackRat.Logic;
using Xunit;

namespace PackRat.Logic.Tests;

public sealed class EmbeddedPanelSessionTests
{
    [Fact]
    public void HiddenStatePersistsForSameOwnerAndResetsForDifferentOwner()
    {
        var session = new EmbeddedPanelSession();
        session.Open(10);
        session.Hide();

        session.Open(10);
        Assert.True(session.IsHidden);

        session.Open(11);
        Assert.False(session.IsHidden);
    }

    [Fact]
    public void ShowRestoresSameOwnerSession()
    {
        var session = new EmbeddedPanelSession();
        session.Open(10);
        session.Hide();
        session.Show();

        Assert.False(session.IsHidden);
    }
}
