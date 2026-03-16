// Copyright (c) ZeroC, Inc.

namespace IceRpc.Ice.Codec;

/// <summary>Assigns a Ice type ID to a class, interface, or struct.</summary>
/// <remarks>The Ice compiler assigns Ice type IDs to interfaces and record structs it generates from Ice
/// interfaces. It also assigns Ice type IDs to classes generated from Ice classes and exceptions.</remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct, Inherited = false)]
public sealed class IceTypeIdAttribute : Attribute
{
    /// <summary>Gets the Ice type ID.</summary>
    /// <value>The Ice type ID string.</value>
    public string Value { get; }

    /// <summary>Constructs a Ice type ID attribute.</summary>
    /// <param name="value">The Ice type ID.</param>>
    public IceTypeIdAttribute(string value) => Value = value;
}

/// <summary>Assigns a compact Ice type ID to a class.</summary>
/// <remarks>The Ice compiler assigns both a Ice type ID and a compact Ice type ID to the mapped class of a Ice
/// class that specifies a compact type ID.</remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CompactIceTypeIdAttribute : Attribute
{
    /// <summary>Gets the compact Ice type ID.</summary>
    /// <value>The compact Ice type ID numeric value.</value>
    public int Value { get; }

    /// <summary>Constructs a compact Ice type ID attribute.</summary>
    /// <param name="value">The compact type ID.</param>>
    public CompactIceTypeIdAttribute(int value) => Value = value;
}
