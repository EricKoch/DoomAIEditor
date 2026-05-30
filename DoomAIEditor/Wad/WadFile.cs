using System.Text;

namespace DoomAIEditor.Wad;

public class WadLump
{
    public int Offset { get; set; }
    public int Size { get; set; }
    public string Name { get; set; } = "";
    public byte[] Data { get; set; } = Array.Empty<byte>();
}

public class WadFile
{
    public string Type { get; private set; } = "PWAD";
    public List<WadLump> Lumps { get; } = new();

    public static WadFile Load(string path)
    {
        var wad = new WadFile();
        using var reader = new BinaryReader(File.OpenRead(path));

        var typeBytes = reader.ReadBytes(4);
        wad.Type = Encoding.ASCII.GetString(typeBytes).TrimEnd('\0');

        int numLumps = reader.ReadInt32();
        int dirOffset = reader.ReadInt32();

        reader.BaseStream.Seek(dirOffset, SeekOrigin.Begin);
        var entries = new (int offset, int size, string name)[numLumps];
        for (int i = 0; i < numLumps; i++)
        {
            int off = reader.ReadInt32();
            int sz = reader.ReadInt32();
            string nm = Encoding.ASCII.GetString(reader.ReadBytes(8)).TrimEnd('\0');
            entries[i] = (off, sz, nm);
        }

        foreach (var (off, sz, nm) in entries)
        {
            var lump = new WadLump { Offset = off, Size = sz, Name = nm };
            if (sz > 0)
            {
                reader.BaseStream.Seek(off, SeekOrigin.Begin);
                lump.Data = reader.ReadBytes(sz);
            }
            wad.Lumps.Add(lump);
        }

        return wad;
    }

    public void Save(string path)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(Encoding.ASCII.GetBytes(Type.PadRight(4, '\0')));
        writer.Write(Lumps.Count);

        int dataStart = 12;
        int dataOffset = dataStart;
        foreach (var lump in Lumps)
        {
            lump.Offset = lump.Data.Length > 0 ? dataOffset : 0;
            dataOffset += lump.Data.Length;
        }

        writer.Write(dataOffset);

        foreach (var lump in Lumps)
            writer.Write(lump.Data);

        foreach (var lump in Lumps)
        {
            writer.Write(lump.Offset);
            writer.Write(lump.Data.Length);
            var nameBytes = new byte[8];
            var src = Encoding.ASCII.GetBytes(lump.Name);
            Array.Copy(src, nameBytes, Math.Min(src.Length, 8));
            writer.Write(nameBytes);
        }
    }

    public WadLump? FindLump(string name) =>
        Lumps.FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public int FindLumpIndex(string name) =>
        Lumps.FindIndex(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static string PadName(string name) => name.PadRight(8, '\0')[..8];
}
