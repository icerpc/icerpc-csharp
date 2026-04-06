// Copyright (c) ZeroC, Inc.

using ZeroC.Slice.Symbols;

using Attribute = ZeroC.Slice.Symbols.Attribute;

namespace ZeroC.Slice.Generator;

/// <summary>Extension methods for <see cref="Entity"/> naming helpers.</summary>
internal static class EntityExtensions
{
    extension(Entity entity)
    {
        /// <summary>Gets the access modifier for this entity ("public" or "internal").</summary>
        internal string AccessModifier =>
            entity.Attributes.HasAttribute(CSAttributes.CSPublic) ? "public" : "internal";

        /// <summary>Gets the name of the generated SliceDecoder extensions class for this entity.</summary>
        internal string DecoderExtensionsClass => $"{entity.Name}SliceDecoderExtensions";

        /// <summary>Gets the name of the generated SliceEncoder extensions class for this entity.</summary>
        internal string EncoderExtensionsClass => $"{entity.Name}SliceEncoderExtensions";

        /// <summary>Gets the C# identifier (checks cs::identifier attribute, applies PascalCase).</summary>
        internal string Name
        {
            get
            {
                Attribute? csIdentifier = entity.Attributes.FindAttribute(CSAttributes.CSIdentifier);
                return csIdentifier is { } attr ? attr.Args[0] : entity.Identifier.ToPascalCase();
            }
        }

        /// <summary>Gets the C# namespace for this entity (respects cs::namespace attribute on the module).</summary>
        internal string Namespace => entity.Module.Namespace;

        /// <summary>Gets the camelCase parameter name (checks cs::identifier attribute).</summary>
        internal string ParameterName
        {
            get
            {
                Attribute? csIdentifier = entity.Attributes.FindAttribute(CSAttributes.CSIdentifier);
                return csIdentifier is { } attr ? attr.Args[0] : entity.Identifier.ToCamelCase();
            }
        }

        /// <summary>Gets the fully-qualified name of the encoder or decoder extensions class, using
        /// <c>global::</c> prefix when the entity is in a different namespace.</summary>
        internal string ScopedExtensionsClass(string currentNamespace, bool decoder)
        {
            string className = decoder ? entity.DecoderExtensionsClass : entity.EncoderExtensionsClass;
            return currentNamespace == entity.Namespace
                ? className
                : $"global::{entity.Namespace}.{className}";
        }

        /// <summary>Gets a value indicating whether this entity type uses a generated extensions class
        /// for encoding/decoding (as opposed to instance Encode/Decode methods).</summary>
        internal bool UsesExtensionsClass => entity is BasicEnum or VariantEnum or CustomType;
    }
}
