// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Symbols;

/// <summary>Represents the built-in Slice types, such as int32, string, etc.</summary>
public enum BuiltinKind
{
    /// <summary>Represents the built-in boolean type in Slice.</summary>
    Bool,

    /// <summary>Represents a signed 8-bit integer.</summary>
    Int8,

    /// <summary>Represents an unsigned 8-bit integer.</summary>
    UInt8,

    /// <summary>Represents a signed 16-bit integer.</summary>
    Int16,

    /// <summary>Represents an unsigned 16-bit integer.</summary>
    UInt16,

    /// <summary>Represents a signed 32-bit integer.</summary>
    Int32,

    /// <summary>Represents an unsigned 32-bit integer.</summary>
    UInt32,

    /// <summary>Represents a variable-length signed 32-bit integer.</summary>
    VarInt32,

    /// <summary>Represents a variable-length unsigned 32-bit integer.</summary>
    VarUInt32,

    /// <summary>Represents a signed 64-bit integer.</summary>
    Int64,

    /// <summary>Represents an unsigned 64-bit integer.</summary>
    UInt64,

    /// <summary>Represents a variable-length signed 62-bit integer. </summary>
    VarInt62,

    /// <summary>Represents a variable-length unsigned 62-bit integer.</summary>
    VarUInt62,

    /// <summary>Represents a 32-bit floating point number.</summary>
    Float32,

    /// <summary>Represents a 64-bit floating point number.</summary>
    Float64,

    /// <summary>Represents a string.</summary>
    String,
}


/// <summary>Represents one of the built-in types in Slice, such as int32, string, etc.</summary>
public class Builtin : IType
{
    /// <summary>The kind of the built-in type.</summary>
    public required BuiltinKind Kind { get; init; }
}
