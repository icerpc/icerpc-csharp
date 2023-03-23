// Copyright (c) ZeroC, Inc.

namespace IceRpc.Features;

/// <summary>A feature that provides information about the current dispatch.</summary>
public interface IDispatchInformationFeature
{
    /// <summary>Gets the connection context.</summary>
    IConnectionContext ConnectionContext { get; }

    /// <summary>Gets the fragment of the target service.</summary>
    /// <value>The fragment of the target service. It is always the empty string with the icerpc protocol.</value>
    string Fragment { get; }

    /// <summary>Gets a value indicating whether this request is oneway or two-way.</summary>
    /// <value><see langword="true" /> for oneway requests, <see langword="false" /> otherwise.</value>
    bool IsOneway { get; }

    /// <summary>Gets the name of the operation to call on the target service.</summary>
    /// <value>The name of the operation.</value>
    string Operation { get; }

    /// <summary>Gets the path of the target service.</summary>
    /// <value>The path of the target service.</value>
    string Path { get; }

    /// <summary>Gets the protocol of the connection that received this request.</summary>
    /// <value>The <see cref="IncomingFrame.Protocol" /> for the current dispatch.</value>
    Protocol Protocol { get; }
}
