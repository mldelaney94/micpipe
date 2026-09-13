using System.Globalization;

namespace MicPipe.Import;

public static class TimeParser
{
    private static readonly string[] ClockFormats =
        [@"h\:mm\:ss", @"m\:ss", @"mm\:ss", @"hh\:mm\:ss", @"h\:mm\:ss\.FFF", @"m\:ss\.FFF"];

    /// <summary>Bare numbers are seconds ("6", "6.5"); otherwise m:ss / h:mm:ss. Null when blank or unparseable.</summary>
    public static TimeSpan? Parse(string? text)
    {
        var raw = text?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        // Checked first: TimeSpan.TryParse would read "6" as six days.
        if (!raw.Contains(':') && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        if (TimeSpan.TryParseExact(raw, ClockFormats, CultureInfo.InvariantCulture, out var ts) ||
            TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out ts))
        {
            return ts;
        }

        return null;
    }
}
