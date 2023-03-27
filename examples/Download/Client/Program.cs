// Copyright (c) ZeroC, Inc.

using DownloadExample;
using IceRpc;
using System.IO.Pipelines;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));
var downloader = new DownloaderProxy(connection);

// Receive the stream from the server.
PipeReader image = await downloader.DownloadImageAsync();

// Create the file, or overwrite the file if it already exists.
using FileStream fs = File.Create("Client/downloads/downloaded_earth.png");

// Copy the image stream to the file stream.
await image.CopyToAsync(fs);

Console.WriteLine("Image downloaded");

await connection.ShutdownAsync();
