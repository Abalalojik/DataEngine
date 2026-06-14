namespace DBEngine.Temporal;

/// <summary>
/// A calendar date expressed against a named era (e.g. "CE", "BCE", or any other custom
/// calendar system identifier), independent of the Gregorian-only <see cref="DateTime"/> type.
///
/// This is a basic, allocation-free representation intended for storage and comparison.
/// It performs lightweight validation of <see cref="Year"/>, <see cref="Month"/> and <see cref="Day"/>
/// against the proleptic Gregorian calendar, but does not itself implement era-specific calendar
/// arithmetic (e.g. leap-year rules of non-Gregorian calendars) — <see cref="Era"/> is treated as an
/// opaque label for sorting and storage purposes.
/// </summary>
public readonly record struct DateTimeEra : IComparable<DateTimeEra>
{
    /// <summary>The well-known era label for the Common Era / Anno Domini.</summary>
    public const string CommonEra = "CE";

    /// <summary>The well-known era label for Before Common Era / Before Christ.</summary>
    public const string BeforeCommonEra = "BCE";

    /// <summary>The year in the given calendar system. Must be 1 or greater.</summary>
    public int Year { get; init; }

    /// <summary>The month in the given calendar system (1-12).</summary>
    public int Month { get; init; }

    /// <summary>The day in the given calendar system (1-31, depending on month/year).</summary>
    public int Day { get; init; }

    /// <summary>The name of the era or calendar system (e.g. "CE", "BCE", "Islamic").</summary>
    public string Era { get; init; }

    /// <summary>
    /// Creates and validates a new <see cref="DateTimeEra"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="year"/> is less than 1, <paramref name="month"/> is not in [1,12],
    /// or <paramref name="day"/> is not valid for the given month/year.
    /// </exception>
    public DateTimeEra(int year, int month, int day, string era = CommonEra)
    {
        if (year < 1)
            throw new ArgumentOutOfRangeException(nameof(year), year, "Year must be 1 or greater.");
        if (month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month), month, "Month must be between 1 and 12.");

        int daysInMonth = DateTime.DaysInMonth(Math.Clamp(year, 1, 9999), month);
        if (day < 1 || day > daysInMonth)
            throw new ArgumentOutOfRangeException(nameof(day), day, $"Day must be between 1 and {daysInMonth} for {year}-{month:D2}.");

        Year = year;
        Month = month;
        Day = day;
        Era = string.IsNullOrWhiteSpace(era) ? CommonEra : era;
    }

    /// <summary>
    /// Converts a standard <see cref="DateTime"/> (proleptic Gregorian, year &gt;= 1) into a
    /// <see cref="DateTimeEra"/> using the <see cref="CommonEra"/> era.
    /// </summary>
    public static DateTimeEra FromDateTime(DateTime dateTime, string era = CommonEra) =>
        new(dateTime.Year, dateTime.Month, dateTime.Day, era);

    /// <summary>
    /// Converts this value to a <see cref="DateTime"/> (at midnight, UTC), provided <see cref="Era"/>
    /// is <see cref="CommonEra"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="Era"/> is not <see cref="CommonEra"/>.</exception>
    public DateTime ToDateTime()
    {
        if (Era != CommonEra)
            throw new InvalidOperationException(
                $"Cannot convert a date in era '{Era}' to {nameof(DateTime)}; only '{CommonEra}' dates can be converted directly.");

        return new DateTime(Year, Month, Day, 0, 0, 0, DateTimeKind.Utc);
    }

    /// <summary>
    /// Compares two dates. Dates are ordered first by <see cref="Era"/> (ordinal string comparison),
    /// then by <see cref="Year"/>, <see cref="Month"/> and <see cref="Day"/>.
    /// Note that this does not account for the chronological relationship between different eras
    /// (e.g. "BCE" years do not sort as "earlier than CE"); <see cref="Era"/> is treated as an
    /// independent, opaque grouping key.
    /// </summary>
    public int CompareTo(DateTimeEra other)
    {
        int cmp = string.CompareOrdinal(Era, other.Era);
        if (cmp != 0) return cmp;

        cmp = Year.CompareTo(other.Year);
        if (cmp != 0) return cmp;

        cmp = Month.CompareTo(other.Month);
        return cmp != 0 ? cmp : Day.CompareTo(other.Day);
    }

    public static bool operator <(DateTimeEra left, DateTimeEra right) => left.CompareTo(right) < 0;
    public static bool operator <=(DateTimeEra left, DateTimeEra right) => left.CompareTo(right) <= 0;
    public static bool operator >(DateTimeEra left, DateTimeEra right) => left.CompareTo(right) > 0;
    public static bool operator >=(DateTimeEra left, DateTimeEra right) => left.CompareTo(right) >= 0;

    /// <inheritdoc/>
    public override string ToString() => $"{Year:D4}-{Month:D2}-{Day:D2} {Era}";
}
