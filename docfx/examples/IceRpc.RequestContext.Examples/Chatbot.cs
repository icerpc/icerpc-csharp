// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;

namespace GreeterExample;

/// <summary>A Chatbot is an IceRPC service that implements Slice interface 'Greeter'.</summary>
[Service]
internal partial class Chatbot : IGreeterService
{
    #region RequestContextFeature
    public ValueTask<string> GreetAsync(
        string name,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        if (features.Get<IRequestContextFeature>() is IRequestContextFeature contextFeature)
        {
            foreach ((string key, string value) in contextFeature.Value)
            {
                // ...
            }
        }
        return new($"Hello, {name}!");
    }
    #endregion
}
