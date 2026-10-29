namespace Visionary.Presentation.Tests;

public class LaunchOptionsTests
{
    [Fact]
    public void TryParse_Empty_ReturnsDefault()
    {
        Assert.True(LaunchOptions.TryParse(Array.Empty<string>(), out var options, out var error));
        Assert.Equal(LaunchOptions.Default, options);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void TryParse_AllThree_ReadsEachValue()
    {
        Assert.True(LaunchOptions.TryParse(
            new[] { "--seed=20260904", "--start-day=3", "--smoke-ticks=720" }, out var options, out _));
        Assert.Equal(new LaunchOptions(20260904, 3, 720), options);
    }

    [Fact]
    public void TryParse_SingleValues_KeepOthersAtDefault()
    {
        Assert.True(LaunchOptions.TryParse(new[] { "--seed=-5" }, out var a, out _));
        Assert.Equal(new LaunchOptions(-5, 0, null), a);

        Assert.True(LaunchOptions.TryParse(new[] { "--start-day=0" }, out var b, out _));
        Assert.Equal(new LaunchOptions(1, 0, null), b);

        Assert.True(LaunchOptions.TryParse(new[] { "--smoke-ticks=1" }, out var c, out _));
        Assert.Equal(new LaunchOptions(1, 0, 1), c);
    }

    [Theory]
    [InlineData("--foo=1")]
    [InlineData("--seed")]
    [InlineData("--seed=abc")]
    [InlineData("--start-day=-1")]
    [InlineData("--smoke-ticks=0")]
    [InlineData("seed=1")]
    public void TryParse_SingleBadArgument_FailsWithMessage(string arg)
    {
        Assert.False(LaunchOptions.TryParse(new[] { arg }, out var options, out var error));
        Assert.Equal(LaunchOptions.Default, options);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void TryParse_DuplicateName_Fails()
    {
        Assert.False(LaunchOptions.TryParse(new[] { "--seed=1", "--seed=2" }, out var options, out var error));
        Assert.Equal(LaunchOptions.Default, options);
        Assert.NotEmpty(error);
    }
}
