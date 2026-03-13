// Copyright (c) ZeroC, Inc.

using ZeroC.Slice.Symbols;
using Attribute = ZeroC.Slice.Symbols.Attribute;

namespace ZeroC.Slice.Generator;

/// <summary>Extension methods for <see cref="EntityInfo"/> naming helpers.</summary>
internal static class EntityInfoExtensions
{
    extension(EntityInfo entity)
    {
        /// <summary>Gets the C# namespace for this entity (respects cs::namespace attribute on the module).</summary>
        internal string Namespace
        {
            get
            {
                Module module = entity.Module;
                if (module.Attributes.FindAttribute(Attribute.CsNamespace) is { } attr)
                {
                    return attr.Args[0];
                }
                string[] segments = module.Identifier.Split("::");
                return string.Join(".", segments.Select(s => s.ToPascalCase()));
            }
        }

        /// <summary>Gets the C# identifier (checks cs::identifier attribute, applies PascalCase).</summary>
        internal string Name
        {
            get
            {
                Attribute? csIdentifier = entity.Attributes.FindAttribute(Attribute.CsIdentifier);
                return csIdentifier is { } attr ? attr.Args[0] : entity.Identifier.ToPascalCase();
            }
        }

        /// <summary>Gets the camelCase parameter name (checks cs::identifier attribute).</summary>
        internal string ParameterName
        {
            get
            {
                Attribute? csIdentifier = entity.Attributes.FindAttribute(Attribute.CsIdentifier);
                return csIdentifier is { } attr ? attr.Args[0] : entity.Identifier.ToCamelCase();
            }
        }
    }
}
