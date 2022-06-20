// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var issuerKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("A dummy secret key for JWT authentication example"));

// Add the JWT middleware to the dispatch pipeline.
Router router = new Router().UseJwt(
    new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        ValidateAudience = true,
        ValidateIssuer = true,
        ValidateLifetime = true,
        RequireSignedTokens = true,
        ValidAudience = "Jwt Example",
        ValidIssuer = "icerpc://127.0.0.1",
        IssuerSigningKey = issuerKey,
        ClockSkew = TimeSpan.Zero
    });
router.Map<IHello>(new Hello());

await using var server = new Server(router, "icerpc://127.0.0.1:10002");

// Shuts down the server on Ctrl+C
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    _ = server.ShutdownAsync();
};

server.Listen();
await server.ShutdownComplete;
