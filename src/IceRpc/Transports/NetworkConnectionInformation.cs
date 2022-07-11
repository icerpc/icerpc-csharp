// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Transports;

/// <summary>The network connection information returned on <see cref="INetworkConnection"/> connection
/// establishment.</summary>
public readonly record struct NetworkConnectionInformation
{
    /// <summary>Gets the network address of the local end of the connection.</summary>
    public EndPoint? LocalNetworkAddress { get; }

    /// <summary>Gets the network address of the remote end of the connection.</summary>
    public EndPoint? RemoteNetworkAddress { get; }

    /// <summary>Gets the certificate of the remote peer, if provided.</summary>
    public X509Certificate? RemoteCertificate { get; }

    /// <summary>Constructs a new instance of <see cref="NetworkConnectionInformation"/>.</summary>
    /// <param name="localNetworkAddress">The local network address.</param>
    /// <param name="remoteNetworkAddress">The remote network address.</param>
    /// <param name="remoteCertificate">The remote certificate.</param>
    public NetworkConnectionInformation(
        EndPoint localNetworkAddress,
        EndPoint remoteNetworkAddress,
        X509Certificate? remoteCertificate)
    {
        LocalNetworkAddress = localNetworkAddress;
        RemoteNetworkAddress = remoteNetworkAddress;
        RemoteCertificate = remoteCertificate;
    }
}
