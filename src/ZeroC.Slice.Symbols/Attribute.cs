// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace ZeroC.Slice.Symbols;

/// <summary>
/// Represents an attribute applied to a Slice declaration.
/// </summary>
public record class Attribute
{
    /// <summary>The directive for the cs::attribute attribute.</summary>
    public const string CsAttribute = "cs::attribute";

    /// <summary>The directive for the cs::identifier attribute.</summary>
    public const string CsIdentifier = "cs::identifier";

    /// <summary>The directive for the cs::internal attribute.</summary>
    public const string CsInternal = "cs::internal";

    /// <summary>The directive for the cs::namespace attribute.</summary>
    public const string CsNamespace = "cs::namespace";

    /// <summary>The directive for the cs::readonly attribute.</summary>
    public const string CsReadonly = "cs::readonly";

    /// <summary>
    /// The attributes directive, e.g. "cs::readonly" for [cs::readonly] attribute.
    /// </summary>
    public required string Directive { get; init; }

    /// <summary>
    /// The arguments for the attribute, if any.
    /// </summary>
    public required ImmutableList<string> Args { get; init; }
}
