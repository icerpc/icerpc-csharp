// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace ZeroC.Slice.Symbols;

/// <summary>Represents a Slice basic enumeration.</summary>
public abstract class BasicEnum : Entity, ISymbol, IType
{
    /// <summary>Gets a value indicating whether this enumeration is unchecked.</summary>
    public required bool IsUnchecked { get; init; }

    /// <summary>Gets the underlying type of this basic enumeration.</summary>
    public required Builtin Underlying { get; init; }

    /// <summary>Finds an enumerator by its Slice identifier.</summary>
    public abstract Entity? FindEnumeratorByIdentifier(string identifier);
}

/// <summary>Represents a Slice basic enumeration with a typed underlying value.</summary>
/// <typeparam name="T">The C# type that corresponds to the underlying Slice type.</typeparam>
public sealed class BasicEnum<T> : BasicEnum where T : struct, System.Numerics.INumber<T>{
    /// <summary>Gets the list of enumerators for this enumeration.</summary>
    public required ImmutableList<Enumerator> Enumerators { get; init; }

    /// <inheritdoc/>
    public override Entity? FindEnumeratorByIdentifier(string identifier) =>
        Enumerators.FirstOrDefault(e => e.Identifier == identifier);

    /// <summary>Represents an enumerator in a Slice basic enumeration.</summary>
    public sealed class Enumerator : Entity
    {
        /// <summary>Gets the value of this enumerator.</summary>
        public required T Value { get; init; }
    }
}
