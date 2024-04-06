// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Transports.Quic;
using QuicServer;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

const string serverCert = "../../../../certs/server_cert.pem";
const string serverKey = "../../../../certs/server_key.pem";

// The X509 certificate used by the server.
using var serverCertificate = X509Certificate2.CreateFromPemFile(serverCert, serverKey);

// Create a collection with the server certificate and any intermediate certificates. This is used by
// ServerCertificateContext to provide the certificate chain to the peer.
var intermediates = new X509Certificate2Collection();
intermediates.ImportFromPemFile(serverCert);

// Create a server that uses the test server certificate, and the QUIC multiplexed transport.
await using var server = new Server(
    new Chatbot(),
    new SslServerAuthenticationOptions
    {
        ServerCertificateContext = SslStreamCertificateContext.Create(serverCertificate, intermediates)
    },
    multiplexedServerTransport: new QuicServerTransport());

server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
