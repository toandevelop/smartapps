using System.Text.Json;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public class ExcelImportTests
{
    [Theory]
    [InlineData("1.234,50", "vi-VN", "1234.50")]
    [InlineData("1,234.50", "invariant", "1234.50")]
    [InlineData("-10,5", "vi-VN", "-10.5")]
    [InlineData("0", "invariant", "0")]
    public void NumberNormalization_UsesExplicitLocale(string input, string format, string expected)
    {
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), ImportValueNormalizer.Normalize(input, "NUMBER", format, "yyyy-MM-dd"));
    }

    [Theory]
    [InlineData("1,5", "invariant")]
    [InlineData("1.23.456", "vi-VN")]
    [InlineData("one", "invariant")]
    public void AmbiguousOrMalformedNumbers_AreNotSilentlyReinterpreted(string input, string format) =>
        Assert.Throws<FormatException>(() => ImportValueNormalizer.Normalize(input, "NUMBER", format, "yyyy-MM-dd"));

    [Fact]
    public void DatesBooleansAndStructuredCells_AreValidated()
    {
        Assert.Equal("2026-11-17", ImportValueNormalizer.Normalize("17/11/2026", "DATE", "invariant", "dd/MM/yyyy"));
        Assert.Throws<FormatException>(() => ImportValueNormalizer.Normalize("31/02/2026", "DATE", "invariant", "dd/MM/yyyy"));
        Assert.Equal(true, ImportValueNormalizer.Normalize("có", "BOOLEAN", "invariant", "yyyy-MM-dd"));
        Assert.Equal(false, ImportValueNormalizer.Normalize(false, "BOOLEAN", "invariant", "yyyy-MM-dd"));
        Assert.Throws<FormatException>(() => ImportValueNormalizer.Normalize("maybe", "BOOLEAN", "invariant", "yyyy-MM-dd"));
        var child = Assert.IsType<JsonElement>(ImportValueNormalizer.Normalize("[{\"qty\":2}]", "SUB_TABLE", "invariant", "yyyy-MM-dd"));
        Assert.Equal(2, child[0].GetProperty("qty").GetInt32());
        Assert.Throws<FormatException>(() => ImportValueNormalizer.Normalize("{}", "SUB_TABLE", "invariant", "yyyy-MM-dd"));
    }
}
