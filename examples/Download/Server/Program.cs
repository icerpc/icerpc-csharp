// Copyright (c) ZeroC, Inc. All rights reserved.

using DownloadExample;
using IceRpc;

await using var server = new Server(new Downloader());
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
