namespace RefBoardXIV.Tests;

public class AutoHideTests
{
    [Fact]
    public void HidesWhenSituationStartsAndBringsBackWhenItEnds()
    {
        var state = new AutoHideState();
        Assert.Equal(AutoHideAction.None, state.Update(false, true));
        Assert.Equal(AutoHideAction.Hide, state.Update(true, true));
        Assert.True(state.HidByPlugin);
        Assert.Equal(AutoHideAction.Show, state.Update(false, false));
        Assert.False(state.HidByPlugin);
    }

    [Fact]
    public void ActsOnlyOnceWhileSituationLasts()
    {
        var state = new AutoHideState();
        Assert.Equal(AutoHideAction.Hide, state.Update(true, true));
        Assert.Equal(AutoHideAction.None, state.Update(true, false));
        Assert.Equal(AutoHideAction.None, state.Update(true, true));
    }

    [Fact]
    public void DoesNotBringBackWindowThatWasAlreadyHidden()
    {
        var state = new AutoHideState();
        Assert.Equal(AutoHideAction.None, state.Update(true, false));
        Assert.Equal(AutoHideAction.None, state.Update(false, false));
    }

    [Fact]
    public void ManualChangeCancelsComingBack()
    {
        var state = new AutoHideState();
        state.Update(true, true);
        state.Forget();
        Assert.Equal(AutoHideAction.None, state.Update(false, true));
    }

    [Fact]
    public void HidesAgainForNextSituation()
    {
        var state = new AutoHideState();
        state.Update(true, true);
        state.Update(false, false);
        Assert.Equal(AutoHideAction.Hide, state.Update(true, true));
    }

    [Theory]
    [InlineData(HideSituation.Cutscene, HideSituation.Cutscene, true)]
    [InlineData(HideSituation.Cutscene, HideSituation.Combat, false)]
    [InlineData(HideSituation.Cutscene | HideSituation.Combat, HideSituation.Combat | HideSituation.Loading, true)]
    [InlineData(HideSituation.None, HideSituation.GPose, false)]
    [InlineData(HideSituation.GPose, HideSituation.None, false)]
    public void HidesOnlyInCheckedSituations(HideSituation enabled, HideSituation current, bool expected)
    {
        Assert.Equal(expected, AutoHideState.ShouldHide(enabled, current));
    }
}
