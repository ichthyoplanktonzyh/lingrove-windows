using System.Numerics;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>A frozen Foundation Date value: finite IEEE-754 seconds from 2001-01-01 UTC.
/// It retains the source Double even when no Windows tick or calendar value can represent it.</summary>
public readonly record struct SourceDate
{
    private static readonly long EpochTicks = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero).UtcTicks;
    public double ReferenceSeconds { get; }

    public SourceDate(double referenceSeconds)
    {
        if (!double.IsFinite(referenceSeconds)) throw new ArgumentOutOfRangeException(nameof(referenceSeconds));
        ReferenceSeconds = referenceSeconds;
    }

    /// <summary>Explicitly converts a Windows timestamp to the nearest Foundation Double.
    /// Several Windows ticks may share a Double at modern dates; this is not a lossless tick map.</summary>
    public static SourceDate FromWindows(DateTimeOffset timestamp) =>
        new((double)((timestamp.UtcTicks - EpochTicks) / (decimal)TimeSpan.TicksPerSecond));

    /// <summary>Converts to the nearest Windows tick when in range. Exact is false whenever the
    /// underlying Double lies between ticks. Neither a failed conversion nor rounding rewrites this value.</summary>
    public bool TryToWindows(out DateTimeOffset timestamp, out bool exact)
    {
        long bits = BitConverter.DoubleToInt64Bits(ReferenceSeconds);
        bool negative = bits < 0;
        int exponentBits = (int)((bits >> 52) & 0x7ff);
        long fraction = bits & 0x000f_ffff_ffff_ffffL;
        BigInteger numerator = exponentBits == 0 ? fraction : fraction | (1L << 52);
        numerator *= TimeSpan.TicksPerSecond;
        int exponent = exponentBits == 0 ? -1074 : exponentBits - 1023 - 52;
        exact = true;
        if (exponent >= 0) numerator <<= exponent;
        else
        {
            var denominator = BigInteger.One << -exponent;
            var rounded = BigInteger.DivRem(numerator, denominator, out var remainder);
            exact = remainder.IsZero;
            int order = (remainder * 2).CompareTo(denominator);
            if (order > 0 || order == 0 && !rounded.IsEven) rounded++;
            numerator = rounded;
        }
        if (negative) numerator = -numerator;
        var ticks = numerator + EpochTicks;
        if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
        { timestamp = default; exact = false; return false; }
        timestamp = new DateTimeOffset((long)ticks, TimeSpan.Zero);
        return true;
    }
}
