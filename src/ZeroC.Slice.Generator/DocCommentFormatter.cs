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
            CommentText text => CommentTag.XmlEscape(text.Value),
            CommentInlineLink link => FormatInlineLink(link.Target, currentNamespace),
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
            if (link is ResolvedCommentLink resolved)
            {
                yield return new CommentTag("seealso", "cref", FormatEntityCref(resolved.Entity, currentNamespace), "");
            }
        }
    }

    private static string FormatInlineLink(CommentLink link, string currentNamespace) => link switch
    {
        ResolvedCommentLink resolved => $"""<see cref="{FormatEntityCref(resolved.Entity, currentNamespace)}" />""",
        UnresolvedCommentLink unresolved => $"<c>{CommentTag.XmlEscape(unresolved.Identifier)}</c>",
        _ => ""
    };

    /// <summary>Converts a C# type string into a form that can be safely used in cref attributes. Specifically, it
    /// converts generic type parameters into their cref-friendly forms (e.g. IList{T}, IDictionary{T0, T1}).</summary>
    private static string FormatTypeString(string typeString)
    {
        // For generic types we have to convert them to their cref-friendly forms (e.g. IList{T}, IDictionary{T0, T1}).
        var start = typeString.IndexOf('<', StringComparison.Ordinal);
        if (start != -1)
        {
            // Get the type-name without any generics, then append a generic parameter 'T'. There must be at least one.
            string sanitizedTypeString = string.Concat(typeString.AsSpan(0, start), "{T");

            // Add an extra type parameter for each top-level comma we see, skipping over any commas within nested
            // generic types, since they don't correspond to type parameters of the outer type.
            int commaCount = 0;
            int nestingLevel = 0;
            foreach(char c in typeString)
            {
                switch (c)
                {
                    case ',' when nestingLevel == 1: // Only count commas within the first level of '<...>'.
                        sanitizedTypeString = $"{commaCount}{sanitizedTypeString}, T";
                        commaCount += 1;
                        break;
                    case '<':
                        nestingLevel += 1;
                        break;
                    case '>':
                        nestingLevel -= 1;
                        break;
                }
            }
            if (commaCount > 0)
            {
                // If there were multiple generic parameters, we generate T0, T1, ... instead of T. But we always have
                // a trailing 'T' with no number. Write the final number if there were multiple parameters to get 'TN'.
                sanitizedTypeString += commaCount;
            }
            typeString = sanitizedTypeString + "}";
        }

        return typeString;
    }

    private static string FormatEntityCref(Entity entity, string currentNamespace)
    {
        if (entity is TypeAlias alias)
        {
            // Type aliases don't generate a C# type; link to the underlying C# type instead.
            string mapped = alias.UnderlyingType.Attributes.FindAttribute(CSAttributes.CSType)?.Args[0]
                ?? alias.UnderlyingType.Type.ToTypeString(currentNamespace);
            return FormatTypeString(mapped);
        }

        if (entity is CustomType custom)
        {
            // Custom types don't generate a C# type; link to their mapped C# type instead.
            if (custom.Attributes.FindAttribute(CSAttributes.CSType)?.Args[0] is string mapped)
            {
                return FormatTypeString(mapped);
            }
        }

        string name = entity switch
        {
            Interface => $"I{entity.Name}",
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
