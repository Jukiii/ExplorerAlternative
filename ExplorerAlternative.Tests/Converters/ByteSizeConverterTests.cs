using System.Globalization;
using ExplorerAlternative.Converters;

namespace ExplorerAlternative.Tests.Converters;

public sealed class ByteSizeConverterTests
{
    private readonly ByteSizeConverter _sut = new();

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(1024L, "1 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(1024L * 1024, "1 MB")]
    [InlineData(1024L * 1024 * 1024, "1 GB")]
    public void Convert_FormatsByteCountsReadably(long bytes, string expected)
    {
        var result = _sut.Convert(bytes, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Convert_ReturnsEmptyString_ForNonLongValue()
    {
        var result = _sut.Convert("not a number", typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() =>
            _sut.ConvertBack("1 KB", typeof(long), null, CultureInfo.InvariantCulture));
    }
}
