// Copyright (c) ZeroC, Inc.

namespace IceRpc.Features;

/// <summary>Provides information about the current dispatch.</summary>
public interface IDispatchInformationFeature
{
    /// <summary>Gets the connection context.</summary>
    IConnectionContext ConnectionContext { get; }

    /// <summary>Gets the fragment of the target service.</summary>
    /// <value>The fragment of the target service. It is always the empty string with the icerpc protocol.</value>
    string Fragment { get; }

    /// <summary>Gets a value indicating whether this request is one-way or two-way.</summary>
    /// <value><see langword="true" /> for one-way requests; otherwise, <see langword="false" />.</value>
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
