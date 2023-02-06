// Copyright (c) ZeroC, Inc.

namespace IceRpc.RequestContext;

/// <summary>A feature that represents an dictionary{string, string} that can be transmitted with both ice and icerpc
/// requests. This feature is encoded and decoded by the IceRPC core.</summary>
public interface IRequestContextFeature
{
    /// <summary>Gets or sets the value of this context feature.</summary>
    IDictionary<string, string> Value { get; set; }
}
