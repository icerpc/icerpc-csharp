// Copyright (c) ZeroC, Inc.

using IceRpc;
using Spider;

using var httpClient = new HttpClient();
var router = new Router();

router.Map("/v1/search", new WebService(httpClient, new Uri("https://geocoding-api.open-meteo.com/v1/search")));
router.Map("/v1/forecast", new WebService(httpClient, new Uri("https://api.open-meteo.com/v1/forecast")));

// Create a server that dispatches requests to router.
await using var server = new Server(router);

server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
