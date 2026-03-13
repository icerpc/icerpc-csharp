// Copyright (c) ZeroC, Inc.

using ZeroC.CodeBuilder;
using ZeroC.Slice.Symbols;

using Attribute = ZeroC.Slice.Symbols.Attribute;

namespace ZeroC.Slice.Generator;

/// <summary>Extension methods for writing cs::attribute attributes to code builders.</summary>
internal static class CsAttributeExtensions
{
    extension(CodeBlock code)
    {
        /// <summary>Writes all cs::attribute attributes as C# attribute lines.</summary>
        internal void WriteCsAttributes(IList<Attribute> attributes)
        {
            foreach (Attribute attr in attributes.CsAttributes())
            {
                code.WriteLine($"[{attr.Args[0]}]");
            }
        }
    }

    /// <summary>Adds all cs::attribute attributes to the builder.</summary>
    internal static T AddCsAttributes<T>(this T builder, IList<Attribute> attributes) where T : IAttributeBuilder<T>
    {
        foreach (Attribute attr in attributes.CsAttributes())
        {
            builder.AddAttribute(attr.Args[0]);
        }
        return builder;
    }
}
