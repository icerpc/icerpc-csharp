// Copyright (c) ZeroC, Inc.

using System.Text;

namespace ZeroC.CodeBuilder;

/// <summary>Represents an XML documentation comment tag.</summary>
public sealed class CommentTag
{
    /// <summary>Gets the tag name (e.g., "summary", "param", "returns").</summary>
    public string Tag { get; }

    /// <summary>Gets the tag content.</summary>
    public string Content { get; }

    /// <summary>Gets the optional attribute name.</summary>
    public string? AttributeName { get; }

    /// <summary>Gets the optional attribute value.</summary>
    public string? AttributeValue { get; }

    /// <summary>Initializes a new instance of the <see cref="CommentTag"/> class.</summary>
    /// <param name="tag">The tag name.</param>
    /// <param name="content">The tag content.</param>
    public CommentTag(string tag, string content)
    {
        Tag = tag;
        Content = content;
    }

    /// <summary>Initializes a new instance of the <see cref="CommentTag"/> class with an attribute.</summary>
    /// <param name="tag">The tag name.</param>
    /// <param name="attributeName">The attribute name.</param>
    /// <param name="attributeValue">The attribute value.</param>
    /// <param name="content">The tag content.</param>
    public CommentTag(string tag, string attributeName, string attributeValue, string content)
    {
        Tag = tag;
        AttributeName = attributeName;
        AttributeValue = attributeValue;
        Content = content;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        var sb = new StringBuilder();

        string attribute = AttributeName is not null
            ? $" {AttributeName}=\"{AttributeValue}\""
            : string.Empty;

        if (Content.Contains(Environment.NewLine))
        {
            sb.AppendLine($"/// <{Tag}{attribute}>");
            foreach (string line in Content.Split(new[] { Environment.NewLine }, StringSplitOptions.None))
            {
                sb.AppendLine($"/// {line}");
            }
            sb.Append($"/// </{Tag}>");
        }
        else
        {
            sb.Append($"/// <{Tag}{attribute}>{Content}</{Tag}>");
        }

        return sb.ToString();
    }
}
