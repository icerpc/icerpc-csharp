// Copyright (c) ZeroC, Inc.

using ZeroC.CodeBuilder;
using ZeroC.Slice.Symbols;

namespace ZeroC.Slice.Generator;

/// <summary>Formats Slice doc comments into C# XML doc comment strings.</summary>
internal static class DocCommentFormatter
{
    /// <summary>Formats the overview of a <see cref="Comment"/> as XML doc comment summary text.
    /// Returns null if the comment has no overview.</summary>
    internal static string? FormatSummary(Comment? comment, string currentNamespace)
    {
        if (comment?.Overview is not { Count: > 0 } overview)
        {
            return null;
        }

        return string.Concat(overview.Select(c => c switch
        {
            CommentMessageComponent.Text t => XmlEscape(t.Value),
            CommentMessageComponent.Link l => FormatInlineLink(l.Target, currentNamespace),
            _ => ""
        })).TrimEnd();
    }

    /// <summary>Formats the @see tags of a <see cref="Comment"/> as a sequence of
    /// <c>&lt;seealso cref="..." /&gt;</c> comment tags. Unresolved links are skipped.</summary>
    internal static IEnumerable<CommentTag> FormatSeeAlso(Comment? comment, string currentNamespace)
    {
        if (comment?.SeeTags is not { Count: > 0 } seeTags)
        {
            yield break;
        }

        foreach (CommentLink link in seeTags)
        {
            if (link is CommentLink.Resolved r)
            {
                yield return new CommentTag("seealso", "cref", FormatEntityCref(r.Entity, currentNamespace), "");
            }
        }
    }

    private static string FormatInlineLink(CommentLink link, string currentNamespace) => link switch
    {
        CommentLink.Resolved r => $"""<see cref="{FormatEntityCref(r.Entity, currentNamespace)}" />""",
        CommentLink.Unresolved u => $"<c>{XmlEscape(u.Identifier)}</c>",
        _ => ""
    };

    private static string FormatEntityCref(Entity entity, string currentNamespace)
    {
        string name = entity switch
        {
            Interface => $"I{entity.Name}",
            CustomType ct => ct.Attributes.FindAttribute(CSAttributes.CSType)?.Args[0] ?? entity.Name,
            Operation op when op.Parent is Interface => $"I{op.Parent.Name}.{entity.Name}Async",
            Field f when f.Parent is not null => $"{f.Parent.Name}.{entity.Name}",
            EnumWithFields.Enumerator e when e.Parent is not null => $"{e.Parent.Name}.{entity.Name}",
            // EnumWithUnderlying<T>.Enumerator
            Entity e when e.Parent is EnumWithUnderlying => $"{e.Parent.Name}.{entity.Name}",
            EnumWithUnderlying => entity.Name,
            EnumWithFields => entity.Name,
            Struct => entity.Name,
            _ => entity.Name
        };

        string entityNamespace = entity.Namespace;
        if (entityNamespace == currentNamespace)
        {
            return name;
        }

        return $"global::{entityNamespace}.{name}";
    }

    private static string XmlEscape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
