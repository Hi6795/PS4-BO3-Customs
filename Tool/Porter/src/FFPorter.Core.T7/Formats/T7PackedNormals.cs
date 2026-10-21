using System.Buffers.Binary;

namespace FFPorter.Core.T7.Formats;

public enum T7NormalForm
{
    Pc,
    Ps4,
    Ps4AsPc,
}

public static class T7PackedNormals
{
    private const double UnitLength = 511.0, Tolerance = 2.0;
    private const int EnoughNeighbours = 32;

    public static uint FromPc(uint value)
    {
        int x = (int)(value & 0x3FF) - 512, y = (int)((value >> 10) & 0x3FF) - 512, z = (int)((value >> 20) & 0x3FF) - 512;
        double length = Math.Sqrt((double)x * x + (double)y * y + (double)z * z);
        int Component(int c)
        {
            double s = length > 0 ? Math.Round(c / length * UnitLength, MidpointRounding.ToEven) : c;
            return Math.Clamp((int)s, -511, 511);
        }
        return (value & 0xC0000000) | ((uint)(Component(x) & 0x3FF)) | ((uint)(Component(y) & 0x3FF) << 10) | ((uint)(Component(z) & 0x3FF) << 20);
    }

    public static uint FromPs4AsPc(uint value)
    {
        uint output = value & 0xC0000000;
        for (int shift = 0; shift < 30; shift += 10)
        {
            int c = (int)((value >> shift) & 0x3FF);
            output |= (uint)((c >= 512 ? c - 512 : c - 511) & 0x3FF) << shift;
        }
        return output;
    }

    public static T7NormalForm Classify(ReadOnlySpan<byte> data, long first, int stride, long count, ReadOnlySpan<int> fields, bool ps4Zone)
    {
        long vectors = 0, biasedFits = 0, signedFits = 0, middle = 0, beside = 0;
        double biasedError = 0, unbiasedError = 0;
        for (long i = 0; i < count; i++)
        {
            foreach (int field in fields)
            {
                uint value = BinaryPrimitives.ReadUInt32LittleEndian(data[(int)(first + i * stride + field)..]);
                int x = (int)(value & 0x3FF), y = (int)((value >> 10) & 0x3FF), z = (int)((value >> 20) & 0x3FF);
                double biased = Length(x - 512, y - 512, z - 512) - UnitLength;
                double signed = Length(Signed(x), Signed(y), Signed(z)) - UnitLength;
                double unbiased = Length(Unbiased(x), Unbiased(y), Unbiased(z)) - UnitLength;
                vectors++;
                if (Math.Abs(biased) < Tolerance)
                    biasedFits++;
                if (Math.Abs(signed) < Tolerance)
                    signedFits++;
                biasedError += biased * biased;
                unbiasedError += unbiased * unbiased;
                middle += (x == 511 ? 1 : 0) + (y == 511 ? 1 : 0) + (z == 511 ? 1 : 0);
                beside += (x is 510 or 513 ? 1 : 0) + (y is 510 or 513 ? 1 : 0) + (z is 510 or 513 ? 1 : 0);
            }
        }
        if (vectors == 0)
            return T7NormalForm.Pc;
        bool biasedHolds = biasedFits * 10 >= vectors * 9, signedHolds = signedFits * 10 >= vectors * 9;
        if (signedHolds && biasedFits * 2 <= vectors)
            return T7NormalForm.Ps4;
        if (biasedHolds && signedFits * 2 <= vectors)
            return ps4Zone && middle == 0 && beside >= EnoughNeighbours && unbiasedError < biasedError ? T7NormalForm.Ps4AsPc : T7NormalForm.Pc;
        return ps4Zone ? T7NormalForm.Ps4 : T7NormalForm.Pc;
    }

    public static void Convert(Span<byte> data, long first, int stride, long count, ReadOnlySpan<int> fields, T7NormalForm form)
    {
        if (form == T7NormalForm.Ps4)
            return;
        for (long i = 0; i < count; i++)
        {
            foreach (int field in fields)
            {
                Span<byte> at = data[(int)(first + i * stride + field)..];
                uint value = BinaryPrimitives.ReadUInt32LittleEndian(at);
                BinaryPrimitives.WriteUInt32LittleEndian(at, form == T7NormalForm.Pc ? FromPc(value) : FromPs4AsPc(value));
            }
        }
    }

    private static double Length(int x, int y, int z) => Math.Sqrt((double)x * x + (double)y * y + (double)z * z);

    private static int Signed(int c) => c >= 512 ? c - 1024 : c;

    private static int Unbiased(int c) => c >= 512 ? c - 512 : c - 511;
}
