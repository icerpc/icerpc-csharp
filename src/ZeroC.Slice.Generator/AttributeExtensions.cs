// Copyright (c) ZeroC, Inc.

using Attribute = ZeroC.Slice.Symbols.Attribute;

namespace ZeroC.Slice.Generator;

/// <summary>C#-specific extension methods for querying Slice attribute lists.</summary>
internal static class AttributeExtensions
{
    extension(IList<Attribute> attributes)
    {
        /// <summary>Returns all cs::attribute attributes from the list.</summary>
        internal IEnumerable<Attribute> CsAttributes() =>
            attributes.Where(a => a.Directive == Slice.Generator.CsAttributes.CsAttribute);
    }
}
