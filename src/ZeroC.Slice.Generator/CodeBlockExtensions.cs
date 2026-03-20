// Copyright (c) ZeroC, Inc.

using ZeroC.CodeBuilder;

using Attribute = ZeroC.Slice.Symbols.Attribute;

namespace ZeroC.Slice.Generator;

/// <summary>Extension methods for <see cref="CodeBlock"/>.</summary>
internal static class CodeBlockExtensions
{
    extension(CodeBlock code)
    {
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
