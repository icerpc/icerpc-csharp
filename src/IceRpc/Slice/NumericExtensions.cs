// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;

namespace IceRpc.Slice
{
    public static class NumericExtensions
    {
        public static int GetIceSizeLength(this int value) => ((long)value).GetIceSizeLength();

        public static int GetIceSizeLength(this long value)
        {
            if (value < EncodingDefinitions.VarLongMinValue || value > EncodingDefinitions.VarLongMaxValue)
            {
                throw new ArgumentOutOfRangeException($"varlong value '{value}' is out of range", nameof(value));
            }

            return (value << 2) switch
            {
                long b when b >= sbyte.MinValue && b <= sbyte.MaxValue => 1,
                long s when s >= short.MinValue && s <= short.MaxValue => 2,
                long i when i >= int.MinValue && i <= int.MaxValue => 4,
                _ => 8
            };
        }

        public static int GetIceSizeLength(this uint value) => ((ulong)value).GetIceSizeLength();

        public static int GetIceSizeLength(this ulong value)
        {
            if (value > EncodingDefinitions.VarULongMaxValue)
            {
                throw new ArgumentOutOfRangeException($"varulong value '{value}' is out of range", nameof(value));
            }

            return (value << 2) switch
            {
                ulong b when b <= byte.MaxValue => 1,
                ulong s when s <= ushort.MaxValue => 2,
                ulong i when i <= uint.MaxValue => 4,
                _ => 8
            };
        }
    }
}
