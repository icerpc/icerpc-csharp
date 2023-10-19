// Copyright (c) ZeroC, Inc.

namespace IceRpc;

/// <summary>Assigns a default service path to an interface.</summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class DefaultServicePathAttribute : Attribute
{
    /// <summary>Gets the default service path.</summary>
    /// <value>The default service path.</value>
    public string Value { get; }

    /// <summary>Constructs a default service path attribute.</summary>
    /// <param name="value">The default service path.</param>>
    public DefaultServicePathAttribute(string value) => Value = value;
}
