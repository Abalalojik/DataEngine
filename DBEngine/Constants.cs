namespace DBEngine;

/// <summary>
/// Provides centralized read-only access to runtime and storage constants.
/// </summary>
public static class Constants
{
    #region Storage

    private const int _pageSize = 16;

    /// <summary>Size of a database page in bytes (16KB).</summary>
    public const int PageSize = _pageSize * 1024;

    /// <summary>Size of the page header in bytes.</summary>
    public const int PageHeaderSize = 32;

    /// <summary>Number of bytes available for data in a page.</summary>
    public const int PageDataSize = PageSize - PageHeaderSize;

    /// <summary>Value used to indicate the absence of a linked page.</summary>
    public const uint NoPage = 0;

    #endregion

    #region Runtime

    /// <summary>
    /// Absolute path to the currently open database file.
    /// Throws if no database is open.
    /// </summary>
    public static string DBPath => Database.Current?.FilePath
        ?? throw new InvalidOperationException(
            "No database is currently open. Call Database.Open() first.");

    #endregion

    #region Database

    /// <summary>Magic number identifying a valid DBEngine file (ASCII "DBEG").</summary>
    public static ReadOnlySpan<byte> MagicNumber => "DBEG"u8;

    /// <summary>Current version of the DBEngine file format.</summary>
    public const ushort FormatVersion = 1;

    #endregion

    #region PageState

    /// <summary>The page is free and available for allocation.</summary>
    public const byte PageStateFree     = 0x00;

    /// <summary>The page is occupied and contains data.</summary>
    public const byte PageStateOccupied = 0x01;

    /// <summary>The page is corrupted and should not be used.</summary>
    public const byte PageStateCorrupt  = 0x02;

    /// <summary>The page is reserved for internal use.</summary>
    public const byte PageStateReserved = 0x03;

    #endregion
}