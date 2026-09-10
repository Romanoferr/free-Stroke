using EpicPencil.Core;

namespace EpicPencil.Core.Tests;

public sealed class InputDedupTests
{
    [Fact]
    public void First_Mouse_Always_Accepted()
    {
        Assert.True(InputDedup.ShouldAcceptMouse(double.NaN, 1000));
    }

    [Fact]
    public void Promoted_Mouse_Suppressed()
    {
        Assert.False(InputDedup.ShouldAcceptMouse(1000, 1010)); // mesmo batch
        Assert.False(InputDedup.ShouldAcceptMouse(1000, 1079));
    }

    [Fact]
    public void Slow_Double_Click_Accepted()
    {
        Assert.True(InputDedup.ShouldAcceptMouse(1000, 1200));
    }
}
