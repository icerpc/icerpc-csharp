// Copyright (c) ZeroC, Inc.

using AuthorizationExample;
using IceRpc;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

var authenticatorProxy = new AuthenticatorProxy(connection, new Uri("icerpc:/authenticator"));

// Authenticate the alice user and get its identity token.
ReadOnlyMemory<byte> aliceToken = await authenticatorProxy.AuthenticateAsync("alice", "password");

// A greeter proxy that doesn't use any identity token.
var unauthenticatedGreeterProxy = new GreeterProxy(connection, new Uri("icerpc:/greeter"));

// The Greet invocation on the unauthenticated greeter proxy prints a generic message.
Console.WriteLine(await unauthenticatedGreeterProxy.GreetAsync());

// Create a greeter proxy that uses a pipe line to insert the "alice" token into a request field.
Pipeline alicePipeline = new Pipeline().UseAuthentication(aliceToken).Into(connection);
var aliceGreeterProxy = new GreeterProxy(alicePipeline, new Uri("icerpc:/greeter"));

// The Greet invocation on the authenticated "alice" greeter proxy prints a custom message for "alice".
Console.WriteLine(await aliceGreeterProxy.GreetAsync());

// A greeter admin proxy that uses the "alice" pipeline is not authorized to change the greeting message because "alice"
// doesn't have administrative privileges.
var greeterAdminProxy = new GreeterAdminProxy(alicePipeline, new Uri("icerpc:/greeterAdmin"));
try
{
    await greeterAdminProxy.ChangeGreetingAsync("Bonjour");
}
catch (DispatchException exception) when (exception.StatusCode == StatusCode.Unauthorized)
{
    Console.WriteLine("The 'alice' user is not authorized to change the greeting message.");
}

// Authenticate the "admin" user and get its identity token.
ReadOnlyMemory<byte> adminToken = await authenticatorProxy.AuthenticateAsync("admin", "admin-password");

// Create a greeter admin proxy that uses a pipe line to insert the "admin" token into a request field.
Pipeline adminPipeline = new Pipeline().UseAuthentication(adminToken).Into(connection);
greeterAdminProxy = new GreeterAdminProxy(adminPipeline, new Uri("icerpc:/greeterAdmin"));

// Changing the greeting message should succeed this time because the "admin" user has administrative privilege.
await greeterAdminProxy.ChangeGreetingAsync("Bonjour");

// The Greet invocation should print a greeting with the updated greeting message.
Console.WriteLine(await aliceGreeterProxy.GreetAsync());

// Change back the greeting message to Hello.
await greeterAdminProxy.ChangeGreetingAsync("Hello");

await connection.ShutdownAsync();
