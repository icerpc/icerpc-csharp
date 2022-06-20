// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using IceRpc.Features;
using IceRpc.Slice;

// Establish the connection to the server
await using var connection = new ClientConnection("icerpc://127.0.0.1");

// Setup the invocation pipeline with the authentication interceptor
IInvoker pipeline = new Pipeline().Use(next => new AuthenticationInterceptor(next));

// Create the proxy using the connection and the invocation pipeline
IHelloPrx hello = HelloPrx.FromConnection(connection, invoker: pipeline);

Console.Write("To say authenticate with the server you must type the password: ");

if (Console.ReadLine() is string password)
{
    Console.Write("To say hello to the server, type your name: ");

    if (Console.ReadLine() is string name)
    {
        // Creating a dictionary to use as a feature
        Dictionary<string, string> headers = new Dictionary<string, string>();
        headers.Add("token", password);

        // Creating a feature collection and setting the headers dictionary as a feature
        IFeatureCollection features = new FeatureCollection();
        features.Set<Dictionary<string, string>>(headers);

        // Invoking the say hello method
        try
        {
            Console.WriteLine(await hello.SayHelloAsync(name, features));
        }
        catch (DispatchException ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}
