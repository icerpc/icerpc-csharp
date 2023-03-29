// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace AuthorizationExample;

/// <summary>A Chatbot is an IceRPC service that implements the Slice interface 'Greeting'.</summary>
internal class Chatbot : Service, IGreetingService
{
    internal string Greeting { get; set; } = "Hello";

    public ValueTask<string> GetGreetingAsync(IFeatureCollection features, CancellationToken cancellationToken)
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

        Console.WriteLine($"Dispatching GetGreeting request {{ name = '{name}' isAdmin = {isAdmin} }}");
        return new($"{Greeting}, {name}!");
    }
}
