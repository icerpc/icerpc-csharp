// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace GreeterExample;

/// <summary>A Chatbot is an IceRPC service that implements Slice interface 'Greeter'.</summary>
[SliceService]
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
