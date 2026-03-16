// Copyright (c) ZeroC, Inc.

using ZeroC.CodeBuilder;

using Attribute = ZeroC.Slice.Symbols.Attribute;

namespace ZeroC.Slice.Generator;

/// <summary>Extension methods for code builders.</summary>
internal static class BuilderExtensions
{
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
