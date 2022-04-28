// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using IceRpc.Transports;
using NUnit.Framework;
using System.Buffers;

namespace IceRpc.Tests;

[Timeout(30000)]
public class FieldTests
{
    private const ConnectionFieldKey TestConnectionFieldKey = (ConnectionFieldKey)100;
    private static readonly byte[] _connectionFieldValue = new byte[] { 0xAA, 0xBB, 0xCC };

    [Test]
    public async Task Client_connection_field_is_received_by_server_connection()
    {
        byte[]? fieldValue = null;
        var colocTransport = new ColocTransport();
        await using var server = new Server(new ServerOptions
        {
            Dispatcher = new InlineDispatcher((request, cancel) =>
            {
                fieldValue = request.Connection.Features.Get<byte[]>();
                return new(new OutgoingResponse(request));
            }),
            MultiplexedServerTransport = new SlicServerTransport(colocTransport.ServerTransport),
            OnConnect = (fields, features) => features.Set(fields[TestConnectionFieldKey].ToArray())
        });
        server.Listen();
        await using var connection = new Connection(new ConnectionOptions
        {
            Fields = new Dictionary<ConnectionFieldKey, OutgoingFieldValue>
            {
                [TestConnectionFieldKey] = new(new ReadOnlySequence<byte>(_connectionFieldValue))
            },
            MultiplexedClientTransport = new SlicClientTransport(colocTransport.ClientTransport),
            RemoteEndpoint = server.Endpoint
        });
        var prx = ServicePrx.FromConnection(connection);

        await prx.IcePingAsync();

        Assert.That(fieldValue, Is.Not.Null);
        Assert.That(fieldValue, Is.EqualTo(_connectionFieldValue));
    }

    [Test]
    public async Task Server_connection_field_is_received_by_client_connection()
    {
        var colocTransport = new ColocTransport();
        await using var server = new Server(new ServerOptions
        {
            Fields = new Dictionary<ConnectionFieldKey, OutgoingFieldValue>
            {
                [TestConnectionFieldKey] = new(new ReadOnlySequence<byte>(_connectionFieldValue))
            },
            MultiplexedServerTransport = new SlicServerTransport(colocTransport.ServerTransport)
        });
        server.Listen();
        await using var connection = new Connection(new ConnectionOptions
        {
            MultiplexedClientTransport = new SlicClientTransport(colocTransport.ClientTransport),
            OnConnect = (fields, features) => features.Set(fields[TestConnectionFieldKey].ToArray()),
            RemoteEndpoint = server.Endpoint
        });

        await connection.ConnectAsync();

        Assert.That(connection.Features.Get<byte[]>, Is.EqualTo(_connectionFieldValue));
    }
}
