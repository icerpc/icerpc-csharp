// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace AuthorizationExample;

/// <summary>A Chatbot is an IceRPC service that implements the Slice interface 'Greeter'.</summary>
internal class Chatbot : Service, IGreeterService
{
    internal string Greeting { get; set; } = "Hello";

    public ValueTask<string> GreetAsync(IFeatureCollection features, CancellationToken cancellationToken)
    {
        string name;
        bool isAdmin;
        if (features.Get<IIdentityFeature>() is IIdentityFeature identityFeature)
        {
            name = identityFeature.Name;
            isAdmin = identityFeature.IsAdmin;
        }
        else
        {
            name = "stranger";
            isAdmin = false;
        }

        Console.WriteLine($"Dispatching Greet request {{ name = '{name}' isAdmin = {isAdmin} }}");
        return new($"{Greeting}, {name}!");
    }
}
