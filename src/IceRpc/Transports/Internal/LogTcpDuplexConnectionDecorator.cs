// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Security.Authentication;

namespace IceRpc.Transports.Internal;

/// <summary>The log decorator installed by the TCP transports.</summary>
internal class LogTcpTransportConnectionDecorator : IDuplexConnection
{
    public Endpoint Endpoint => _decoratee.Endpoint;

    private readonly TcpDuplexConnection _decoratee;
    private readonly ILogger _logger;

    public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancel)
    {
        try
        {
            TransportConnectionInformation result = await _decoratee.ConnectAsync(cancel).ConfigureAwait(false);

            if (_decoratee.SslStream is SslStream sslStream)
            {
                _logger.LogTlsAuthentication(sslStream);
            }

            _logger.LogTcpConnect(_decoratee.Socket.ReceiveBufferSize, _decoratee.Socket.SendBufferSize);

            return result;
        }
        catch (AuthenticationException ex)
        {
            _logger.LogTlsAuthenticationFailed(ex);
            throw;
        }
    }

    void IDisposable.Dispose() => _decoratee.Dispose();

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel) =>
        _decoratee.ReadAsync(buffer, cancel);

    public Task ShutdownAsync(CancellationToken cancel) => _decoratee.ShutdownAsync(cancel);

    public override string? ToString() => _decoratee.ToString();

    public ValueTask WriteAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancel) =>
        _decoratee.WriteAsync(buffers, cancel);

    internal LogTcpTransportConnectionDecorator(TcpDuplexConnection decoratee, ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
    }
}
