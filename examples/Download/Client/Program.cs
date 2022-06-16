// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using System.IO.Pipelines;

// Establish the connection to the server
await using var connection = new ClientConnection("icerpc://127.0.0.1");
IDownloaderPrx downloader = DownloaderPrx.FromConnection(connection);

// Receive the stream from the server.
PipeReader image = await downloader.DownloadImageAsync();

// AsStream has a parameter `leaveOpen` which is set to `false` by default. When the stream is disposed, if
// leaveOpen` is `false` then the PipeReader used to create the stream is completed.
using Stream imageStream = image.AsStream();

// Create the file, or overwrite if the file exists.
using FileStream fs = File.Create("Client/downloads/downloaded_earth.png");

// Copy the image to the file stream.
await imageStream.CopyToAsync(fs);

Console.WriteLine("Image downloaded");
