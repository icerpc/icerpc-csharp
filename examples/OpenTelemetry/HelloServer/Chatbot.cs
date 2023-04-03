// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace OpenTelemetryExample;

/// <summary>A Chatbot is an IceRPC service that implements Slice interface 'Greeter'.</summary>
internal class Chatbot : Service, IGreeterService
{
    private readonly ICrm _crm;

    internal Chatbot(ICrm crm) => _crm = crm;

    public async ValueTask<string> GreetAsync(
        string name,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching greet request {{ name = '{name}' }}");
        if (await _crm.TryAddCustomerAsync(name, features, cancellationToken))
        {
            return $"Greeter, {name}!";
        }
        else
        {
            return $"Welcome back, {name}!";
        }
    }
}
