// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Symbols;

/// <summary>
/// Represents the built-in Slice types, such as int32, string, etc.
/// </summary>
public enum BuiltinKind
{
    /// <summary>
    /// Represents the built-in boolean type in Slice.
    /// </summary>
    Bool,

    /// <summary>
    /// Represents a signed 8-bit integer.
    /// </summary>
    Int8,

    /// <summary>
    /// Represents an unsigned 8-bit integer.
    /// </summary>
    UInt8,

    /// <summary>
    /// Represents a signed 16-bit integer.
    /// </summary>
    Int16,

    /// <summary>
    /// Represents an unsigned 16-bit integer.
    /// </summary>
    UInt16,

    /// <summary>
    /// Represents a signed 32-bit integer.
    /// </summary>
    Int32,

    /// <summary>
    /// Represents an unsigned 32-bit integer.
    /// </summary>
    UInt32,

    /// <summary>
    /// Represents a variable-length signed 32-bit integer.
    /// </summary>
    VarInt32,

    /// <summary>
    /// Represents a variable-length unsigned 32-bit integer.
    /// </summary>
    VarUInt32,

    /// <summary>
    /// Represents a signed 64-bit integer.
    /// </summary>
    Int64,

    /// <summary>
    /// Represents an unsigned 64-bit integer.
    /// </summary>
    UInt64,

    /// <summary>
    /// Represents a variable-length signed 62-bit integer.
    /// </summary>
    VarInt62,

    /// <summary>
    /// Represents a variable-length unsigned 62-bit integer.
    /// </summary>
    VarUInt62,

    /// <summary>
    /// Represents a 32-bit floating point number.
    /// </summary>
    Float32,

    /// <summary>
    /// Represents a 64-bit floating point number.
    /// </summary>
    Float64,

    /// <summary>
    /// Represents a string.
    /// </summary>
    String,
}


/// <summary>
/// Represents one of the built-in types in Slice, such as int32, string, etc.
/// </summary>
public record class Builtin : Symbol
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

    /// <summary>
    /// The kind of the built-in type.
    /// </summary>
    public required BuiltinKind Kind { get; init; }

    /// <summary>The C# type name for this built-in (e.g. "int", "string").</summary>
    public string CsType => _csTypeMap[Kind];

    /// <summary>The encoder/decoder method suffix for this built-in (e.g. "Int32", "String").</summary>
    public string Suffix => Kind.ToString();

    /// <summary>Whether the C# type is a value type.</summary>
    public bool IsValueType => Kind != BuiltinKind.String;
}
