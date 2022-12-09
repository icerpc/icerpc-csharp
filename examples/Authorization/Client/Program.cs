// Copyright (c) ZeroC, Inc. All rights reserved.

using AuthorizationExample;
using IceRpc;

await using var connection = new ClientConnection(new Uri("icerpc://127.0.0.1"));

// Stores the session token
var sessionData = new SessionData();

var pipeline = new Pipeline();
// Inserts the token into a request field
pipeline.Use(sessionData.Interceptor);
pipeline.Into(connection);

IHelloProxy helloProxy = new HelloProxy(pipeline, new Uri("icerpc:/hello"));
ISessionProxy sessionProxy = new SessionProxy(pipeline, new Uri("icerpc:/session"));
IAdminProxy adminProxy = new AdminProxy(pipeline, new Uri("icerpc:/admin/"));

// Un-authenticated hello; prints generic greeting
Console.WriteLine(await helloProxy.SayHelloAsync());

// Try to change the greeting. Since we're not logged in, this will fail.
try
{
    await adminProxy.ChangeGreetingAsync("Bonjour");
}
catch (DispatchException ex) when (ex.StatusCode == StatusCode.Unauthorized)
{
    Console.WriteLine(ex.Message);
}

// Login and store the token
sessionData.Token = await sessionProxy.LoginAsync("friend");

// Authenticated hello; prints personalized greeting
Console.WriteLine(await helloProxy.SayHelloAsync());

// Try again with the authentication token
await adminProxy.ChangeGreetingAsync("Bonjour");

// Authenticated hello with new greeting
Console.WriteLine(await helloProxy.SayHelloAsync());
