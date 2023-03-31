// Copyright (c) ZeroC, Inc.

using AuthorizationExample;
using IceRpc;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

var authenticatorProxy = new AuthenticatorProxy(connection, new Uri("icerpc:/authenticator"));

// Authenticate the "friend" user and get its identity token.
ReadOnlyMemory<byte> friendToken = await authenticatorProxy.AuthenticateAsync("friend", "password");

// A greeting proxy that doesn't use any identity token.
var unauthenticatedGreetingProxy = new GreeterProxy(connection, new Uri("icerpc:/greeting"));

// The SayGreeting invocation on the unauthenticated greeting proxy prints a generic message.
Console.WriteLine(await unauthenticatedGreetingProxy.GreetAsync());

// Create a greeting proxy that uses a pipe line to insert the "friend" token into a request field.
Pipeline friendPipeline = new Pipeline().UseAuthentication(friendToken).Into(connection);
var friendGreetingProxy = new GreeterProxy(friendPipeline, new Uri("icerpc:/greeting"));

// The SayGreeting invocation on the authenticated "friend" greeting proxy prints a custom message for "friend".
Console.WriteLine(await friendGreetingProxy.GreetAsync());

// A greeting admin proxy that uses the "friend" pipeline is not authorized to change the greeting message because "friend"
// doesn't have administrative privileges.
var greetingAdminProxy = new GreeterAdminProxy(friendPipeline, new Uri("icerpc:/greetingAdmin"));
try
{
    await greetingAdminProxy.ChangeGreetingAsync("Bonjour");
}
catch (DispatchException exception) when (exception.StatusCode == StatusCode.Unauthorized)
{
    Console.WriteLine("The 'friend' user is not authorized to change the greeting message.");
}

// Authenticate the "admin" user and get its identity token.
ReadOnlyMemory<byte> adminToken = await authenticatorProxy.AuthenticateAsync("admin", "admin-password");

// Create a greeting admin proxy that uses a pipe line to insert the "admin" token into a request field.
Pipeline adminPipeline = new Pipeline().UseAuthentication(adminToken).Into(connection);
greetingAdminProxy = new GreeterAdminProxy(adminPipeline, new Uri("icerpc:/greetingAdmin"));

// Changing the greeting message should succeed this time because the "admin" user has administrative privilege.
await greetingAdminProxy.ChangeGreetingAsync("Bonjour");

// The SayGreeting invocation should print a greeting with the updated greeting message.
Console.WriteLine(await friendGreetingProxy.GreetAsync());

// Change back the greeting message to Greeting.
await greetingAdminProxy.ChangeGreetingAsync("Greeting");

await connection.ShutdownAsync();
