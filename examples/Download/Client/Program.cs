// Copyright (c) ZeroC, Inc.

using DownloadExample;
using IceRpc;
using System.IO.Pipelines;

// Establish the connection to the server
await using var connection = new ClientConnection(new Uri("icerpc://127.0.0.1"));
var downloader = new DownloaderProxy(connection);

// Receive the stream from the server.
PipeReader image = await downloader.DownloadImageAsync();

// The stream returned by AsStream completes the 'image' pipe reader when it's disposed.
using Stream imageStream = image.AsStream();

// Create the file, or overwrite the file if it already exists.
using FileStream fs = File.Create("Client/downloads/downloaded_earth.png");

// Copy the image stream to the file stream.
await imageStream.CopyToAsync(fs);

Console.WriteLine("Image downloaded");
