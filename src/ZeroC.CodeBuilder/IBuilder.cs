// Copyright (c) ZeroC, Inc.

namespace ZeroC.CodeBuilder;

/// <summary>Represents a builder that can produce a <see cref="CodeBlock"/>.</summary>
public interface IBuilder
{
    /// <summary>Builds and returns the code block.</summary>
    /// <returns>The generated <see cref="CodeBlock"/>.</returns>
    CodeBlock Build();
}
