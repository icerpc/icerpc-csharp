// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var issuerKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("A dummy secret key for JWT authentication example"));
var credentials = new SigningCredentials(issuerKey, SecurityAlgorithms.HmacSha256);

// Add the request context middleware to the dispatch pipeline.
Router router = new Router().UseJwt();
router.Map<IAuth>(new Auth(credentials));

await using var server = new Server(router, "icerpc://127.0.0.1:10001");

// Shuts down the server on Ctrl+C
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    _ = server.ShutdownAsync();
};

server.Listen();
await server.ShutdownComplete;
