namespace FuserSongManager.Core.Models;

// Numeric values match the v1 database (program_enums.py) so migrated rows keep their meaning.
// Names match the game's enum values (EGenre::Rock, EKey::Ab, EKeyMode::Minor).

public enum Genre
{
    Classical = 0,
    Country = 1,
    Rock = 2,
    LatinAndCaribbean = 3,
    Pop = 4,
    RnB = 5,
    HipHop = 6,
    Dance = 7,
    Indie = 8,
    Other = 9,
}

public enum MusicalKey
{
    C = 0,
    Db = 1,
    D = 2,
    Eb = 3,
    E = 4,
    F = 5,
    Gb = 6,
    G = 7,
    Ab = 8,
    A = 9,
    Bb = 10,
    B = 11,
}

public enum SongMode
{
    Major = 0,
    Minor = 1,
}

public enum LauncherType
{
    Steam = 0,
    Epic = 1,
    Local = 2,
}

public enum SongLocation
{
    Enabled,
    Disabled,
}

public enum ReadStatus
{
    Ok,
    Partial,
    Failed,
}

public enum MetadataSource
{
    Pak,
    PatchedDlcCatalog,
}
