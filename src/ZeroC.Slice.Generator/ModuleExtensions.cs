// Copyright (c) ZeroC, Inc.

using ZeroC.Slice.Symbols;

namespace ZeroC.Slice.Generator;

/// <summary>Extension methods for <see cref="Module"/>.</summary>
internal static class ModuleExtensions
{
    extension(Module module)
    {
        /// <summary>Gets the mapped C# namespace for this module (respects cs::namespace attribute).</summary>
        internal string Namespace
        {
            get
            {
                if (module.Attributes.FindAttribute(CSAttributes.CSNamespace) is Symbols.Attribute attr)
                {
                    return attr.Args[0];
                }
                string[] segments = module.Identifier.Split("::");
                return string.Join(".", segments.Select(s => s.ToPascalCase()));
            }
        }
    }
}
