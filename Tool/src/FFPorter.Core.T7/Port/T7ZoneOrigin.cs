using FFPorter.Core.T7.Formats;
using FFPorter.Core.T7.Harness;
using FFPorter.Core.T7.Link;
using FFPorter.Core.T7.Port.Converters;

namespace FFPorter.Core.T7.Port;

public sealed class T7ZoneOrigin
{
    private const int Enough = 8;
    private const ulong Inline = ulong.MaxValue;

    public int TiledImages { get; private init; }

    public int LinearImages { get; private init; }

    public bool IsPs4 => TiledImages >= Enough && TiledImages > LinearImages;

    public bool Tiled(long size, long ps4Size) => IsPs4 && size == ps4Size;

    public static T7ZoneOrigin Detect(T7WalkIndex pc)
    {
        int tiled = 0, linear = 0;
        void Count(long size, long pcSize, long ps4Size)
        {
            if (size != ps4Size)
                linear++;
            else if (size != pcSize)
                tiled++;
        }
        foreach (T7Walk.Asset asset in pc.Walk.Assets)
        {
            foreach (int index in T7ImageConverter.ImageStructReads(asset))
            {
                var info = new T7ImageConverter.ImageInfo(pc.Zone.AsSpan((int)asset.Reads[index].FileOffset, T7ImageConverter.PcImageSize));
                if (info.IsReference || !T7Gnm.IsKnownDxgi(info.Dxgi))
                    continue;
                try
                {
                    if (info.Streamed == 0 && info.PixelsPointer == Inline && info.PixelsSize != 0)
                    {
                        T7Gnm.Texture texture = info.Texture();
                        Count(info.PixelsSize, T7ImageConverter.PcSize(info, 0, info.Levels, null), texture.TotalSize);
                    }
                    if (info.LowPointer == Inline && info.LowSize != 0)
                    {
                        T7Gnm.Texture low = info.LowTexture();
                        Count(info.LowSize, T7ImageConverter.PcSize(info, 0, 1, low), low.TotalSize);
                    }
                }
                catch (InvalidDataException)
                {
                }
            }
        }
        return new T7ZoneOrigin { TiledImages = tiled, LinearImages = linear };
    }
}
