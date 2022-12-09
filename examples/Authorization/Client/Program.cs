// Copyright (c) ZeroC, Inc. All rights reserved.

using AuthorizationExample;
using IceRpc;

await using var connection = new ClientConnection(new Uri("icerpc://127.0.0.1"));

// A `Hello` proxy that doesn't use any authentication
var unauthenticatedHelloProxy = new HelloProxy(connection, new Uri("icerpc:/hello"));

// Unauthenticated hello; prints generic greeting
Console.WriteLine(await unauthenticatedHelloProxy.SayHelloAsync());

// A `Session` proxy that doesn't use any authentication. An authentication token is not need to login.
var sessionProxy = new SessionProxy(connection, new Uri("icerpc:/session"));

// Get an authentication token. The token is used to authenticate future requests.
byte[] token = await sessionProxy.LoginAsync("friend");

var authenticatedPipeline = new Pipeline();
// Add an interceptor to the invocation pipeline that inserts the token into a request field
authenticatedPipeline.Use(next => new SessionInterceptor(next, token));
authenticatedPipeline.Into(connection);

// A `Hello` proxy that uses the authentication pipeline. When an authentication token is used, `SayHello`
// will return a personalized greeting.
var helloProxy = new HelloProxy(authenticatedPipeline, new Uri("icerpc:/hello"));

// A `HelloAdmin`that uses the authentication pipeline. An authentication token is needed to change the greeting.
var helloAdminProxy = new HelloAdminProxy(authenticatedPipeline, new Uri("icerpc:/helloAdmin/"));

// Authenticated hello
Console.WriteLine(await helloProxy.SayHelloAsync());

// Change the greeting using the authentication token
await helloAdminProxy.ChangeGreetingAsync("Bonjour");

// Authenticated hello with updated greeting
Console.WriteLine(await helloProxy.SayHelloAsync());
