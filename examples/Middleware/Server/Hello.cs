// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;

namespace Demo;

public class Hello : Service, IHello
{
    public ValueTask<string> SayHelloAsync(string name, IFeatureCollection features, CancellationToken cancel)
    {
        if (isAuthenticated(features))
        {
            Console.WriteLine($"{name} says hello!");
            return new($"Hello, {name}!");
        }
        else
        {
            throw new DispatchException("Unauthorized. You did not supply the correct password", (DispatchErrorCode)404);
        }
    }

    bool isAuthenticated(IFeatureCollection features)
    {
        // Retrieve the feature collection containing the password and the user supplied token
        string? token = features.Get<Dictionary<string, string?>>()?["token"];
        string? password = features.Get<Dictionary<string, string?>>()?["authorization"];

        if (token != null && password != null && token == password)
        {
            return true;
        }

        return false;
    }
}
