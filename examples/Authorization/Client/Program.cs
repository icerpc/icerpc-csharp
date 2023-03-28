// Copyright (c) ZeroC, Inc.

using AuthorizationExample;
using IceRpc;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

var authenticatorProxy = new AuthenticatorProxy(connection, new Uri("icerpc:/authenticator"));

// Authenticate the "friend" user and get its authentication token.
ReadOnlyMemory<byte> friendToken = await authenticatorProxy.AuthenticateAsync("friend", "password");

// A hello proxy that doesn't use any authentication token.
var unauthenticatedHelloProxy = new HelloProxy(connection, new Uri("icerpc:/hello"));

// The SayHello invocation on the unauthenticated hello proxy prints a generic message.
Console.WriteLine(await unauthenticatedHelloProxy.SayHelloAsync());

// Create a hello proxy that uses a pipe line to insert the "friend" token into a request field.
Pipeline friendPipeline = new Pipeline().UseAuthentication(friendToken).Into(connection);
var friendHelloProxy = new HelloProxy(friendPipeline, new Uri("icerpc:/hello"));

// The SayHello invocation on the authenticated "friend" hello proxy prints a custom message for "friend".
Console.WriteLine(await friendHelloProxy.SayHelloAsync());

// A hello admin proxy that uses the "friend" pipeline is not authorized to change the greeting message because "friend"
// doesn't have administrative privileges.
var helloAdminProxy = new HelloAdminProxy(friendPipeline, new Uri("icerpc:/helloAdmin"));
try
{
    await helloAdminProxy.ChangeGreetingAsync("Bonjour");
}
catch (DispatchException exception) when (exception.StatusCode == StatusCode.Unauthorized)
{
    Console.WriteLine("The 'friend' user is not authorized to change the greeting message.");
}

// Authenticate the "admin" user and get its authentication token.
ReadOnlyMemory<byte> adminToken = await authenticatorProxy.AuthenticateAsync("admin", "password");

// Create a hello admin proxy that uses a pipe line to insert the "admin" token into a request field.
Pipeline adminPipeline = new Pipeline().UseAuthentication(adminToken).Into(connection);
helloAdminProxy = new HelloAdminProxy(adminPipeline, new Uri("icerpc:/helloAdmin"));

// Changing the greeting message should succeed this time because the "admin" user has administrative privilege.
await helloAdminProxy.ChangeGreetingAsync("Bonjour");

// The SayHello invocation should print a greeting with the updated greeting message.
Console.WriteLine(await friendHelloProxy.SayHelloAsync());

// Change back the greeting message to Hello.
await helloAdminProxy.ChangeGreetingAsync("Hello");

await connection.ShutdownAsync();
