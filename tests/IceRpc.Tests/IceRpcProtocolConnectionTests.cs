// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Transports;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Net.Security;
using System.Net;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class IceRpcProtocolConnectionTests
{
    public static IEnumerable<TestCaseData> ExceptionIsEncodedAsDispatchExceptionSource
    {
        get
        {
            yield return new TestCaseData(new InvalidDataException("invalid data"), DispatchErrorCode.InvalidData);
            // Slice1 only exception will get encoded as unhandled exception with Slice2
            yield return new TestCaseData(new MyDerivedException(), DispatchErrorCode.UnhandledException);
            yield return new TestCaseData(new InvalidOperationException(), DispatchErrorCode.UnhandledException);
        }
    }

    /// <summary>Verifies that aborting the client connection cancels the dispatches.</summary>
    [Test]
    public async Task Dispose_cancels_dispatches()
    {
        // Arrange
        using var dispatcher = new TestDispatcher();
        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        _ = sut.Client.InvokeAsync(new OutgoingRequest(new ServiceAddress(Protocol.IceRpc)));

        await dispatcher.DispatchStart; // Wait for the dispatch to start

        // Act
        await sut.Server.DisposeAsync();

        // Assert
        Assert.That(async () => await dispatcher.DispatchComplete, Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>Verifies that exceptions thrown by the dispatcher are correctly mapped to a DispatchException with the
    /// expected error code.</summary>
    /// <param name="thrownException">The exception to throw by the dispatcher.</param>
    /// <param name="errorCode">The expected <see cref="DispatchErrorCode"/>.</param>
    [Test, TestCaseSource(nameof(ExceptionIsEncodedAsDispatchExceptionSource))]
    public async Task Exception_is_encoded_as_a_dispatch_exception(
        Exception thrownException,
        DispatchErrorCode errorCode)
    {
        // Arrange
        var dispatcher = new InlineDispatcher((request, cancel) => throw thrownException);

        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();
        var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        // Act
        var response = await sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(response.ResultType, Is.EqualTo(ResultType.Failure));
        var exception = await response.DecodeFailureAsync(
            request,
            new ServiceProxy(sut.Client, request.ServiceAddress)) as DispatchException;
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.ErrorCode, Is.EqualTo(errorCode));
    }

    [Test]
    public async Task Not_dispatched_request_gets_connection_closed_on_server_connection_shutdown()
    {
        // Arrange
        HoldMultiplexedServerTransport? serverTransport = null;
        using var dispatcher = new TestDispatcher();

        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .AddSingleton<IMultiplexedServerTransport>(
                provider => serverTransport = new HoldMultiplexedServerTransport(
                    new SlicServerTransport(provider.GetRequiredService<IDuplexServerTransport>())))
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();
        var invokeTask = sut.Client.InvokeAsync(new OutgoingRequest(new ServiceAddress(Protocol.IceRpc)));
        await dispatcher.DispatchStart; // Wait for the dispatch to start

        serverTransport!.Hold(HoldOperation.AcceptStream); // Don't accept the next stream.
        var invokeTask2 = sut.Client.InvokeAsync(new OutgoingRequest(new ServiceAddress(Protocol.IceRpc)));

        // Act
        var shutdownTask = sut.Server.ShutdownAsync("");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(invokeTask.IsCompleted, Is.False);
            Assert.That(async () => await invokeTask2, Throws.InstanceOf<ConnectionClosedException>());
        });
        dispatcher.ReleaseDispatch();
        Assert.That(async () => await invokeTask, Throws.Nothing);
    }

    [Test]
    public async Task Not_sent_request_gets_connection_closed_on_server_connection_shutdown(
        [Values(false, true)] bool isOneway)
    {
        // Arrange
        HoldMultiplexedClientTransport? clientTransport = null;
        using var dispatcher = new TestDispatcher();

        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .AddSingleton<IMultiplexedClientTransport>(
                provider => clientTransport = new HoldMultiplexedClientTransport(
                    new SlicClientTransport(provider.GetRequiredService<IDuplexClientTransport>())))
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();
        var invokeTask = sut.Client.InvokeAsync(new OutgoingRequest(new ServiceAddress(Protocol.IceRpc)));
        await dispatcher.DispatchStart; // Wait for the dispatch to start

        clientTransport!.Hold(HoldOperation.Write); // Hold writes to prevent the sending of the request.
        var invokeTask2 = sut.Client.InvokeAsync(
            new OutgoingRequest(new ServiceAddress(Protocol.IceRpc))
            {
                IsOneway = isOneway
            });

        // Act
        var shutdownTask = sut.Server.ShutdownAsync("");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(invokeTask.IsCompleted, Is.False);
            Assert.That(async () => await invokeTask2, Throws.InstanceOf<ConnectionClosedException>());
        });
        dispatcher.ReleaseDispatch();
        Assert.That(async () => await invokeTask, Throws.Nothing);
    }

    /// <summary>Ensures that the response payload is completed if the response fields are invalid.</summary>
    [Test]
    public async Task Payload_completed_on_invalid_response_fields()
    {
        // Arrange
        var payloadDecorator = new PayloadPipeReaderDecorator(InvalidPipeReader.Instance);
        var dispatcher = new InlineDispatcher((request, cancel) =>
            {
                var response = new OutgoingResponse(request)
                {
                    Payload = payloadDecorator
                };
                response.Fields = response.Fields.With(
                    ResponseFieldKey.CompressionFormat,
                    (ref SliceEncoder encoder) => throw new NotSupportedException("invalid request fields"));
                return new(response);
            });

        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new ServiceAddress(Protocol.IceRpc)));

        // Assert
        Assert.That(await payloadDecorator.Completed, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>Ensures that the request payload stream is completed on valid request.</summary>
    [Test]
    public async Task PayloadStream_completed_on_valid_request([Values(true, false)] bool isOneway)
    {
        // Arrange
        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc)
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        var payloadStreamDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc))
        {
            IsOneway = isOneway,
            PayloadStream = payloadStreamDecorator
        };

        // Act
        _ = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(await payloadStreamDecorator.Completed, Is.Null);
    }

    /// <summary>Ensures that the request payload is completed if the payload stream is invalid.</summary>
    [Test]
    public async Task PayloadStream_completed_on_invalid_request_payload([Values(true, false)] bool isOneway)
    {
        // Arrange
        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc)
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        var payloadStreamDecorator = new PayloadPipeReaderDecorator(InvalidPipeReader.Instance);
        var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc))
        {
            IsOneway = isOneway,
            PayloadStream = payloadStreamDecorator
        };

        // Act
        _ = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(await payloadStreamDecorator.Completed, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>Ensures that the response payload stream is completed on a valid response.</summary>
    [Test]
    public async Task PayloadStream_completed_on_valid_response()
    {
        // Arrange
        var payloadStreamDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var dispatcher = new InlineDispatcher((request, cancel) =>
                new(new OutgoingResponse(request)
                {
                    PayloadStream = payloadStreamDecorator
                }));

        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new ServiceAddress(Protocol.IceRpc)));

        // Assert
        Assert.That(await payloadStreamDecorator.Completed, Is.Null);
    }

    /// <summary>Ensures that the response payload is completed on an invalid response payload stream.</summary>
    [Test]
    public async Task PayloadStream_completed_on_invalid_response_payload()
    {
        // Arrange
        var payloadStreamDecorator = new PayloadPipeReaderDecorator(InvalidPipeReader.Instance);
        var dispatcher = new InlineDispatcher((request, cancel) =>
                new(new OutgoingResponse(request)
                {
                    PayloadStream = payloadStreamDecorator
                }));

        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new ServiceAddress(Protocol.IceRpc)));

        // Assert
        Assert.That(await payloadStreamDecorator.Completed, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>Ensures that the request payload writer is completed on an invalid request.</summary>
    /// <remarks>This test only works with the icerpc protocol since it relies on reading the payload after the payload
    /// writer is created.</remarks>
    [Test]
    public async Task PayloadWriter_completed_with_invalid_request()
    {
        // Arrange
        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc)
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc))
        {
            Payload = InvalidPipeReader.Instance
        };
        var payloadWriterSource = new TaskCompletionSource<PayloadPipeWriterDecorator>();
        request.Use(writer =>
            {
                var payloadWriterDecorator = new PayloadPipeWriterDecorator(writer);
                payloadWriterSource.SetResult(payloadWriterDecorator);
                return payloadWriterDecorator;
            });

        // Act
        _ = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(await (await payloadWriterSource.Task).Completed, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>Ensures that the request payload writer is completed on an invalid response.</summary>
    /// <remarks>This test only works with the icerpc protocol since it relies on reading the payload after the payload
    /// writer is created.</remarks>
    [Test]
    public async Task PayloadWriter_completed_with_invalid_response()
    {
        // Arrange
        var payloadWriterSource = new TaskCompletionSource<PayloadPipeWriterDecorator>();
        var dispatcher = new InlineDispatcher((request, cancel) =>
            {
                var response = new OutgoingResponse(request)
                {
                    Payload = InvalidPipeReader.Instance
                };
                response.Use(writer =>
                    {
                        var payloadWriterDecorator = new PayloadPipeWriterDecorator(writer);
                        payloadWriterSource.SetResult(payloadWriterDecorator);
                        return payloadWriterDecorator;
                    });
                return new(response);
            });

        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new ServiceAddress(Protocol.IceRpc)));

        // Assert
        Assert.That(await (await payloadWriterSource.Task).Completed, Is.InstanceOf<NotSupportedException>());
    }

    [Test]
    public async Task Request_with_header_size_larger_than_max_header_size_fails()
    {
        var services = new ServiceCollection().AddProtocolTest(Protocol.IceRpc);
        services.AddOptions<ServerOptions>().Configure(options => options.ConnectionOptions.MaxIceRpcHeaderSize = 100);
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc))
        {
            Operation = new string('x', 100)
        };

        Assert.That(
            async () => await sut.Client.InvokeAsync(request),
            Throws.InstanceOf<ProtocolException>());
    }

    [Test]
    public async Task Response_with_large_header()
    {
        // Arrange
        // This large value should be large enough to create multiple buffers for the response header.
        var expectedValue = new string('A', 16_000);
        var dispatcher = new InlineDispatcher((request, cancel) =>
        {
            var response = new OutgoingResponse(request);
            response.Fields = response.Fields.With(
                (ResponseFieldKey)1000,
                (ref SliceEncoder encoder) => encoder.EncodeString(expectedValue));
            return new(response);
        });
        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        var payloadDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        // Act
        var response = await sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(
            response.Fields.DecodeValue((ResponseFieldKey)1000, (ref SliceDecoder decoder) => decoder.DecodeString()),
            Is.EqualTo(expectedValue));
    }

    [Flags]
    private enum HoldOperation
    {
        None,
        AcceptStream,
        Write,
    }

    private sealed class HoldMultiplexedServerTransport : IMultiplexedServerTransport, IDisposable
    {
        private readonly IMultiplexedServerTransport _decoratee;
        private HoldMultiplexedListener? _listener;

        public string Name => _decoratee.Name;

        public void Dispose() => _listener?.Dispose();

        public IListener<IMultiplexedConnection> Listen(
            ServerAddress serverAddress,
            MultiplexedConnectionOptions options,
            SslServerAuthenticationOptions? serverAuthenticationOptions) =>
            _listener = new HoldMultiplexedListener(
                _decoratee.Listen(serverAddress, options, serverAuthenticationOptions));

        internal HoldMultiplexedServerTransport(IMultiplexedServerTransport decoratee) => _decoratee = decoratee;

        internal void Hold(HoldOperation operation) => _listener?.Hold(operation);
    }

    private sealed class HoldMultiplexedClientTransport : IMultiplexedClientTransport, IAsyncDisposable
    {
        private readonly IMultiplexedClientTransport _decoratee;
        private HoldMultiplexedConnection? _connection;

        public string Name => _decoratee.Name;

        public bool CheckParams(ServerAddress serverAddress) => _decoratee.CheckParams(serverAddress);

        public IMultiplexedConnection CreateConnection(
            ServerAddress serverAddress,
            MultiplexedConnectionOptions options,
            SslClientAuthenticationOptions? clientAuthenticationOptions)
        {
            Debug.Assert(_connection == null);
            _connection = new HoldMultiplexedConnection(
                _decoratee.CreateConnection(serverAddress, options, clientAuthenticationOptions));
            return _connection;
        }

        public ValueTask DisposeAsync() => _connection?.DisposeAsync() ?? new();

        internal HoldMultiplexedClientTransport(IMultiplexedClientTransport decoratee) => _decoratee = decoratee;

        internal void Hold(HoldOperation operation) => _connection!.Hold(operation);
    }

    private sealed class HoldMultiplexedListener : IListener<IMultiplexedConnection>
    {
        private readonly IListener<IMultiplexedConnection> _decoratee;
        private HoldMultiplexedConnection? _connection;

        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        public async Task<(IMultiplexedConnection, EndPoint)> AcceptAsync()
        {
            Debug.Assert(_connection == null);
            (IMultiplexedConnection connection, EndPoint address) = await _decoratee.AcceptAsync();
            _connection = new HoldMultiplexedConnection(connection);
            return (_connection, address);
        }

        public void Dispose()
        {
            _ = _connection!.DisposeAsync().AsTask();
            _decoratee.Dispose();
        }

        internal HoldMultiplexedListener(IListener<IMultiplexedConnection> decoratee) => _decoratee = decoratee;

        internal void Hold(HoldOperation operation) => _connection!.Hold(operation);
    }

    private sealed class HoldMultiplexedConnection : IMultiplexedConnection
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;
        internal HoldOperation HoldOperation { get; private set; }

        private readonly IMultiplexedConnection _decoratee;

        public async ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancel)
        {
            var stream = new HoldMultiplexedStream(this, await _decoratee.AcceptStreamAsync(cancel));
            if (HoldOperation.HasFlag(HoldOperation.AcceptStream))
            {
                await Task.Delay(-1, cancel);
            }
            return stream;
        }

        public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancel) =>
            _decoratee.ConnectAsync(cancel);

        public IMultiplexedStream CreateStream(bool bidirectional) =>
            new HoldMultiplexedStream(this, _decoratee.CreateStream(bidirectional));

        public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

        public Task ShutdownAsync(Exception completeException, CancellationToken cancel) =>
            _decoratee.ShutdownAsync(completeException, cancel);

        internal HoldMultiplexedConnection(IMultiplexedConnection decoratee) => _decoratee = decoratee;

        internal void Hold(HoldOperation operation) => HoldOperation |= operation;
    }

    private sealed class HoldMultiplexedStream : IMultiplexedStream
    {
        public ulong Id => _decoratee.Id;

        public PipeReader Input => _decoratee.Input;

        public bool IsBidirectional => _decoratee.IsBidirectional;

        public bool IsRemote => _decoratee.IsRemote;

        public bool IsStarted => _decoratee.IsStarted;

        public PipeWriter Output => _output ?? _decoratee.Output;

        public Task ReadsClosed => _decoratee.ReadsClosed;

        public Task WritesClosed => _decoratee.WritesClosed;

        public HoldOperation HoldOperation =>
            // Don't hold operations for the icerpc control stream
            (IsBidirectional || !IsStarted || Id >= 4) ? _connection.HoldOperation : HoldOperation.None;

        private readonly HoldMultiplexedConnection _connection;
        private readonly IMultiplexedStream _decoratee;
        private readonly HoldPipeWriter? _output;

        public void Abort(Exception exception)
        {
            _output?.Abort(exception);
            _decoratee.Abort(exception);
        }

        internal HoldMultiplexedStream(HoldMultiplexedConnection connection, IMultiplexedStream decoratee)
        {
            _connection = connection;
            _decoratee = decoratee;
            if (!IsRemote || IsBidirectional)
            {
                _output = new HoldPipeWriter(this, decoratee.Output);
            }
        }
    }

    private class HoldPipeWriter : PipeWriter
    {
        private readonly TaskCompletionSource _abortTaskCompletionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly HoldMultiplexedStream _stream;
        private readonly PipeWriter _decoratee;

        public override void Advance(int bytes) => _decoratee.Advance(bytes);

        public override void CancelPendingFlush() => _decoratee.CancelPendingFlush();

        public override void Complete(Exception? exception) => _decoratee.Complete(exception);

        public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken)
        {
            if (_stream.HoldOperation.HasFlag(HoldOperation.Write))
            {
                await _abortTaskCompletionSource.Task.WaitAsync(cancellationToken);
            }
            return await _decoratee.FlushAsync(cancellationToken);
        }

        public override Memory<byte> GetMemory(int sizeHint = 0) => _decoratee.GetMemory(sizeHint);

        public override Span<byte> GetSpan(int sizeHint = 0) => _decoratee.GetSpan(sizeHint);

        public override async ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancel)
        {
            if (_stream.HoldOperation.HasFlag(HoldOperation.Write))
            {
                await _abortTaskCompletionSource.Task.WaitAsync(cancel);
            }
            return await _decoratee.WriteAsync(source, cancel);
        }

        internal HoldPipeWriter(HoldMultiplexedStream stream, PipeWriter decoratee)
        {
            _stream = stream;
            _decoratee = decoratee;
        }

        internal void Abort(Exception exception) => _abortTaskCompletionSource.SetException(exception);
    }
}
