// Copyright (c) ZeroC, Inc.

namespace ZeroC.CodeBuilder;

/// <summary>Represents a builder that can have C# attributes added to it.</summary>
/// <typeparam name="T">The concrete builder type for fluent chaining.</typeparam>
public interface IAttributeBuilder<T>
    where T : IAttributeBuilder<T>
{
    /// <summary>Adds a C# attribute to the builder.</summary>
    /// <param name="attribute">The attribute text (without brackets).</param>
    /// <returns>The builder instance for method chaining.</returns>
    T AddAttribute(string attribute);
}

/// <summary>Extension methods for <see cref="IAttributeBuilder{T}"/>.</summary>
public static class AttributeBuilderExtensions
{
    /// <summary>Adds the EditorBrowsable(Never) attribute.</summary>
    /// <typeparam name="T">The concrete builder type for fluent chaining.</typeparam>
    /// <param name="builder">The builder instance.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public static T AddNeverEditorBrowsableAttribute<T>(this T builder) where T : IAttributeBuilder<T> =>
        builder.AddAttribute(
            "global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)");

    /// <summary>Adds the Obsolete attribute when <paramref name="condition"/> is true.</summary>
    /// <typeparam name="T">The concrete builder type for fluent chaining.</typeparam>
    /// <param name="builder">The builder instance.</param>
    /// <param name="condition">Whether to add the Obsolete attribute.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public static T AddObsoleteAttribute<T>(this T builder, bool condition = true) where T : IAttributeBuilder<T> =>
        condition ? builder.AddAttribute("global::System.Obsolete") : builder;
}
