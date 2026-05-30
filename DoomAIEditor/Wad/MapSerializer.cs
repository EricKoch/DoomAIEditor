using System.Text;
using DoomAIEditor.Models;

namespace DoomAIEditor.Wad;

public static class MapSerializer
{
    static readonly string[] MapLumpOrder = { "THINGS", "LINEDEFS", "SIDEDEFS", "VERTEXES", "SEGS", "SSECTORS", "NODES", "SECTORS", "REJECT", "BLOCKMAP" };

    public static DoomMap? ReadFromWad(WadFile wad, string mapName)
    {
        int idx = wad.FindLumpIndex(mapName);
        if (idx < 0) return null;

        var map = new DoomMap { Name = mapName };

        WadLump? Get(string name)
        {
            for (int i = idx + 1; i < Math.Min(idx + 12, wad.Lumps.Count); i++)
            {
                if (wad.Lumps[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return wad.Lumps[i];
                if (MapLumpOrder.Contains(wad.Lumps[i].Name, StringComparer.OrdinalIgnoreCase) &&
                    !wad.Lumps[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                    i > idx + 1)
                    break;
            }
            for (int i = idx + 1; i < Math.Min(idx + 12, wad.Lumps.Count); i++)
                if (wad.Lumps[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return wad.Lumps[i];
            return null;
        }

        var verts = Get("VERTEXES");
        if (verts != null)
        {
            using var r = new BinaryReader(new MemoryStream(verts.Data));
            while (r.BaseStream.Position + 4 <= r.BaseStream.Length)
                map.Vertices.Add(new DoomVertex(r.ReadInt16(), r.ReadInt16()));
        }

        var things = Get("THINGS");
        if (things != null)
        {
            using var r = new BinaryReader(new MemoryStream(things.Data));
            while (r.BaseStream.Position + 10 <= r.BaseStream.Length)
                map.Things.Add(new DoomThing { X = r.ReadInt16(), Y = r.ReadInt16(), Angle = r.ReadInt16(), Type = r.ReadInt16(), Flags = r.ReadInt16() });
        }

        var sidedefs = Get("SIDEDEFS");
        if (sidedefs != null)
        {
            using var r = new BinaryReader(new MemoryStream(sidedefs.Data));
            while (r.BaseStream.Position + 30 <= r.BaseStream.Length)
                map.Sidedefs.Add(new DoomSidedef
                {
                    XOffset = r.ReadInt16(), YOffset = r.ReadInt16(),
                    UpperTexture = ReadName(r), LowerTexture = ReadName(r), MiddleTexture = ReadName(r),
                    Sector = r.ReadInt16()
                });
        }

        var sectors = Get("SECTORS");
        if (sectors != null)
        {
            using var r = new BinaryReader(new MemoryStream(sectors.Data));
            while (r.BaseStream.Position + 26 <= r.BaseStream.Length)
                map.Sectors.Add(new DoomSector
                {
                    FloorHeight = r.ReadInt16(), CeilingHeight = r.ReadInt16(),
                    FloorTexture = ReadName(r), CeilingTexture = ReadName(r),
                    LightLevel = r.ReadInt16(), Special = r.ReadInt16(), Tag = r.ReadInt16()
                });
        }

        var linedefs = Get("LINEDEFS");
        if (linedefs != null)
        {
            using var r = new BinaryReader(new MemoryStream(linedefs.Data));
            while (r.BaseStream.Position + 14 <= r.BaseStream.Length)
                map.Linedefs.Add(new DoomLinedef
                {
                    StartVertex = r.ReadInt16(), EndVertex = r.ReadInt16(),
                    Flags = r.ReadInt16(), Special = r.ReadInt16(), Tag = r.ReadInt16(),
                    FrontSidedef = r.ReadInt16(), BackSidedef = r.ReadInt16()
                });
        }

        return map;
    }

    public static void WriteToWad(WadFile wad, DoomMap map)
    {
        int existingIdx = wad.FindLumpIndex(map.Name);
        if (existingIdx >= 0)
        {
            int count = 1;
            while (existingIdx + count < wad.Lumps.Count && MapLumpOrder.Contains(wad.Lumps[existingIdx + count].Name, StringComparer.OrdinalIgnoreCase))
                count++;
            wad.Lumps.RemoveRange(existingIdx, count);
        }

        var newLumps = new List<WadLump>();
        newLumps.Add(new WadLump { Name = map.Name });
        newLumps.Add(BuildThings(map));
        newLumps.Add(BuildLinedefs(map));
        newLumps.Add(BuildSidedefs(map));
        newLumps.Add(BuildVertexes(map));
        newLumps.Add(new WadLump { Name = "SEGS" });
        newLumps.Add(new WadLump { Name = "SSECTORS" });
        newLumps.Add(new WadLump { Name = "NODES" });
        newLumps.Add(BuildSectors(map));
        newLumps.Add(new WadLump { Name = "REJECT" });
        newLumps.Add(new WadLump { Name = "BLOCKMAP" });

        if (existingIdx >= 0)
            wad.Lumps.InsertRange(existingIdx, newLumps);
        else
            wad.Lumps.AddRange(newLumps);
    }

    static string ReadName(BinaryReader r)
    {
        var bytes = r.ReadBytes(8);
        int len = Array.IndexOf(bytes, (byte)0);
        return Encoding.ASCII.GetString(bytes, 0, len < 0 ? 8 : len);
    }

    static byte[] WriteName(string name)
    {
        var buf = new byte[8];
        var src = Encoding.ASCII.GetBytes(name.ToUpperInvariant());
        Array.Copy(src, buf, Math.Min(src.Length, 8));
        return buf;
    }

    static WadLump BuildThings(DoomMap map)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        foreach (var t in map.Things) { w.Write(t.X); w.Write(t.Y); w.Write(t.Angle); w.Write(t.Type); w.Write(t.Flags); }
        return new WadLump { Name = "THINGS", Data = ms.ToArray() };
    }

    static WadLump BuildLinedefs(DoomMap map)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        foreach (var l in map.Linedefs)
        {
            w.Write((short)l.StartVertex); w.Write((short)l.EndVertex);
            w.Write(l.Flags); w.Write(l.Special); w.Write(l.Tag);
            w.Write((short)l.FrontSidedef); w.Write((short)l.BackSidedef);
        }
        return new WadLump { Name = "LINEDEFS", Data = ms.ToArray() };
    }

    static WadLump BuildSidedefs(DoomMap map)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        foreach (var s in map.Sidedefs)
        {
            w.Write(s.XOffset); w.Write(s.YOffset);
            w.Write(WriteName(s.UpperTexture)); w.Write(WriteName(s.LowerTexture)); w.Write(WriteName(s.MiddleTexture));
            w.Write((short)s.Sector);
        }
        return new WadLump { Name = "SIDEDEFS", Data = ms.ToArray() };
    }

    static WadLump BuildVertexes(DoomMap map)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        foreach (var v in map.Vertices) { w.Write(v.X); w.Write(v.Y); }
        return new WadLump { Name = "VERTEXES", Data = ms.ToArray() };
    }

    static WadLump BuildSectors(DoomMap map)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        foreach (var s in map.Sectors)
        {
            w.Write(s.FloorHeight); w.Write(s.CeilingHeight);
            w.Write(WriteName(s.FloorTexture)); w.Write(WriteName(s.CeilingTexture));
            w.Write(s.LightLevel); w.Write(s.Special); w.Write(s.Tag);
        }
        return new WadLump { Name = "SECTORS", Data = ms.ToArray() };
    }

    public static List<string> FindMapNames(WadFile wad)
    {
        var names = new List<string>();
        for (int i = 0; i < wad.Lumps.Count; i++)
        {
            var name = wad.Lumps[i].Name;
            if ((System.Text.RegularExpressions.Regex.IsMatch(name, @"^E\dM\d$") ||
                 System.Text.RegularExpressions.Regex.IsMatch(name, @"^MAP\d\d$")) &&
                wad.Lumps[i].Data.Length == 0)
                names.Add(name);
        }
        return names;
    }
}
