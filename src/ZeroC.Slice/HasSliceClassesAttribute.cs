// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice;

/// <summary>Represents an assembly attribute for marking assemblies that contain Slice classes or exceptions.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class HasSliceClassesAttribute : Attribute
{
}
