using RepoQuest.UI;
using Xunit;

public sealed class ModalMetricsTests
{
    [Theory]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    public void AllButtonRowsStayInsideThePanelWithPadding(int count, bool input)
    {
        var layout = new ModalMetrics(80, count, input);
        Assert.True(layout.Height <= ModalMetrics.MaxHeight);
        for (var i = 0; i < count; i++)
        {
            Assert.True(layout.ButtonCenterY(i) - ModalMetrics.ButtonHeight / 2 >= -layout.Height / 2 + ModalMetrics.Padding);
            Assert.True(Math.Abs(layout.ButtonCenterX(i, count)) + ModalMetrics.ButtonWidth / 2 <= ModalMetrics.Width / 2 - ModalMetrics.Padding);
        }
        var bodyBottom = layout.BodyTop - layout.BodyHeight;
        Assert.True(layout.ButtonsTop <= bodyBottom - ModalMetrics.ButtonsGap);
        if (input) Assert.True(layout.InputCenter + ModalMetrics.InputHeight / 2 < bodyBottom);
    }

    [Fact]
    public void ExtraRowsGrowThePanelAndLongTextUsesABoundedViewport()
    {
        Assert.True(new ModalMetrics(60, 4, true).Height > new ModalMetrics(60, 2, true).Height);
        var review = new ModalMetrics(2000, 3, false);
        Assert.Equal(ModalMetrics.MaxHeight, review.Height);
        Assert.True(review.BodyHeight > 100);
        Assert.True(review.BodyHeight < 2000);
    }
}
