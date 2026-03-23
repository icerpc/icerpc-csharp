// Copyright (c) ZeroC, Inc.

using ZeroC.CodeBuilder;
using ZeroC.Slice.Symbols;

using Attribute = ZeroC.Slice.Symbols.Attribute;

namespace ZeroC.Slice.Generator;

/// <summary>Extension methods for <see cref="CodeBlock"/>.</summary>
internal static class CodeBlockExtensions
{
    extension(CodeBlock code)
    {
        /// <summary>Writes a <c>&lt;summary&gt;</c> XML doc comment line if the comment has an overview.</summary>
        internal void WriteDocCommentSummary(Comment? comment, string currentNamespace)
        {
            if (DocCommentFormatter.FormatOverview(comment, currentNamespace) is string summary)
            {
                code.WriteLine(new CommentTag("summary", summary).ToString());
            }
        }

        /// <summary>Writes all <c>cs::attribute</c> attributes as C# attribute lines.</summary>
        internal void WriteCSAttributes(IList<Attribute> attributes)
        {
            foreach (Attribute attr in attributes.CSAttributes())
            {
                code.WriteLine($"[{attr.Args[0]}]");
            }
        }
    }
}
