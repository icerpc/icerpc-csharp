// Copyright (c) ZeroC, Inc.

using IceRpc;
using SecureServer;
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

// Create the authentication options using the test server certificate.
var serverAuthenticationOptions = new SslServerAuthenticationOptions()
{
    ServerCertificateContext = SslStreamCertificateContext.Create(serverCertificate, intermediates)
};

await using var server = new Server(new Chatbot(), serverAuthenticationOptions);
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
