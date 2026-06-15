using DBEngine.Temporal;
using Xunit;

namespace DBEngine.Tests;

public class DateTimeEraTests
{
    [Fact]
    public void Constructor_RejectsInvalidYear()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DateTimeEra(0, 1, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void Constructor_RejectsInvalidMonth(int month)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DateTimeEra(2026, month, 1));
    }

    [Fact]
    public void Constructor_RejectsInvalidDayForMonth()
    {
        // February 2026 (not a leap year) has 28 days.
        Assert.Throws<ArgumentOutOfRangeException>(() => new DateTimeEra(2026, 2, 29));
    }

    [Fact]
    public void Constructor_AllowsLeapDay()
    {
        var date = new DateTimeEra(2024, 2, 29);
        Assert.Equal(29, date.Day);
    }

    [Fact]
    public void Constructor_DefaultsEraToCommonEra()
    {
        var date = new DateTimeEra(2026, 6, 14, "");
        Assert.Equal(DateTimeEra.CommonEra, date.Era);
    }

    [Fact]
    public void FromDateTime_And_ToDateTime_RoundTrip()
    {
        var dateTime = new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Utc);

        var era = DateTimeEra.FromDateTime(dateTime);
        Assert.Equal(DateTimeEra.CommonEra, era.Era);

        var roundTripped = era.ToDateTime();
        Assert.Equal(dateTime, roundTripped);
    }

    [Fact]
    public void ToDateTime_ThrowsForNonCommonEra()
    {
        var era = new DateTimeEra(100, 1, 1, "BCE");
        Assert.Throws<InvalidOperationException>(() => era.ToDateTime());
    }

    [Fact]
    public void CompareTo_OrdersByYearMonthDayWithinSameEra()
    {
        var earlier = new DateTimeEra(2026, 6, 14);
        var later = new DateTimeEra(2026, 6, 15);

        Assert.True(earlier < later);
        Assert.True(later > earlier);
        Assert.True(earlier <= earlier);
        Assert.True(earlier >= earlier);
    }

    [Fact]
    public void CompareTo_OrdersByEraFirst()
    {
        var bce = new DateTimeEra(100, 1, 1, "BCE");
        var ce = new DateTimeEra(1, 1, 1, "CE");

        // Eras are compared as opaque labels (ordinal string comparison), not chronologically.
        Assert.Equal(string.CompareOrdinal("BCE", "CE") < 0, bce < ce);
    }

    [Fact]
    public void ToString_FormatsAsIsoLikeDateWithEra()
    {
        var date = new DateTimeEra(2026, 6, 14, "CE");
        Assert.Equal("2026-06-14 CE", date.ToString());
    }
}
