using System.Numerics;

namespace FFPorter.Core.Common.Native;

internal static class CpuContext
{
    public const int MxCsr = 0x34;
    public const int EFlags = 0x44;
    public const int Gpr = 0x78;
    public const int Rip = 0xF8;
    public const int Xmm = 0x1A0;
}

internal static unsafe class CpuEmulator
{
    private const ulong Carry = 1, Zero = 1 << 6, Sign = 1 << 7, Overflow = 1 << 11, Parity = 1 << 2, Adjust = 1 << 4;

    private static readonly ulong[] Upper = new ulong[32];

    private struct Instruction
    {
        public bool Vex;
        public int Map;
        public int Pp;
        public bool W;
        public bool L;
        public int V;
        public int Opcode;
        public int Mod;
        public int Reg;
        public int Rm;
        public bool Memory;
        public ulong Address;
        public int Length;
        public bool Operand16;
        public bool Rep;
        public bool RepNe;
    }

    public static bool Execute(nint context, ReadOnlySpan<byte> code, ulong rip, out string? problem)
    {
        problem = null;
        if (!Decode(context, code, rip, out Instruction op, out problem))
            return false;
        bool done = op.Vex ? ExecuteVex(context, ref op, out problem) : ExecuteLegacy(context, ref op, out problem);
        if (!done)
            return false;
        *(ulong*)(context + CpuContext.Rip) = rip + (ulong)op.Length;
        return true;
    }

    private static bool Decode(nint context, ReadOnlySpan<byte> code, ulong rip, out Instruction op, out string? problem)
    {
        op = default;
        problem = null;
        int i = 0;
        bool rexW = false, rexR = false, rexX = false, rexB = false;
        while (i < code.Length)
        {
            byte b = code[i];
            if (b == 0x66)
                op.Operand16 = true;
            else if (b == 0xF3)
                op.Rep = true;
            else if (b == 0xF2)
                op.RepNe = true;
            else if (b is 0x2E or 0x3E or 0x26 or 0x36)
            {
            }
            else if (b is 0x64 or 0x65 or 0x67 or 0xF0)
            {
                problem = "segment, address-size or lock prefix";
                return false;
            }
            else
                break;
            i++;
        }
        if (i + 1 >= code.Length)
        {
            problem = "truncated instruction";
            return false;
        }
        if (code[i] == 0xC5)
        {
            byte b1 = code[i + 1];
            op.Vex = true;
            rexR = (b1 & 0x80) == 0;
            op.V = (~b1 >> 3) & 15;
            op.L = (b1 & 4) != 0;
            op.Pp = b1 & 3;
            op.Map = 1;
            i += 2;
        }
        else if (code[i] == 0xC4)
        {
            if (i + 2 >= code.Length)
            {
                problem = "truncated instruction";
                return false;
            }
            byte b1 = code[i + 1], b2 = code[i + 2];
            op.Vex = true;
            rexR = (b1 & 0x80) == 0;
            rexX = (b1 & 0x40) == 0;
            rexB = (b1 & 0x20) == 0;
            op.Map = b1 & 0x1F;
            op.W = (b2 & 0x80) != 0;
            op.V = (~b2 >> 3) & 15;
            op.L = (b2 & 4) != 0;
            op.Pp = b2 & 3;
            i += 3;
        }
        else
        {
            if ((code[i] & 0xF0) == 0x40)
            {
                byte rex = code[i];
                rexW = (rex & 8) != 0;
                rexR = (rex & 4) != 0;
                rexX = (rex & 2) != 0;
                rexB = (rex & 1) != 0;
                op.W = rexW;
                i++;
            }
            if (i + 1 >= code.Length || code[i] != 0x0F)
            {
                problem = "not an extended opcode";
                return false;
            }
            i++;
            op.Map = 1;
            if (code[i] == 0x38)
            {
                op.Map = 2;
                i++;
            }
            else if (code[i] == 0x3A)
            {
                op.Map = 3;
                i++;
            }
        }
        if (i + 1 >= code.Length)
        {
            problem = "truncated instruction";
            return false;
        }
        op.Opcode = code[i++];
        byte modrm = code[i++];
        op.Mod = modrm >> 6;
        op.Reg = ((modrm >> 3) & 7) | (rexR ? 8 : 0);
        int rm = modrm & 7;
        if (op.Mod == 3)
        {
            op.Rm = rm | (rexB ? 8 : 0);
            op.Length = i;
            return true;
        }
        op.Memory = true;
        ulong address = 0;
        bool ripRelative = false;
        if (rm == 4)
        {
            if (i >= code.Length)
            {
                problem = "truncated instruction";
                return false;
            }
            byte sib = code[i++];
            int scale = sib >> 6;
            int index = ((sib >> 3) & 7) | (rexX ? 8 : 0);
            int baseReg = (sib & 7) | (rexB ? 8 : 0);
            if (index != 4)
                address += Gpr(context, index) << scale;
            if ((sib & 7) == 5 && op.Mod == 0)
            {
                address += (ulong)(long)ReadInt32(code, ref i);
            }
            else
                address += Gpr(context, baseReg);
        }
        else if (rm == 5 && op.Mod == 0)
        {
            ripRelative = true;
            address += (ulong)(long)ReadInt32(code, ref i);
        }
        else
            address += Gpr(context, rm | (rexB ? 8 : 0));
        if (op.Mod == 1)
        {
            if (i >= code.Length)
            {
                problem = "truncated instruction";
                return false;
            }
            address += (ulong)(long)(sbyte)code[i++];
        }
        else if (op.Mod == 2)
            address += (ulong)(long)ReadInt32(code, ref i);
        if (i > code.Length)
        {
            problem = "truncated instruction";
            return false;
        }
        op.Length = i;
        op.Address = ripRelative ? address + rip + (ulong)i : address;
        return true;
    }

    private static int ReadInt32(ReadOnlySpan<byte> code, ref int i)
    {
        if (i + 4 > code.Length)
        {
            i += 4;
            return 0;
        }
        int value = BitConverter.ToInt32(code[i..(i + 4)]);
        i += 4;
        return value;
    }

    private static bool ExecuteVex(nint context, ref Instruction op, out string? problem)
    {
        problem = null;
        int width = op.L ? 32 : 16;
        Span<byte> a = stackalloc byte[32];
        Span<byte> b = stackalloc byte[32];
        if (op.Map == 1)
        {
            switch (op.Opcode, op.Pp)
            {
                case (0x10, 0) or (0x10, 1) or (0x28, 0) or (0x28, 1) or (0x6F, 1) or (0x6F, 2) or (0xF0, 3):
                    Source(context, ref op, b, width);
                    SetVector(context, op.Reg, b, op.L);
                    return true;
                case (0x11, 0) or (0x11, 1) or (0x29, 0) or (0x29, 1) or (0x7F, 1) or (0x7F, 2) or (0x2B, 0) or (0x2B, 1) or (0xE7, 1):
                    GetVector(context, op.Reg, a);
                    if (op.Memory)
                        a[..width].CopyTo(new Span<byte>((void*)op.Address, width));
                    else
                        SetVector(context, op.Rm, a, op.L);
                    return true;
                case (0x10, 2) or (0x10, 3):
                {
                    int size = op.Pp == 2 ? 4 : 8;
                    if (op.Memory)
                    {
                        a.Clear();
                        new ReadOnlySpan<byte>((void*)op.Address, size).CopyTo(a);
                    }
                    else
                    {
                        GetVector(context, op.V, a);
                        GetVector(context, op.Rm, b);
                        b[..size].CopyTo(a);
                    }
                    SetVector(context, op.Reg, a, false);
                    return true;
                }
                case (0x11, 2) or (0x11, 3):
                {
                    int size = op.Pp == 2 ? 4 : 8;
                    GetVector(context, op.Reg, b);
                    if (op.Memory)
                        b[..size].CopyTo(new Span<byte>((void*)op.Address, size));
                    else
                    {
                        GetVector(context, op.V, a);
                        b[..size].CopyTo(a);
                        SetVector(context, op.Rm, a, false);
                    }
                    return true;
                }
                case (0x54, 0) or (0x54, 1) or (0xDB, 1):
                    return Bitwise(context, ref op, a, b, width, 0);
                case (0x55, 0) or (0x55, 1) or (0xDF, 1):
                    return Bitwise(context, ref op, a, b, width, 1);
                case (0x56, 0) or (0x56, 1) or (0xEB, 1):
                    return Bitwise(context, ref op, a, b, width, 2);
                case (0x57, 0) or (0x57, 1) or (0xEF, 1):
                    return Bitwise(context, ref op, a, b, width, 3);
                case (0x6E, 1):
                {
                    a.Clear();
                    ulong value = op.Memory ? (op.W ? *(ulong*)op.Address : *(uint*)op.Address) : op.W ? Gpr(context, op.Rm) : (uint)Gpr(context, op.Rm);
                    BitConverter.TryWriteBytes(a, value);
                    SetVector(context, op.Reg, a, false);
                    return true;
                }
                case (0x7E, 1):
                {
                    GetVector(context, op.Reg, a);
                    ulong value = op.W ? BitConverter.ToUInt64(a) : BitConverter.ToUInt32(a);
                    if (op.Memory)
                    {
                        if (op.W)
                            *(ulong*)op.Address = value;
                        else
                            *(uint*)op.Address = (uint)value;
                    }
                    else
                        SetGpr(context, op.Rm, value);
                    return true;
                }
                case (0x7E, 2):
                {
                    b.Clear();
                    if (op.Memory)
                        new ReadOnlySpan<byte>((void*)op.Address, 8).CopyTo(b);
                    else
                    {
                        GetVector(context, op.Rm, a);
                        a[..8].CopyTo(b);
                    }
                    SetVector(context, op.Reg, b, false);
                    return true;
                }
                case (0xD6, 1):
                    GetVector(context, op.Reg, a);
                    if (op.Memory)
                        a[..8].CopyTo(new Span<byte>((void*)op.Address, 8));
                    else
                    {
                        b.Clear();
                        a[..8].CopyTo(b);
                        SetVector(context, op.Rm, b, false);
                    }
                    return true;
                case (0x77, 0):
                    Array.Clear(Upper);
                    if (op.L)
                    {
                        a.Clear();
                        for (int r = 0; r < 16; r++)
                            SetVector(context, r, a, false);
                    }
                    return true;
            }
        }
        else if (op.Map == 2)
        {
            switch (op.Opcode, op.Pp)
            {
                case (0xF2, 0):
                {
                    ulong left = Gpr(context, op.V), right = RmGpr(context, ref op);
                    ulong result = Mask(~left & right, op.W);
                    SetGpr(context, op.Reg, result);
                    SetLogicFlags(context, result, op.W, false);
                    return true;
                }
                case (0xF7, 0):
                {
                    ulong source = RmGpr(context, ref op), control = Gpr(context, op.V);
                    int start = (int)(control & 0xFF), length = (int)((control >> 8) & 0xFF), bits = op.W ? 64 : 32;
                    ulong result = 0;
                    if (start < bits)
                    {
                        result = Mask(source, op.W) >> start;
                        if (length < 64)
                            result &= (1UL << length) - 1;
                    }
                    result = Mask(result, op.W);
                    SetGpr(context, op.Reg, result);
                    SetLogicFlags(context, result, op.W, false);
                    return true;
                }
                case (0xF3, 0):
                {
                    ulong source = Mask(RmGpr(context, ref op), op.W);
                    ulong result;
                    bool carry;
                    switch (op.Reg & 7)
                    {
                        case 1:
                            result = source & (source - 1);
                            carry = source == 0;
                            break;
                        case 2:
                            result = source ^ (source - 1);
                            carry = source == 0;
                            break;
                        case 3:
                            result = source & (0 - source);
                            carry = source != 0;
                            break;
                        default:
                            problem = $"VEX 0F38 F3 /{op.Reg & 7}";
                            return false;
                    }
                    result = Mask(result, op.W);
                    SetGpr(context, op.V, result);
                    SetLogicFlags(context, result, op.W, carry);
                    return true;
                }
            }
        }
        problem = $"VEX map {op.Map} opcode {op.Opcode:X2} pp {op.Pp}{(op.W ? " W1" : "")}{(op.L ? " L1" : "")}";
        return false;
    }

    private static bool Bitwise(nint context, ref Instruction op, Span<byte> a, Span<byte> b, int width, int kind)
    {
        GetVector(context, op.V, a);
        Source(context, ref op, b, width);
        for (int k = 0; k < width; k++)
        {
            a[k] = kind switch
            {
                0 => (byte)(a[k] & b[k]),
                1 => (byte)(~a[k] & b[k]),
                2 => (byte)(a[k] | b[k]),
                _ => (byte)(a[k] ^ b[k]),
            };
        }
        SetVector(context, op.Reg, a, op.L);
        return true;
    }

    private static bool ExecuteLegacy(nint context, ref Instruction op, out string? problem)
    {
        problem = null;
        if (op.Map == 1 && op.Rep && op.Opcode is 0xBD or 0xBC or 0xB8)
        {
            int bits = op.W ? 64 : op.Operand16 ? 16 : 32;
            ulong source = op.Memory ? bits switch { 64 => *(ulong*)op.Address, 32 => *(uint*)op.Address, _ => *(ushort*)op.Address } : RmGpr(context, ref op);
            source &= bits == 64 ? ulong.MaxValue : (1UL << bits) - 1;
            ulong result = op.Opcode switch
            {
                0xBD => source == 0 ? (ulong)bits : (ulong)(BitOperations.LeadingZeroCount(source) - (64 - bits)),
                0xBC => source == 0 ? (ulong)bits : (ulong)BitOperations.TrailingZeroCount(source),
                _ => (ulong)BitOperations.PopCount(source),
            };
            WriteGpr(context, op.Reg, result, bits);
            ulong flags = (ulong)*(uint*)(context + CpuContext.EFlags) & ~(Carry | Zero | Sign | Overflow | Parity | Adjust);
            if (op.Opcode == 0xB8)
                flags |= source == 0 ? Zero : 0;
            else
            {
                flags |= result == (ulong)bits ? Carry : 0;
                flags |= result == 0 ? Zero : 0;
            }
            *(uint*)(context + CpuContext.EFlags) = (uint)flags;
            return true;
        }
        if (op.Map == 2 && op.Opcode is 0xF0 or 0xF1 && op.Memory && !op.Rep && !op.RepNe)
        {
            int bits = op.W ? 64 : op.Operand16 ? 16 : 32;
            if (op.Opcode == 0xF0)
            {
                ulong value = bits switch
                {
                    64 => BinaryPrimitivesReverse(*(ulong*)op.Address),
                    32 => System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(*(uint*)op.Address),
                    _ => System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(*(ushort*)op.Address),
                };
                WriteGpr(context, op.Reg, value, bits);
            }
            else
            {
                ulong value = Gpr(context, op.Reg);
                switch (bits)
                {
                    case 64:
                        *(ulong*)op.Address = BinaryPrimitivesReverse(value);
                        break;
                    case 32:
                        *(uint*)op.Address = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness((uint)value);
                        break;
                    default:
                        *(ushort*)op.Address = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness((ushort)value);
                        break;
                }
            }
            return true;
        }
        problem = $"opcode map {op.Map} {op.Opcode:X2}";
        return false;
    }

    private static ulong BinaryPrimitivesReverse(ulong value) => System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(value);

    private static void Source(nint context, ref Instruction op, Span<byte> target, int width)
    {
        target.Clear();
        if (op.Memory)
            new ReadOnlySpan<byte>((void*)op.Address, width).CopyTo(target);
        else
        {
            GetVector(context, op.Rm, target);
            if (width == 16)
                target[16..].Clear();
        }
    }

    private static void GetVector(nint context, int register, Span<byte> target)
    {
        new ReadOnlySpan<byte>((void*)(context + CpuContext.Xmm + 16 * register), 16).CopyTo(target);
        BitConverter.TryWriteBytes(target[16..], Upper[2 * register]);
        BitConverter.TryWriteBytes(target[24..], Upper[2 * register + 1]);
    }

    private static void SetVector(nint context, int register, ReadOnlySpan<byte> value, bool wide)
    {
        value[..16].CopyTo(new Span<byte>((void*)(context + CpuContext.Xmm + 16 * register), 16));
        Upper[2 * register] = wide ? BitConverter.ToUInt64(value[16..]) : 0;
        Upper[2 * register + 1] = wide ? BitConverter.ToUInt64(value[24..]) : 0;
    }

    private static ulong Gpr(nint context, int register) => *(ulong*)(context + CpuContext.Gpr + 8 * register);

    private static void SetGpr(nint context, int register, ulong value) => *(ulong*)(context + CpuContext.Gpr + 8 * register) = value;

    private static void WriteGpr(nint context, int register, ulong value, int bits)
    {
        if (bits == 16)
            *(ushort*)(context + CpuContext.Gpr + 8 * register) = (ushort)value;
        else
            SetGpr(context, register, bits == 32 ? (uint)value : value);
    }

    private static ulong RmGpr(nint context, ref Instruction op) =>
        op.Memory ? (op.W ? *(ulong*)op.Address : *(uint*)op.Address) : Mask(Gpr(context, op.Rm), op.W);

    private static ulong Mask(ulong value, bool wide) => wide ? value : (uint)value;

    private static void SetLogicFlags(nint context, ulong result, bool wide, bool carry)
    {
        ulong flags = (ulong)*(uint*)(context + CpuContext.EFlags) & ~(Carry | Zero | Sign | Overflow);
        flags |= result == 0 ? Zero : 0;
        flags |= (wide ? (result >> 63) : (result >> 31)) != 0 ? Sign : 0;
        flags |= carry ? Carry : 0;
        *(uint*)(context + CpuContext.EFlags) = (uint)flags;
    }
}
