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
var downloader = new DownloaderProxy(connection);

// Receive the stream from the server.
PipeReader image = await downloader.DownloadImageAsync();

// Create the file, or overwrite the file if it already exists.
using FileStream fs = File.Create("Client/downloads/downloaded_earth.png");

// Copy the image stream to the file stream.
await image.CopyToAsync(fs);

Console.WriteLine("Image downloaded");

await connection.ShutdownAsync();
