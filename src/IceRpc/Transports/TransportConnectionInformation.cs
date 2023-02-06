// Copyright (c) ZeroC, Inc.

using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Transports;

/// <summary>The transport connection information returned on connection establishment.</summary>
public sealed record class TransportConnectionInformation
{
    /// <summary>Gets the network address of the local end of the connection.</summary>
    public EndPoint LocalNetworkAddress { get; }

    /// <summary>Gets the network address of the remote end of the connection.</summary>
    public EndPoint RemoteNetworkAddress { get; }

    /// <summary>Gets the certificate of the peer, if provided.</summary>
    public X509Certificate? RemoteCertificate { get; }

    /// <summary>Constructs a new instance of <see cref="TransportConnectionInformation" />.</summary>
    /// <param name="localNetworkAddress">The local network address.</param>
    /// <param name="remoteNetworkAddress">The remote network address.</param>
    /// <param name="remoteCertificate">The remote certificate.</param>
    public TransportConnectionInformation(
        EndPoint localNetworkAddress,
        EndPoint remoteNetworkAddress,
        X509Certificate? remoteCertificate)
    {
        LocalNetworkAddress = localNetworkAddress;
        RemoteNetworkAddress = remoteNetworkAddress;
        RemoteCertificate = remoteCertificate;
    }
}
