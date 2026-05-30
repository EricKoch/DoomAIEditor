namespace DoomAIEditor.Models;

[Flags]
public enum LinedefFlags
{
    None            = 0,
    Impassable      = 0x0001,
    BlockMonsters   = 0x0002,
    TwoSided        = 0x0004,
    UpperUnpegged   = 0x0008,
    LowerUnpegged   = 0x0010,
    Secret          = 0x0020,
    BlockSound      = 0x0040,
    NeverOnAutomap  = 0x0080,
    AlwaysOnAutomap = 0x0100,
}

public class DoomVertex
{
    public short X { get; set; }
    public short Y { get; set; }
    public DoomVertex() { }
    public DoomVertex(short x, short y) { X = x; Y = y; }
}

public class DoomLinedef
{
    public int StartVertex { get; set; }
    public int EndVertex { get; set; }
    public short Flags { get; set; }
    public short Special { get; set; }
    public short Tag { get; set; }
    public int FrontSidedef { get; set; } = -1;
    public int BackSidedef { get; set; } = -1;

    public bool IsImpassable => (Flags & 0x0001) != 0;
    public bool IsTwoSided => (Flags & 0x0004) != 0;
}

public class DoomSidedef
{
    public short XOffset { get; set; }
    public short YOffset { get; set; }
    public string UpperTexture { get; set; } = "-";
    public string LowerTexture { get; set; } = "-";
    public string MiddleTexture { get; set; } = "-";
    public int Sector { get; set; }
}

public class DoomSector
{
    public short FloorHeight { get; set; }
    public short CeilingHeight { get; set; } = 128;
    public string FloorTexture { get; set; } = "FLOOR4_8";
    public string CeilingTexture { get; set; } = "CEIL3_5";
    public short LightLevel { get; set; } = 160;
    public short Special { get; set; }
    public short Tag { get; set; }
}

public class DoomThing
{
    public short X { get; set; }
    public short Y { get; set; }
    public short Angle { get; set; }
    public short Type { get; set; }
    public short Flags { get; set; } = 7;

    public static readonly Dictionary<int, string> TypeNames = new()
    {
        { 1, "Player 1 Start" }, { 2, "Player 2 Start" }, { 3, "Player 3 Start" }, { 4, "Player 4 Start" },
        { 11, "Deathmatch Start" }, { 14, "Teleport Dest" },
        { 3004, "Pistol Zombie" }, { 9, "Shotgun Guy" }, { 65, "Heavy Weapon Dude" },
        { 3001, "Imp" }, { 3002, "Demon" }, { 58, "Spectre" },
        { 3003, "Baron of Hell" }, { 69, "Hell Knight" }, { 3005, "Cacodemon" },
        { 71, "Pain Elemental" }, { 68, "Arachnotron" }, { 66, "Revenant" },
        { 67, "Mancubus" }, { 64, "Arch-Vile" }, { 7, "Spider Mastermind" }, { 16, "Cyberdemon" },
        { 2001, "Shotgun" }, { 2002, "Chaingun" }, { 2003, "Rocket Launcher" },
        { 2004, "Plasma Rifle" }, { 2006, "BFG9000" }, { 2005, "Chainsaw" }, { 82, "Super Shotgun" },
        { 2007, "Clip" }, { 2048, "Box of Bullets" }, { 2008, "4 Shotgun Shells" },
        { 2049, "Box of Shotgun Shells" }, { 2010, "Rocket" }, { 2046, "Box of Rockets" },
        { 2047, "Cell" }, { 17, "Cell Pack" },
        { 2011, "Stimpak" }, { 2012, "Medikit" }, { 2014, "Health Bonus" }, { 2015, "Armor Bonus" },
        { 2018, "Green Armor" }, { 2019, "Blue Armor" },
        { 2013, "Supercharge" }, { 2022, "Invulnerability" }, { 2023, "Berserk" },
        { 2024, "Invisibility" }, { 2025, "Radiation Suit" }, { 2026, "Computer Map" },
        { 2045, "Light Amplification Visor" },
        { 5, "Blue Keycard" }, { 40, "Blue Skull Key" }, { 13, "Red Keycard" },
        { 38, "Red Skull Key" }, { 6, "Yellow Keycard" }, { 39, "Yellow Skull Key" },
    };

    public string TypeName => TypeNames.TryGetValue(Type, out var n) ? n : $"Thing #{Type}";
}

public class DoomMap
{
    public string Name { get; set; } = "MAP01";
    public List<DoomVertex> Vertices { get; set; } = new();
    public List<DoomLinedef> Linedefs { get; set; } = new();
    public List<DoomSidedef> Sidedefs { get; set; } = new();
    public List<DoomSector> Sectors { get; set; } = new();
    public List<DoomThing> Things { get; set; } = new();

    public bool IsEmpty => Vertices.Count == 0 && Things.Count == 0;

    public RectangleF GetBounds()
    {
        if (Vertices.Count == 0) return new RectangleF(-512, -512, 1024, 1024);
        float minX = Vertices.Min(v => (float)v.X);
        float minY = Vertices.Min(v => (float)v.Y);
        float maxX = Vertices.Max(v => (float)v.X);
        float maxY = Vertices.Max(v => (float)v.Y);
        float pad = 128;
        return new RectangleF(minX - pad, minY - pad, (maxX - minX) + pad * 2, (maxY - minY) + pad * 2);
    }
}
