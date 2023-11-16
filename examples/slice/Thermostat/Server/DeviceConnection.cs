// Copyright (c) ZeroC, Inc.

using IceRpc;

namespace ThermostatServer;

/// <summary>Represents the server-side of the connection from the device to this server. This connection remains valid
/// across re-connections from the device.</summary>
internal class DeviceConnection : IInvoker
{
    private volatile IInvoker? _invoker;

    public async Task<IncomingResponse> InvokeAsync(
        OutgoingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_invoker is IInvoker invoker)
        {
            try
            {
                return await invoker.InvokeAsync(request, cancellationToken);
            }
            catch (ObjectDisposedException)
            {
                // throw NotFound below
            }
        }
        throw new DispatchException(StatusCode.NotFound, "The device is not connected.");
    }

    /// <summary>Sets the invoker that represents the latest connection from the device.</summary>
    internal void SetInvoker(IInvoker invoker) => _invoker = invoker;
}
