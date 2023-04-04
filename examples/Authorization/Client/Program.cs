// Copyright (c) ZeroC, Inc.

using AuthorizationExample;
using IceRpc;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

// A `Greeter` proxy that doesn't use any authentication. An authentication token is not needed to call `Greet`.
var unauthenticatedGreeterProxy = new GreeterProxy(connection, new Uri("icerpc:/greeter"));

// Unauthenticated greeter; prints generic greeting.
Console.WriteLine(await unauthenticatedGreeterProxy.GreetAsync());

// A `SessionManager` proxy that doesn't use any authentication. Used to create new session tokens.
var sessionManagerProxy = new SessionManagerProxy(connection, new Uri("icerpc:/sessionManager"));

// Get an authentication token. The token is used to authenticate future requests.
var token = new Guid(await sessionManagerProxy.CreateSessionAsync("friend"));

// Add an interceptor to the invocation pipeline that inserts the token into a request field.
Pipeline authenticatedPipeline = new Pipeline().UseSession(token).Into(connection);

// A `Greeter` proxy that uses the authentication pipeline. When an authentication token is used, `Greet`
// will return a personalized greeting.
var greeterProxy = new GreeterProxy(authenticatedPipeline, new Uri("icerpc:/greeter"));

// A `GreeterAdmin` proxy that uses the authentication pipeline. An authentication token is needed to change the greeting.
var greeterAdminProxy = new GreeterAdminProxy(authenticatedPipeline, new Uri("icerpc:/greeterAdmin"));

// Authenticated greeter.
Console.WriteLine(await greeterProxy.GreetAsync());

// Change the greeting using the authentication token.
await greeterAdminProxy.ChangeGreetingAsync("Bonjour");

// Authenticated greeter with updated greeting.
Console.WriteLine(await greeterProxy.GreetAsync());

await connection.ShutdownAsync();
