using MicPipe.Import;

namespace MicPipe.Tests;

public class SectionPadTests
{
    [Fact]
    public void Pads_two_seconds_each_side_and_maps_trim_into_file()
    {
        var start = TimeSpan.FromSeconds(10);
        var end = TimeSpan.FromSeconds(20);

        var (fetchStart, fetchEnd, trimStart, trimEnd) =
            UrlAudioFetcher.ApplySectionPad(start, end);

        Assert.Equal(TimeSpan.FromSeconds(8), fetchStart);
        Assert.Equal(TimeSpan.FromSeconds(22), fetchEnd);
        Assert.Equal(TimeSpan.FromSeconds(2), trimStart);
        Assert.Equal(TimeSpan.FromSeconds(12), trimEnd);
    }

    [Fact]
    public void Start_pad_floors_at_zero()
    {
        var (fetchStart, fetchEnd, trimStart, trimEnd) =
            UrlAudioFetcher.ApplySectionPad(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.Zero, fetchStart);
        Assert.Equal(TimeSpan.FromSeconds(7), fetchEnd);
        Assert.Equal(TimeSpan.FromSeconds(1), trimStart);
        Assert.Equal(TimeSpan.FromSeconds(5), trimEnd);
    }

    [Fact]
    public void Open_ended_section_pads_start_only()
    {
        var (fetchStart, fetchEnd, trimStart, trimEnd) =
            UrlAudioFetcher.ApplySectionPad(TimeSpan.FromSeconds(10), end: null);

        Assert.Equal(TimeSpan.FromSeconds(8), fetchStart);
        Assert.Null(fetchEnd);
        Assert.Equal(TimeSpan.FromSeconds(2), trimStart);
        Assert.Null(trimEnd);
    }
}

public class TimeParserTests
{
    [Theory]
    [InlineData("6", 6)]
    [InlineData("0", 0)]
    [InlineData("6.5", 6.5)]
    [InlineData("1:30", 90)]
    [InlineData("01:02:03", 3723)]
    public void Parses_seconds_and_clock_forms(string text, double expectedSeconds)
    {
        var ts = TimeParser.Parse(text);
        Assert.NotNull(ts);
        Assert.Equal(expectedSeconds, ts!.Value.TotalSeconds, precision: 3);
    }

    [Fact]
    public void Empty_returns_null()
    {
        Assert.Null(TimeParser.Parse(null));
        Assert.Null(TimeParser.Parse(""));
        Assert.Null(TimeParser.Parse("   "));
    }
}
