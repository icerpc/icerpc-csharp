// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Transports;

/// <summary>The default implementation of <see cref="INetworkConnectionInformationFeature"/>.</summary>
public class NetworkConnectionInformationFeature : INetworkConnectionInformationFeature
{
    /// <inheritdoc/>
    public EndPoint LocalEndPoint { get; }

    /// <inheritdoc/>
    public EndPoint RemoteEndPoint { get; }

    /// <inheritdoc/>
    public X509Certificate? RemoteCertificate { get; }

    /// <summary>Constructs a network connection information feature.</summary>
    /// <param name="localEndPoint">The local endpoint.</param>
    /// <param name="remoteEndPoint">The remote endpoint.</param>
    /// <param name="remoteCertificate">The optional remote certificate.</param>
    public NetworkConnectionInformationFeature(
        EndPoint localEndPoint,
        EndPoint remoteEndPoint,
        X509Certificate? remoteCertificate)
    {
        LocalEndPoint = localEndPoint;
        RemoteEndPoint = remoteEndPoint;
        RemoteCertificate = remoteCertificate;
    }
}
