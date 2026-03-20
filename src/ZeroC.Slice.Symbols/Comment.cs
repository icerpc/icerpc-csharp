// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace ZeroC.Slice.Symbols;

/// <summary>Represents a doc comment attached to a Slice entity, with links resolved to entities.</summary>
public sealed class Comment
{
    /// <summary>Gets the overview message components.</summary>
    public required ImmutableList<CommentMessageComponent> Overview { get; init; }

    /// <summary>Gets the @see tag references.</summary>
    public required ImmutableList<CommentLink> SeeTags { get; init; }
}

/// <summary>A component of a doc comment overview message.</summary>
public abstract record class CommentMessageComponent
{
    /// <summary>Plain text.</summary>
    public sealed record class Text(string Value) : CommentMessageComponent;

    /// <summary>A link to another entity.</summary>
    public sealed record class Link(CommentLink Target) : CommentMessageComponent;
}

/// <summary>A link to a Slice entity in a doc comment.</summary>
public abstract record class CommentLink
{
    /// <summary>A resolved link to a known entity.</summary>
    public sealed record class Resolved(Entity Entity) : CommentLink;

    /// <summary>An unresolved link where only the raw identifier is available.</summary>
    public sealed record class Unresolved(string Identifier) : CommentLink;
}
