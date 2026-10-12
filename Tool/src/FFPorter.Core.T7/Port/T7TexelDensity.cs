using System.Buffers.Binary;
using FFPorter.Core.T7.Harness;
using FFPorter.Core.T7.Link;
using FFPorter.Core.T7.Port.Converters;

namespace FFPorter.Core.T7.Port;

public sealed class T7TexelDensity
{
    private const int StreamedParts = 2, MostParts = 4, TextureCount = 0x270, TextureTable = 0x280, TextureEntry = 32;

    private readonly T7WalkIndex _pc;
    private readonly Func<string, byte[]?> _image;
    private readonly T7StockImages? _stock;
    private readonly Dictionary<string, long> _byName = new(StringComparer.Ordinal);
    private readonly HashSet<long> _roots = [];
    private readonly Dictionary<long, float?> _scales = [];

    public T7TexelDensity(T7WalkIndex pc, Func<string, byte[]?> image, T7StockImages? stock = null)
    {
        _pc = pc;
        _image = image;
        _stock = stock;
        foreach (T7Walk.Asset asset in pc.Walk.Assets)
        {
            foreach (T7Walk.Registration registration in asset.Registrations)
            {
                if (registration.Type != T7AssetTypes.Material || string.IsNullOrEmpty(registration.Name) || _byName.ContainsKey(registration.Name))
                    continue;
                for (int i = asset.Reads.Count - 1; i >= 0; i--)
                {
                    T7Walk.Span read = asset.Reads[i];
                    if (read.FileOffset >= registration.FilePos || read.Kind != T7WalkKind.Read || read.Size != T7MaterialConverter.PcSize || read.At != registration.Header)
                        continue;
                    _byName[registration.Name] = read.FileOffset;
                    _roots.Add(read.FileOffset);
                    break;
                }
            }
        }
    }

    public static float? Scale(IEnumerable<(int Width, int Height, int Parts)> textures) =>
        Scale(textures.Select(t => ((long)t.Width * t.Height, t.Parts)));

    public static float? Scale(IEnumerable<(long Area, int Parts)> textures)
    {
        double full = 0, resident = 0;
        foreach ((long area, int parts) in textures)
        {
            if (parts < StreamedParts || area <= 0)
                continue;
            double size = Math.Sqrt(area);
            full += size;
            resident += size / (1 << (Math.Min(parts, MostParts) - 1));
        }
        if (resident <= 0)
            return null;
        double ratio = full / resident;
        return (float)(1 / (ratio * ratio));
    }

    public float? Of(long materialField)
    {
        long root = Root(materialField);
        if (root < 0)
            return null;
        lock (_scales)
        {
            if (!_scales.TryGetValue(root, out float? scale))
                _scales[root] = scale = Scale(Textures(root));
            return scale;
        }
    }

    private long Root(long field)
    {
        if (_pc.TryReferencedName(field, out int type, out string name) && type == T7AssetTypes.Material && _byName.TryGetValue(name, out long named))
            return named;
        return _pc.TryFollow(field, out long target) && _roots.Contains(target) ? target : -1;
    }

    private List<(long Area, int Parts)> Textures(long root)
    {
        var textures = new List<(long, int)>();
        byte[] zone = _pc.Zone;
        int count = zone[root + TextureCount];
        if (count == 0 || BinaryPrimitives.ReadUInt64LittleEndian(zone.AsSpan((int)root + TextureTable)) == 0
            || !_pc.TryFollow(root + TextureTable, out long table) || table < 0 || table + (long)TextureEntry * count > zone.Length)
            return textures;
        for (int entry = 0; entry < count; entry++)
        {
            long field = table + TextureEntry * entry;
            if (!_pc.TryReferencedName(field, out int type, out string name) || type != T7AssetTypes.Image)
                continue;
            if (_image(name) is { } data && new T7ImageConverter.ImageInfo(data) is { IsReference: false } info)
                textures.Add(((long)info.Width * info.Height, info.Parts));
            else if (_stock != null && _stock.TryGet(name.TrimStart(','), out long area, out int parts))
                textures.Add((area, parts));
        }
        return textures;
    }
}
