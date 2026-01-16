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
