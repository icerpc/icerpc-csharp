// Copyright (c) ZeroC, Inc.

using Attribute = ZeroC.Slice.Symbols.Attribute;

namespace ZeroC.Slice.Generator;

/// <summary>C#-specific extension methods for querying Slice attribute lists.</summary>
internal static class AttributeExtensions
{
    extension(IList<Attribute> attributes)
    {
        /// <summary>Returns all cs::attribute attributes from the list.</summary>
        internal IEnumerable<Attribute> CSAttributes() =>
            attributes.Where(a => a.Directive == Generator.CSAttributes.CSAttribute);

        /// <summary>Gets whether the entity has the <c>deprecated</c> attribute.</summary>
        internal bool IsDeprecated => attributes.Any(a => a.Directive == "deprecated");

        /// <summary>Gets the deprecation message, or null if not deprecated or no message provided.</summary>
        internal string? DeprecatedMessage
        {
            get
            {
                Attribute deprecated = attributes.FirstOrDefault(a => a.Directive == "deprecated");
                return deprecated != default && deprecated.Args.Count > 0 ? deprecated.Args[0] : null;
            }
        }
    }
}
