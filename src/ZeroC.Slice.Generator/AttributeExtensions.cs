// Copyright (c) ZeroC, Inc.

using ZeroC.Slice.Symbols;

using Attribute = ZeroC.Slice.Symbols.Attribute;

namespace ZeroC.Slice.Generator;

/// <summary>C#-specific extension methods for querying Slice attribute lists.</summary>
internal static class AttributeExtensions
{
    extension(IList<Attribute> attributes)
    {
        /// <summary>Returns all cs::attribute attributes from the list.</summary>
        internal IEnumerable<Attribute> CsAttributes() =>
            attributes.Where(a => a.Directive == ZeroC.Slice.Generator.CsAttributes.CsAttribute);
    }
}
