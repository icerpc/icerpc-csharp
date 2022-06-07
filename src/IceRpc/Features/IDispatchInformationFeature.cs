// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Features;

/// <summary>A feature that provides information about the current dispatch.</summary>
public interface IDispatchInformationFeature
{
    /// <summary>Gets the connection that received the request.</summary>
    IConnection Connection { get; }

    /// <summary>Gets the fragment of the target service.</summary>
    /// <value>The fragment of the target service. it is always the empty string with the icerpc protocol.</value>
    string Fragment { get; }

    /// <summary>Gets a value indicating whether this request is oneway or two-way.</summary>
    /// <value><c>true</c> for oneway requests, <c>false</c> otherwise.</value>
    bool IsOneway { get; }

    /// <summary>Gets the name of the operation to call on the target service.</summary>
    /// <value>The name of the operation.</value>
    string Operation { get; }

    /// <summary>Gets the path of the target service.</summary>
    /// <value>The path of the target service.</value>
    string Path { get; }
}
