// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Transports;

/// <summary>A feature that provides information about the network connection used to send or receive a request.
/// </summary>
public interface INetworkConnectionInformationFeature
{
    /// <summary>Gets the local endpoint.</summary>
    public EndPoint LocalEndPoint { get; }

    /// <summary>Gets the remote endpoint.</summary>
    public EndPoint RemoteEndPoint { get; }

    /// <summary>Gets the peer's remote certificate if TLS is used for the connection, <c>null</c> otherwise.</summary>
    public X509Certificate? RemoteCertificate { get; }
}
