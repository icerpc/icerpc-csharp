// Copyright (c) ZeroC, Inc.

using IceRpc;
using Repository;
using System.IO.Pipelines;
using System.Security.Cryptography.X509Certificates;

// Load the test root CA certificate in order to connect to the server that uses a test server certificate.
using X509Certificate2 rootCA = X509CertificateLoader.LoadCertificateFromFile("../../../../certs/cacert.der");

// Create a secure connection to the server using the default transport (QUIC).
await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA));
var uploader = new UploaderProxy(connection);

Console.WriteLine("Uploading image of the Earth...");

// Create a pipe reader that wraps the image we want to upload. The pipe reader takes ownership of the file stream and
// disposes it once it is completed by the IceRPC runtime.
var reader = PipeReader.Create(new FileStream("Client/images/Earth.png", FileMode.Open));

// This call waits until the uploader service returns. The uploader service returns after reading the full image.
await uploader.UploadImageAsync(reader);

Console.WriteLine("Image of the Earth uploaded");

await connection.ShutdownAsync();
