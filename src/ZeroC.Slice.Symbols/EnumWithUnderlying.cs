// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace ZeroC.Slice.Symbols;

/// <summary>Represents a Slice enumeration with an underlying type.</summary>
public abstract class EnumWithUnderlying : Entity, ISymbol, IType
{
    /// <summary>Gets a value indicating whether this enumeration is unchecked.</summary>
    public required bool IsUnchecked { get; init; }

    /// <summary>Gets the underlying type of this enumeration.</summary>
    public required Builtin Underlying { get; init; }
}

/// <summary>Represents a Slice enumeration with a typed underlying value.</summary>
/// <typeparam name="T">The C# type that corresponds to the underlying Slice type.</typeparam>
public sealed class EnumWithUnderlying<T> : EnumWithUnderlying where T : struct, System.Numerics.INumber<T>{
    /// <summary>Gets the list of enumerators for this enumeration.</summary>
    public required ImmutableList<Enumerator> Enumerators { get; init; }

    /// <summary>Represents an enumerator in a Slice enumeration with an underlying type.</summary>
    public sealed class Enumerator : Entity
    {
        /// <summary>Gets the value of this enumerator.</summary>
        public required T Value { get; init; }
    }
}
