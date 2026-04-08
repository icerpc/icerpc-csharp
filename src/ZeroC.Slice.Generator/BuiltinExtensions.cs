// Copyright (c) ZeroC, Inc.

using ZeroC.Slice.Symbols;

namespace ZeroC.Slice.Generator;

/// <summary>C#-specific extension methods for <see cref="Builtin"/>.</summary>
internal static class BuiltinExtensions
{
    private static readonly Dictionary<BuiltinKind, string> _csTypeMap = new()
    {
        [BuiltinKind.Bool] = "bool",
        [BuiltinKind.Int8] = "sbyte",
        [BuiltinKind.UInt8] = "byte",
        [BuiltinKind.Int16] = "short",
        [BuiltinKind.UInt16] = "ushort",
        [BuiltinKind.Int32] = "int",
        [BuiltinKind.UInt32] = "uint",
        [BuiltinKind.VarInt32] = "int",
        [BuiltinKind.VarUInt32] = "uint",
        [BuiltinKind.Int64] = "long",
        [BuiltinKind.UInt64] = "ulong",
        [BuiltinKind.VarInt62] = "long",
        [BuiltinKind.VarUInt62] = "ulong",
        [BuiltinKind.Float32] = "float",
        [BuiltinKind.Float64] = "double",
        [BuiltinKind.String] = "string",
    };

    extension(Builtin builtin)
    {
        /// <summary>The C# type name for this built-in (e.g. "int", "string").</summary>
        internal string CSType => _csTypeMap[builtin.Kind];

        /// <summary>Whether the C# type is a value type.</summary>
        internal bool IsValueType => builtin.Kind != BuiltinKind.String;

        /// <summary>The encoder/decoder method suffix for this built-in (e.g. "Int32", "String").</summary>
        internal string Suffix => builtin.Kind.ToString();

        /// <summary>Whether this built-in has a fixed wire size (i.e. can be memcpy'd in sequences).</summary>
        internal bool IsFixedSize => builtin.Kind is not (
            BuiltinKind.String or
            BuiltinKind.VarInt32 or
            BuiltinKind.VarUInt32 or
            BuiltinKind.VarInt62 or
            BuiltinKind.VarUInt62);
    }
}
