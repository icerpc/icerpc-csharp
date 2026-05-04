// Copyright (c) ZeroC, Inc.

using ZeroC.CodeBuilder;
using ZeroC.Slice.Symbols;

namespace ZeroC.Slice.Generator;

/// <summary>Formats Slice doc comments into C# XML doc comment strings.</summary>
internal static class DocCommentFormatter
{
    /// <summary>Formats the overview of a <see cref="Comment"/> as XML doc comment text. Returns null if the
    /// comment has no overview.</summary>
    internal static string? FormatOverview(Comment? comment, string currentNamespace)
    {
        if (comment?.Overview is not { Count: > 0 } overview)
        {
            return null;
        }

        return string.Concat(overview.Select(c => c switch
        {
            CommentText t => CommentTag.XmlEscape(t.Value),
            CommentInlineLink l => FormatInlineLink(l.Target, currentNamespace),
            _ => ""
        })).TrimEnd();
    }

    /// <summary>Formats the @see tags of a <see cref="Comment"/> as a sequence of
    /// <c>&lt;seealso cref="..." /&gt;</c> comment tags. Unresolved links are skipped.</summary>
    internal static IEnumerable<CommentTag> FormatSeeAlsoTags(Comment? comment, string currentNamespace)
    {
        if (comment?.SeeTags is not { Count: > 0 } seeTags)
        {
            yield break;
        }

        foreach (CommentLink link in seeTags)
        {
            if (link is not ResolvedCommentLink r)
            {
                continue;
            }

            if (r.Entity is TypeAlias alias)
            {
                // Type aliases don't generate a C# type; map to the underlying C# type. We can't reliably
                // produce a seealso for a generic mapped type without C# parsing, so skip those.
                string mapped = alias.UnderlyingType.Type.ToTypeString(currentNamespace);
                if (!mapped.Contains('<', StringComparison.Ordinal))
                {
                    yield return new CommentTag("seealso", "cref", mapped, "");
                }
            }
            else
            {
                yield return new CommentTag("seealso", "cref", FormatEntityCref(r.Entity, currentNamespace), "");
            }
        }
    }

    private static string FormatInlineLink(CommentLink link, string currentNamespace) => link switch
    {
        ResolvedCommentLink { Entity: TypeAlias alias } => FormatTypeAliasInline(alias, currentNamespace),
        ResolvedCommentLink r => $"""<see cref="{FormatEntityCref(r.Entity, currentNamespace)}" />""",
        UnresolvedCommentLink u => $"<c>{CommentTag.XmlEscape(u.Identifier)}</c>",
        _ => ""
    };

    private static string FormatTypeAliasInline(TypeAlias alias, string currentNamespace)
    {
        // Type aliases don't generate a C# type; link to the underlying C# type instead.
        string mapped = alias.UnderlyingType.Type.ToTypeString(currentNamespace);

        // For generic types we have to convert them to their cref-friendly forms (e.g. IList{T0}, IDictionary{T0, T1}).
        var start = mapped.IndexOf('<', StringComparison.Ordinal);
        if (start != -1)
        {
            // Get the type-name without any generics, then append a generic parameter 'T0'. There must be at least one.
            string sanitizedTypeString = string.Concat(mapped.AsSpan(0, start), "{T0");

            // Add an extra type parameter for each top-level comma we see, skipping over any commas within nested
            // generic types, since they don't correspond to type parameters of the outer type.
            int commaCount = 0;
            int nestingLevel = 0;
            foreach(char c in mapped)
            {
                switch (c)
                {
                    case ',' when nestingLevel == 1: // Only count commas within the first level of '<...>'.
                        commaCount += 1;
                        sanitizedTypeString += ", T" + commaCount;
                        break;
                    case '<':
                        nestingLevel += 1;
                        break;
                    case '>':
                        nestingLevel -= 1;
                        break;
                }
            }
            mapped = sanitizedTypeString + "}";
        }

        return $"""<see cref="{mapped}" />""";
    }

    private static string FormatEntityCref(Entity entity, string currentNamespace)
    {
        string name = entity switch
        {
            Interface => $"I{entity.Name}",
            CustomType ct => ct.Attributes.FindAttribute(CSAttributes.CSType)?.Args[0] ?? entity.Name,
            Operation op when op.Parent is Interface => $"I{op.Parent.Name}.{entity.Name}Async",
            Field f when f.Parent is not null => $"{f.Parent.Name}.{entity.Name}",
            VariantEnum.Variant e when e.Parent is not null => $"{e.Parent.Name}.{entity.Name}",
            // BasicEnum<T>.Enumerator
            Entity e when e.Parent is BasicEnum => $"{e.Parent.Name}.{entity.Name}",
            BasicEnum => entity.Name,
            VariantEnum => entity.Name,
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

}
