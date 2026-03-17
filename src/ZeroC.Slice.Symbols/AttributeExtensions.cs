// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Symbols;

/// <summary>Extension methods for querying Slice attribute lists.</summary>
public static class AttributeExtensions
{
    extension(IList<Attribute> attributes)
    {
        /// <summary>Checks if the attribute list contains a specific directive.</summary>
        public bool HasAttribute(string directive) =>
            attributes.Any(a => a.Directive == directive);

        /// <summary>Finds an attribute by directive.</summary>
        public Attribute? FindAttribute(string directive)
        {
            foreach (Attribute attr in attributes)
            {
                if (attr.Directive == directive)
                {
                    return attr;
                }
            }
            return null;
        }
    }
}
