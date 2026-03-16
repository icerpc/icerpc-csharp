// Copyright (c) ZeroC, Inc.

using ZeroC.Slice.Symbols;

using Attribute = ZeroC.Slice.Symbols.Attribute;

namespace ZeroC.Slice.Generator;

/// <summary>Extension methods for <see cref="Entity"/> naming helpers.</summary>
internal static class EntityExtensions
{
    extension(Entity entity)
    {
        /// <summary>Gets the C# namespace for this entity (respects cs::namespace attribute on the module).</summary>
        internal string Namespace
        {
            get
            {
                Module module = entity.Module;
                if (module.Attributes.FindAttribute(CsAttributes.CsNamespace) is { } attr)
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
                Attribute? csIdentifier = entity.Attributes.FindAttribute(CsAttributes.CsIdentifier);
                return csIdentifier is { } attr ? attr.Args[0] : entity.Identifier.ToPascalCase();
            }
        }

        /// <summary>Gets the camelCase parameter name (checks cs::identifier attribute).</summary>
        internal string ParameterName
        {
            get
            {
                Attribute? csIdentifier = entity.Attributes.FindAttribute(CsAttributes.CsIdentifier);
                return csIdentifier is { } attr ? attr.Args[0] : entity.Identifier.ToCamelCase();
            }
        }

        /// <summary>Gets the access modifier for this entity ("public" or "internal").</summary>
        internal string AccessModifier =>
            entity.Attributes.HasAttribute(CsAttributes.CsInternal) ? "internal" : "public";
    }
}
