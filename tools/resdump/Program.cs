
using System.Resources;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

class RcData
{
    public int Rva; public int Size;
}

class Program
{
    static PEReader pe = null!;
    static int rsrcBase;
    static Dictionary<string, RcData> rcdata = new(StringComparer.OrdinalIgnoreCase);

    static void Main(string[] args)
    {
        string asmPath = args[0], outRoot = args[1];
        Directory.CreateDirectory(outRoot);
        using var fs = File.OpenRead(asmPath);
        using (pe = new PEReader(fs))
        {
            rsrcBase = pe.PEHeaders.PEHeader!.ResourceTableDirectory.RelativeVirtualAddress;
            ParseDir((int)rsrcBase, 0, new List<string>());
            Console.WriteLine($"RCDATA entries: {rcdata.Count}");

            var md = pe.GetMetadataReader();
            foreach (var h in md.ManifestResources)
            {
                var r = md.GetManifestResource(h);
                string name = md.GetString(r.Name);
                if (!r.Implementation.IsNil) { Console.WriteLine("LINKED " + name); continue; }

                byte[] data;
                if (r.Offset == 0)
                {
                    if (!rcdata.TryGetValue(name, out var rc)) { Console.WriteLine("MISSING RCDATA " + name); continue; }
                    data = ReadRva(rc.Rva, rc.Size);
                }
                else
                {
                    var reader = pe.GetSectionData((int)r.Offset).GetReader();
                    int size = reader.ReadInt32();
                    data = reader.ReadBytes(size);
                }
                string safe = Safe(name);
                string rawPath = Path.Combine(outRoot, "manifest", safe);
                Directory.CreateDirectory(Path.GetDirectoryName(rawPath)!);
                File.WriteAllBytes(rawPath, data);
                Console.WriteLine($"MANIFEST {name}  {data.Length:N0} bytes");
                if (name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                    Unpack(data, Path.Combine(outRoot, "unpacked", safe));
            }
        }
    }

    static byte[] ReadRva(int rva, int size)
    {
        var block = pe.GetSectionData(rva);
        return block.GetReader().ReadBytes(Math.Min(size, block.Length));
    }

    static string Safe(string name) => name.Replace('/', '_').Replace('\\', '_');

    static void ParseDir(int dirRva, int depth, List<string> path)
    {
        var reader = pe.GetSectionData(dirRva).GetReader();
        reader.ReadBytes(12); // characteristics, timestamp, major, minor
        int named = reader.ReadUInt16();
        int ids = reader.ReadUInt16();
        int total = named + ids;
        for (int i = 0; i < total; i++)
        {
            uint nameField = reader.ReadUInt32();
            uint dataField = reader.ReadUInt32();
            string label;
            if ((nameField & 0x80000000) != 0)
            {
                int nameOff = rsrcBase + (int)(nameField & 0x7FFFFFFF);
                var nr = pe.GetSectionData(nameOff).GetReader();
                int len = nr.ReadUInt16();
                var chars = nr.ReadBytes(len * 2);
                label = System.Text.Encoding.Unicode.GetString(chars);
            }
            else label = "#" + nameField;

            if ((dataField & 0x80000000) != 0)
            {
                if (depth < 3) ParseDir(rsrcBase + (int)(dataField & 0x7FFFFFFF), depth + 1, new List<string>(path) { label });
            }
            else
            {
                int entryRva = rsrcBase + (int)dataField;
                var er = pe.GetSectionData(entryRva).GetReader();
                int rva = er.ReadInt32();
                int size = er.ReadInt32();
                var full = new List<string>(path) { label };
                if (full.Count >= 2 && (full[0] == "#10" || full[0].Equals("RCDATA", StringComparison.OrdinalIgnoreCase)))
                    rcdata[full[full.Count - 1]] = new RcData { Rva = rva, Size = size };
            }
        }
    }

    static void Unpack(byte[] data, string outDir)
    {
        Directory.CreateDirectory(outDir);
        int count = 0;
        using var ms = new MemoryStream(data);
        using var reader = new ResourceReader(ms);
        foreach (System.Collections.DictionaryEntry e in reader)
        {
            string key = (string)e.Key;
            object? val = e.Value;
            string path = Path.Combine(outDir, key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (val is byte[] b) File.WriteAllBytes(path, b);
            else if (val is Stream s) { using var fo = File.Create(path); s.CopyTo(fo); }
            else File.WriteAllText(path + ".txt", val?.ToString() ?? "");
            count++;
        }
        Console.WriteLine($"   UNPACKED {count} entries -> {outDir}");
    }
}
