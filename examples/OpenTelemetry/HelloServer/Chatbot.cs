// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace OpenTelemetryExample;

/// <summary>A Chatbot is an IceRPC service that implements Slice interface 'Hello'.</summary>
internal class Chatbot : Service, IHelloService
{
    private readonly ICrm _crm;

    internal Chatbot(ICrm crm) => _crm = crm;

    public async ValueTask<string> SayHelloAsync(
        string name,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching sayHello request {{ name = '{name}' }}");
        if (await _crm.TryAddCustomerAsync(name, features, cancellationToken))
        {
            return $"Hello, {name}!";
        }
        else
        {
            return $"Welcome back, {name}!";
        }
    }
}
