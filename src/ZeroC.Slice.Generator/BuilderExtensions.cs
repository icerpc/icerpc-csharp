// Copyright (c) ZeroC, Inc.

using ZeroC.CodeBuilder;
using ZeroC.Slice.Symbols;

using Attribute = ZeroC.Slice.Symbols.Attribute;

namespace ZeroC.Slice.Generator;

/// <summary>Extension methods for code builders.</summary>
internal static class BuilderExtensions
{
    /// <summary>Adds the <c>&lt;summary&gt;</c> tag from a <see cref="Comment"/> to a builder.</summary>
    internal static T AddDocCommentSummary<T>(this T builder, Comment? comment, string currentNamespace)
        where T : ICommentBuilder<T>
    {
        if (DocCommentFormatter.FormatOverview(comment, currentNamespace) is string summary)
        {
            builder.AddComment("summary", summary);
        }
        return builder;
    }

    /// <summary>Adds the <c>&lt;seealso&gt;</c> tags from a <see cref="Comment"/> to a builder.</summary>
    internal static T AddDocCommentSeeAlso<T>(this T builder, Comment? comment, string currentNamespace)
        where T : ICommentBuilder<T>
    {
        builder.AddComments(DocCommentFormatter.FormatSeeAlsoTags(comment, currentNamespace));
        return builder;
    }

    /// <summary>Adds all <c>cs::attribute</c> attributes to the builder.</summary>
    internal static T AddCSAttributes<T>(this T builder, IList<Attribute> attributes) where T : IAttributeBuilder<T>
    {
        foreach (Attribute attr in attributes.CSAttributes())
        {
            builder.AddAttribute(attr.Args[0]);
        }
        return builder;
    }
}
