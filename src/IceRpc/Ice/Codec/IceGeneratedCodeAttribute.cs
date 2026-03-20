// Copyright (c) ZeroC, Inc.

namespace IceRpc.Ice.Codec;

/// <summary>Represents an assembly attribute for assemblies that contain Ice generated code.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class IceGeneratedCodeAttribute : Attribute
{
    /// <summary>Gets the name of the file that contains the Ice definitions.</summary>
    public string SourceFileName { get; }

    /// <summary>Constructs a new instance of <see cref="IceGeneratedCodeAttribute" />.</summary>
    /// <param name="sourceFileName">The name of the source file.</param>
    public IceGeneratedCodeAttribute(string sourceFileName) => SourceFileName = sourceFileName;
}
