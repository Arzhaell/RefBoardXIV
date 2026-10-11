namespace RefBoardXIV.Tests;

public class ToolbarLayoutTests
{
    // 4 boutons de 30 (espace compris), bouton « … » de 30, liste des tableaux entre 80 et 180.
    private static readonly float[] Buttons = [30, 30, 30, 30];

    private static (float ComboWidth, int Shown) Fit(float available) => ToolbarLayout.Fit(available, Buttons, 30, 80, 180);

    [Fact]
    public void WideWindowShowsEverythingAtFullSize() => Assert.Equal((180f, 4), Fit(500));

    [Fact]
    public void BoardListShrinksBeforeAnyButtonIsHidden() => Assert.Equal((100f, 4), Fit(220));

    [Fact]
    public void ButtonsThatDoNotFitGoToTheMoreMenu()
    {
        // 80 (liste) + 30 (« … ») + 2 boutons = 170 ; les 15 qui restent reviennent à la liste.
        var (combo, shown) = Fit(185);

        Assert.Equal(2, shown);
        Assert.Equal(95f, combo);
        Assert.True(combo + (shown * 30) + 30 <= 185);
    }

    [Fact]
    public void TinyWindowKeepsTheListAndTheMoreButton() => Assert.Equal((80f, 0), Fit(100));

    [Theory]
    [InlineData(150)]
    [InlineData(199)]
    [InlineData(200)]
    [InlineData(260)]
    [InlineData(320)]
    public void EverythingShownAlwaysFits(float available)
    {
        var (combo, shown) = Fit(available);
        var more = shown < Buttons.Length ? 30 : 0;

        Assert.True(combo + Buttons.Take(shown).Sum() + more <= available);
    }
}
