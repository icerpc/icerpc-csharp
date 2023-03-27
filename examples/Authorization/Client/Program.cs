// Copyright (c) ZeroC, Inc.

using AuthorizationExample;
using IceRpc;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

// A hello proxy that doesn't use any authentication will print a generic greeting.
var unauthenticatedHelloProxy = new HelloProxy(connection, new Uri("icerpc:/hello"));
Console.WriteLine(await unauthenticatedHelloProxy.SayHelloAsync());

// Authenticate the "friend" user to get its authentication token.
var authenticatorProxy = new AuthenticatorProxy(connection, new Uri("icerpc:/authenticator"));
byte[] friendToken = await authenticatorProxy.AuthenticateAsync("friend", "password");

// Setup a pipe line that inserts the "friend" token into a request field.
Pipeline friendPipeline = new Pipeline().UseAuthenticationToken(friendToken).Into(connection);

// A hello proxy that uses  "friend" pipeline will print a custom greeting for "friend".
var friendHelloProxy = new HelloProxy(friendPipeline, new Uri("icerpc:/hello"));
Console.WriteLine(await friendHelloProxy.SayHelloAsync());

// A hello admin proxy that uses the "friend" pipeline is not authorized to change the greeting message.
var helloAdminProxy = new HelloAdminProxy(friendPipeline, new Uri("icerpc:/helloAdmin"));
try
{
    await helloAdminProxy.ChangeGreetingAsync("Bonjour");
}
catch (DispatchException exception) when (exception.StatusCode == StatusCode.Unauthorized)
{
    Console.WriteLine("The friend user is not authorized to change the greeting.");
}

// Authenticate the "admin" user to get its authentication token and setup a pipeline with the "admin" token.
byte[] adminToken = await authenticatorProxy.AuthenticateAsync("admin", "password");
helloAdminProxy = new HelloAdminProxy(
    new Pipeline().UseAuthenticationToken(adminToken).Into(connection),
    new Uri("icerpc:/helloAdmin"));

// Change the greeting message.
await helloAdminProxy.ChangeGreetingAsync("Bonjour");

// Authenticated hello with updated greeting.
Console.WriteLine(await friendHelloProxy.SayHelloAsync());

// Change back the greeting to Hello.
await helloAdminProxy.ChangeGreetingAsync("Hello");

await connection.ShutdownAsync();
