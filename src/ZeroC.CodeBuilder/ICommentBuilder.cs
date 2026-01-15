// Copyright (c) ZeroC, Inc.

namespace ZeroC.CodeBuilder;

/// <summary>Represents a builder that can have XML documentation comments added to it.</summary>
/// <typeparam name="T">The concrete builder type for fluent chaining.</typeparam>
public interface ICommentBuilder<T>
    where T : ICommentBuilder<T>
{
    /// <summary>Adds an XML documentation comment.</summary>
    /// <param name="tag">The comment tag (e.g., "summary", "returns").</param>
    /// <param name="content">The comment content.</param>
    /// <returns>The builder instance for method chaining.</returns>
    T AddComment(string tag, string content);

    /// <summary>Adds an XML documentation comment with an attribute.</summary>
    /// <param name="tag">The comment tag.</param>
    /// <param name="attributeName">The attribute name.</param>
    /// <param name="attributeValue">The attribute value.</param>
    /// <param name="content">The comment content.</param>
    /// <returns>The builder instance for method chaining.</returns>
    T AddComment(string tag, string attributeName, string attributeValue, string content);

    /// <summary>Adds multiple XML documentation comments.</summary>
    /// <param name="comments">The comments to add.</param>
    /// <returns>The builder instance for method chaining.</returns>
    T AddComments(IEnumerable<CommentTag> comments);
}
