// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using IceRpc.Internal;
using IceRpc.Slice;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.Collections.Immutable;
using System.IO.Pipelines;

namespace IceRpc.Tests;

[Timeout(5000)]
[Parallelizable(ParallelScope.All)]
public sealed class ProtocolConformanceTests
{
    private static readonly List<Protocol> _protocols = new() { Protocol.Ice, Protocol.IceRpc };

    private static IEnumerable<TestCaseData> ClientServerConnections
    {
        get
        {
            foreach (Protocol protocol in _protocols)
            {
                yield return new TestCaseData(protocol, ConnectionType.Client);
                yield return new TestCaseData(protocol, ConnectionType.Client);
            }
        }
    }

    /// <summary>Provides test case data for the <see cref="Payload_completed_on_request_and_response"> test. The test
    /// case data provides the outgoing request to send, the dipatcher to provide the response and the payload reader
    /// decorator use to ensure <see cref="PipeReader.Complete"/> is called.
    private static IEnumerable<TestCaseData> RequestsAndResponses
    {
        get
        {
            foreach (Protocol protocol in _protocols)
            {
                foreach (TestCaseData testCase in GetTestCaseData(protocol, isOneway: false))
                {
                    yield return testCase;
                }
                foreach (TestCaseData testCase in GetTestCaseData(protocol, isOneway: true))
                {
                    yield return testCase;
                }
            }

            static IEnumerable<TestCaseData> GetTestCaseData(Protocol protocol, bool isOneway)
            {
                // Valid request
                {
                    var payload = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
                    var request = new OutgoingRequest(new Proxy(protocol))
                        {
                            Payload = payload,
                            IsOneway = isOneway
                        };
                    var dispatcher = ConnectionOptions.DefaultDispatcher;
                    yield return new("request reader", protocol, request, dispatcher, payload);
                }

                // Valid response
                {
                    var request = new OutgoingRequest(new Proxy(protocol)) { IsOneway = isOneway };
                    var payload = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
                    var dispatcher = new InlineDispatcher((request, cancel) =>
                        new(new OutgoingResponse(request) { Payload = payload }));
                    yield return new("response reader", protocol, request, dispatcher, payload);
                }

                // Invalid requests
                {
                    var payload = new PayloadPipeReaderDecorator(InvalidPipeReader.Instance);
                    var request = new OutgoingRequest(new Proxy(protocol))
                        {
                            Payload = payload,
                            IsOneway = isOneway
                        };
                    var dispatcher = ConnectionOptions.DefaultDispatcher;
                    yield return new("request invalid reader", protocol, request, dispatcher, payload);
                }

                {
                    var payload = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
                    var request = new OutgoingRequest(new Proxy(protocol))
                        {
                            Payload = payload,
                            IsOneway = isOneway
                        };
                    request.Use(writer => InvalidPipeWriter.Instance);
                    var dispatcher = ConnectionOptions.DefaultDispatcher;
                    yield return new("request invalid writer", protocol, request, dispatcher, payload);
                }

                if (protocol.HasFields)
                {
                    var payload = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
                    var request = new OutgoingRequest(new Proxy(protocol))
                        {
                            Payload = payload,
                            IsOneway = isOneway
                        };
                    var dispatcher = ConnectionOptions.DefaultDispatcher;
                    request.Fields = request.Fields.With(
                        RequestFieldKey.Idempotent,
                        (ref SliceEncoder encoder) => throw new NotSupportedException("bogus header"));
                    yield return new("request invalid header", protocol, request, dispatcher, payload);
                }

                // Invalid responses
                if (!isOneway)
                {
                    {
                        var request = new OutgoingRequest(new Proxy(protocol)) { IsOneway = isOneway };
                        var payload = new PayloadPipeReaderDecorator(InvalidPipeReader.Instance);
                        var dispatcher = new InlineDispatcher((request, cancel) =>
                            new(new OutgoingResponse(request) { Payload = payload }));
                        yield return new("response invalid reader", protocol, request, dispatcher, payload);
                    }

                    {
                        var request = new OutgoingRequest(new Proxy(protocol)) { IsOneway = isOneway };
                        var payload = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
                        var dispatcher = new InlineDispatcher((request, cancel) =>
                            {
                                var response = new OutgoingResponse(request) { Payload = payload };
                                response.Use(writer => InvalidPipeWriter.Instance);
                                return new(response);
                            });
                        yield return new("response invalid writer", protocol, request, dispatcher, payload);
                    }

                    if (protocol.HasFields)
                    {
                        var request = new OutgoingRequest(new Proxy(protocol)) { IsOneway = isOneway };
                        var payload = new PayloadPipeReaderDecorator(InvalidPipeReader.Instance);
                        var dispatcher = new InlineDispatcher((request, cancel) =>
                            {
                                var response = new OutgoingResponse(request) { Payload = payload };
                                response.Fields = response.Fields.With(
                                        ResponseFieldKey.RetryPolicy,
                                        (ref SliceEncoder encoder) => throw new NotSupportedException("bogus header"));
                                return new(response);
                            });
                        yield return new("response invalid header", protocol, request, dispatcher, payload);
                    }
                }
            }
        }
    }

    /// <summary>Provides test case data for the <see cref="PayloadWriter_completed_on_request_and_response"> test. The
    /// test case data provides the outgoing request to send, the dipatcher to provide the response and the payload
    /// writer interceptor use to ensure <see cref="PipeWriter.Complete"/> is called.
    private static IEnumerable<TestCaseData> RequestsAndResponsesWithPayloadWriter
    {
        get
        {
            foreach (Protocol protocol in _protocols)
            {
                foreach (TestCaseData testCase in GetTestCaseData(protocol, isOneway: false))
                {
                    yield return testCase;
                }
                foreach (TestCaseData testCase in GetTestCaseData(protocol, isOneway: true))
                {
                    yield return testCase;
                }
            }

            // Only test invalid request/response payloads with the icerpc protocol. The ice protocol reads the
            // request/response payload before instantiating the payload writer.
            foreach (TestCaseData testCase in GetInvalidRequestResponseTestCaseData(Protocol.IceRpc, isOneway: true))
            {
                yield return testCase;
            }

            static IEnumerable<TestCaseData> GetTestCaseData(Protocol protocol, bool isOneway)
            {
                // Valid request
                {
                    var writerTaskSource = new TaskCompletionSource<PayloadPipeWriterDecorator>();
                    var request = new OutgoingRequest(new Proxy(protocol)) { IsOneway = isOneway };
                    request.Use(writer => PayloadInterceptor(writer, writerTaskSource));
                    var dispatcher = ConnectionOptions.DefaultDispatcher;
                    yield return new("request writer", protocol, request, dispatcher, writerTaskSource.Task);
                }

                // Valid response
                if (!isOneway)
                {
                    var writerTaskSource = new TaskCompletionSource<PayloadPipeWriterDecorator>();
                    var request = new OutgoingRequest(new Proxy(protocol)) { IsOneway = isOneway };
                    var payload = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
                    var dispatcher = new InlineDispatcher((request, cancel) =>
                        {
                            var response = new OutgoingResponse(request);
                            response.Use(writer => PayloadInterceptor(writer, writerTaskSource));
                            return new(response);
                        });
                    yield return new("response writer", protocol, request, dispatcher, writerTaskSource.Task);
                }
            }

            static IEnumerable<TestCaseData> GetInvalidRequestResponseTestCaseData(Protocol protocol, bool isOneway)
            {
                // Invalid request payload
                {
                    var writerTaskSource = new TaskCompletionSource<PayloadPipeWriterDecorator>();
                    var request = new OutgoingRequest(new Proxy(protocol))
                        {
                            Payload = InvalidPipeReader.Instance,
                            IsOneway = isOneway
                        };
                    request.Use(writer => PayloadInterceptor(writer, writerTaskSource));
                    var dispatcher = ConnectionOptions.DefaultDispatcher;
                    yield return new("invalid request writer", protocol, request, dispatcher, writerTaskSource.Task);
                }

                // Invalid response payload
                if (!isOneway)
                {
                    var writerTaskSource = new TaskCompletionSource<PayloadPipeWriterDecorator>();
                    var request = new OutgoingRequest(new Proxy(protocol)) { IsOneway = isOneway };
                    var dispatcher = new InlineDispatcher((request, cancel) =>
                        {
                            var response = new OutgoingResponse(request) { Payload = InvalidPipeReader.Instance };
                            response.Use(writer => PayloadInterceptor(writer, writerTaskSource));
                            return new(response);
                        });
                    yield return new("invalid response writer", protocol, request, dispatcher, writerTaskSource.Task);
                }
            }

            static PipeWriter PayloadInterceptor(
                PipeWriter writer,
                TaskCompletionSource<PayloadPipeWriterDecorator> source)
            {
                var payloadWriterDecorator = new PayloadPipeWriterDecorator(writer);
                source.SetResult(payloadWriterDecorator);
                return payloadWriterDecorator;
            }
        }
    }

    /// <summary>Provides test case data for the <see cref="PayloadWriter_completed_on_request_and_response"> test. The
    /// test case data provides the outgoing request to send, the dipatcher to provide the response and the payload
    /// writer interceptor use to ensure <see cref="PipeWriter.Complete"/> is called.
    private static IEnumerable<TestCaseData> RequestsAndResponsesExceptions
    {
        get
        {
            foreach (Protocol protocol in _protocols)
            {
                foreach (TestCaseData testCase in GetTestCaseData(protocol))
                {
                    yield return testCase;
                }
            }

            static IEnumerable<TestCaseData> GetTestCaseData(Protocol protocol)
            {
                // Invalid requests
                {
                    IDispatcher dispatcher = ConnectionOptions.DefaultDispatcher;
                    Type? exception = typeof(NotSupportedException);

                    {
                        var request = new OutgoingRequest(new Proxy(protocol)) { Payload = InvalidPipeReader.Instance };
                        yield return new("request invalid reader", protocol, request, dispatcher, exception);
                    }

                    {
                        var request = new OutgoingRequest(new Proxy(protocol));
                        request.Use(writer => InvalidPipeWriter.Instance);
                        yield return new("request invalid writer", protocol, request, dispatcher, exception);
                    }

                    if (protocol.HasFields)
                    {
                        var request = new OutgoingRequest(new Proxy(protocol));
                        request.Fields = request.Fields.With(
                            RequestFieldKey.Idempotent,
                            (ref SliceEncoder encoder) => throw new NotSupportedException("bogus header"));
                        yield return new("request invalid header", protocol, request, dispatcher, exception);
                    }
                }

                // Invalid responses
                {
                    // TODO: ConnectionLostException for the icerpc stream abort is bogus
                    Type? exception = typeof(ConnectionLostException);

                    {
                        var request = new OutgoingRequest(new Proxy(protocol));
                        var dispatcher = new InlineDispatcher((request, cancel) =>
                            new(new OutgoingResponse(request) { Payload = InvalidPipeReader.Instance }));
                        yield return new("response invalid reader", protocol, request, dispatcher, exception);
                    }

                    {
                        var request = new OutgoingRequest(new Proxy(protocol));
                        var dispatcher = new InlineDispatcher((request, cancel) =>
                            {
                                var response = new OutgoingResponse(request);
                                response.Use(writer => InvalidPipeWriter.Instance);
                                return new(response);
                            });
                        yield return new("response invalid writer", protocol, request, dispatcher, exception);
                    }

                    if (protocol.HasFields)
                    {
                        var request = new OutgoingRequest(new Proxy(protocol));
                        var dispatcher = new InlineDispatcher((request, cancel) =>
                            {
                                var response = new OutgoingResponse(request);
                                response.Fields = response.Fields.With(
                                        ResponseFieldKey.RetryPolicy,
                                        (ref SliceEncoder encoder) => throw new NotSupportedException("bogus header"));
                                return new(response);
                            });
                        yield return new("response invalid header", protocol, request, dispatcher, exception);
                    }
                }
            }
        }
    }

    public enum ConnectionType
    {
        Client,
        Server
    }

    /// <summary>Ensures that the connection HasInvocationInProgress and HasDispatchInProgress work.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Connection_has_invocation_and_dispatch_in_progress(Protocol protocol)
    {
        // Arrange
        var result = new TaskCompletionSource<bool>();
        (IProtocolConnection Client, IProtocolConnection Server)? sut = null;
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerConnectionOptions(new ConnectionOptions()
                {
                    Dispatcher = new InlineDispatcher((request, cancel) =>
                        {
                            result.SetResult(
                                sut!.Value.Client.HasInvocationsInProgress &&
                                sut!.Value.Server.HasDispatchesInProgress);
                            return new(new OutgoingResponse(request));
                        })
                })
            .BuildServiceProvider();

        sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        _ = sut.Value.Server.AcceptRequestsAsync();

        // Act
        await sut.Value.Client.SendRequestAsync(new OutgoingRequest(new Proxy(protocol)));

        // Assert
        Assert.That(await result.Task, Is.True);
    }

    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Dispose_the_protocol_connections(Protocol protocol)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection().UseProtocol(protocol).BuildServiceProvider();
        var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();

        // Act
        sut.Client.Dispose();
        sut.Server.Dispose();
    }

    /// <summary>Ensures that the connection fields are correctly exchanged on the protocol connection
    /// initialization.</summary>
    [Test]
    public async Task Initialize_peer_fields()
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .UseServerConnectionOptions(new()
            {
                Fields = new Dictionary<ConnectionFieldKey, OutgoingFieldValue>()
                        .With(ConnectionFieldKey.MaxHeaderSize, (ref SliceEncoder encoder) => encoder.EncodeInt(56))
            })
            .UseClientConnectionOptions(new()
            {
                Fields = new Dictionary<ConnectionFieldKey, OutgoingFieldValue>()
                        .With(ConnectionFieldKey.MaxHeaderSize, (ref SliceEncoder encoder) => encoder.EncodeInt(34))
                        .With((ConnectionFieldKey)10, (ref SliceEncoder encoder) => encoder.EncodeInt(38))
            })
            .BuildServiceProvider();

        // Act
        var sut = await serviceProvider.GetClientServerProtocolConnectionAsync(); // Initializes the connections

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(sut.Server.PeerFields, Has.Count.EqualTo(2));
            Assert.That(sut.Client.PeerFields, Has.Count.EqualTo(1));
            Assert.That(DecodeField(sut.Server.PeerFields, ConnectionFieldKey.MaxHeaderSize), Is.EqualTo(34));
            Assert.That(DecodeField(sut.Server.PeerFields, (ConnectionFieldKey)10), Is.EqualTo(38));
            Assert.That(DecodeField(sut.Client.PeerFields, ConnectionFieldKey.MaxHeaderSize), Is.EqualTo(56));
        });

        static int DecodeField(
            ImmutableDictionary<ConnectionFieldKey, ReadOnlySequence<byte>> fields,
            ConnectionFieldKey key) =>
            fields.DecodeValue(key, (ref SliceDecoder decoder) => decoder.DecodeInt());
    }

    /// <summary>Ensures that the PeerShutdownInitiated callback is called when the peer initiates the
    /// shutdown.</summary>
    [Test, TestCaseSource(nameof(ClientServerConnections))]
    public async Task PeerShutdownInitiated_callback_is_called(Protocol protocol, ConnectionType connectionType)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection().UseProtocol(protocol).BuildServiceProvider();
        var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();

        IProtocolConnection connection1 = connectionType == ConnectionType.Client ? sut.Server : sut.Client;
        IProtocolConnection connection2 = connectionType == ConnectionType.Client ? sut.Client : sut.Server;

        var shutdownInitiatedCalled = new TaskCompletionSource<string>();
        connection2.PeerShutdownInitiated = message =>
            {
                shutdownInitiatedCalled.SetResult(message);
                _ = connection2.ShutdownAsync("");
            };

        // Act
        await connection1.ShutdownAsync("hello world");

        // Assert
        string message = protocol == Protocol.Ice ? "connection shutdown by peer" : "hello world";
        Assert.That(await shutdownInitiatedCalled.Task, Is.EqualTo(message));
    }

    /// <summary>Ensures that the sending a request after shutdown fails.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task SendRequest_on_shutdown_connection_fails(Protocol protocol)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection().UseProtocol(protocol).BuildServiceProvider();

        var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        _ = sut.Client.ShutdownAsync("");
        _ = sut.Server.ShutdownAsync("");

        // Act
        Task<IncomingResponse> sendRequestTask = sut.Client.SendRequestAsync(new OutgoingRequest(new Proxy(protocol)));

        // Assert
        Assert.ThrowsAsync<ConnectionClosedException>(async () => await sendRequestTask);
    }

    /// <summary>Ensures that the failure to read or write the payload raises the expected exception.</summary>
    [Test, TestCaseSource(nameof(RequestsAndResponsesExceptions))]
    public async Task SendRequest_failure_throws_expected_exception(
        string name,
        Protocol protocol,
        OutgoingRequest request,
        IDispatcher dispatcher,
        Type expectedType)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerConnectionOptions(new ConnectionOptions() { Dispatcher = dispatcher })
            .BuildServiceProvider();
        var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        _ = sut.Server.AcceptRequestsAsync();

        // Act
        Task sendRequestTask = sut.Client.SendRequestAsync(request);

        // Assert
        Exception? exception = Assert.CatchAsync<Exception>(() => sendRequestTask);
        Assert.That(exception?.GetType(), Is.EqualTo(expectedType));
    }

    /// <summary>Ensures that the request or response payload is always completed.</summary>
    [Test, TestCaseSource(nameof(RequestsAndResponses))]
    public async Task Payload_completed_on_request_and_response(
        string name,
        Protocol protocol,
        OutgoingRequest request,
        IDispatcher dispatcher,
        PayloadPipeReaderDecorator payload)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerConnectionOptions(new ConnectionOptions() { Dispatcher = dispatcher })
            .BuildServiceProvider();
        var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        _ = sut.Server.AcceptRequestsAsync();

        // Act
        _ = sut.Client.SendRequestAsync(request);

        // Assert
        Assert.That(await payload.CompleteCalled, Is.True);
    }

    /// <summary>Ensures that the request or response payload writer is always completed.</summary>
    [Test, TestCaseSource(nameof(RequestsAndResponsesWithPayloadWriter))]
    public async Task PayloadWriter_completed_on_request_and_response(
        string name,
        Protocol protocol,
        OutgoingRequest request,
        IDispatcher dispatcher,
        Task<PayloadPipeWriterDecorator> payloadWriterTask)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerConnectionOptions(new ConnectionOptions() { Dispatcher = dispatcher })
            .BuildServiceProvider();
        var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        _ = sut.Server.AcceptRequestsAsync();

        // Act
        _ = sut.Client.SendRequestAsync(request);
        PayloadPipeWriterDecorator payloadWriter = await payloadWriterTask;

        // Assert
        Assert.That(await payloadWriter.CompleteCalled, Is.True);
    }

    public sealed class PayloadPipeReaderDecorator : PipeReader
    {
        internal Task<bool> CompleteCalled => _completeCalled.Task;

        private readonly PipeReader _decoratee;
        private readonly TaskCompletionSource<bool> _completeCalled =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void AdvanceTo(SequencePosition consumed) => _decoratee.AdvanceTo(consumed);

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
            _decoratee.AdvanceTo(consumed, examined);

        public override void CancelPendingRead() => _decoratee.CancelPendingRead();

        public override void Complete(Exception? exception = null)
        {
            _completeCalled.SetResult(true);
            _decoratee.Complete(exception);
        }

        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
            _decoratee.ReadAsync(cancellationToken);

        public override bool TryRead(out ReadResult result) => _decoratee.TryRead(out result);

        internal PayloadPipeReaderDecorator(PipeReader decoratee) => _decoratee = decoratee;
    }

    public sealed class PayloadPipeWriterDecorator : PipeWriter
    {
        internal Task<bool> CompleteCalled => _completeCalled.Task;

        private readonly PipeWriter _decoratee;
        private readonly TaskCompletionSource<bool> _completeCalled =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void CancelPendingFlush() => _decoratee.CancelPendingFlush();

        public override void Complete(Exception? exception = null)
        {
            _completeCalled.SetResult(true);
            _decoratee.Complete(exception);
        }

        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default) =>
            _decoratee.FlushAsync(cancellationToken);

        public override void Advance(int bytes) =>
            _decoratee.Advance(bytes);

        public override Memory<byte> GetMemory(int sizeHint = 0) =>
            _decoratee.GetMemory(sizeHint);

        public override Span<byte> GetSpan(int sizeHint = 0) =>
            _decoratee.GetSpan(sizeHint);

        internal PayloadPipeWriterDecorator(PipeWriter decoratee) => _decoratee = decoratee;
    }
}

internal class ProtocolServiceCollection : TransportServiceCollection
{
    public ProtocolServiceCollection()
    {
        this.AddSingleton(IceProtocol.Instance.ProtocolConnectionFactory);
        this.AddSingleton(IceRpcProtocol.Instance.ProtocolConnectionFactory);
    }
}

internal static class ProtocolServiceCollectionExtensions
{
    internal static IServiceCollection UseProtocol(this IServiceCollection collection, Protocol protocol) =>
        collection.AddSingleton(protocol);

    internal static IServiceCollection UseServerConnectionOptions(
        this IServiceCollection collection,
        ConnectionOptions options) =>
        collection.AddSingleton(new ServerConnectionOptions(options));

    internal static IServiceCollection UseClientConnectionOptions(
        this IServiceCollection collection,
        ConnectionOptions options) =>
        collection.AddSingleton(new ClientConnectionOptions(options));

    internal static Task<IProtocolConnection> GetClientProtocolConnectionAsync(
        this IServiceProvider serviceProvider) => serviceProvider.GetRequiredService<Protocol>() == Protocol.Ice ?
         GetProtocolConnectionAsync(serviceProvider, false, serviceProvider.GetSimpleClientConnectionAsync) :
         GetProtocolConnectionAsync(serviceProvider, false, serviceProvider.GetMultiplexedClientConnectionAsync);

    internal static Task<IProtocolConnection> GetServerProtocolConnectionAsync(
        this IServiceProvider serviceProvider) => serviceProvider.GetRequiredService<Protocol>() == Protocol.Ice ?
         GetProtocolConnectionAsync(serviceProvider, true, serviceProvider.GetSimpleServerConnectionAsync) :
         GetProtocolConnectionAsync(serviceProvider, true, serviceProvider.GetMultiplexedServerConnectionAsync);

    internal static async Task<(IProtocolConnection Client, IProtocolConnection Server)> GetClientServerProtocolConnectionAsync(
        this IServiceProvider serviceProvider)
    {
        Task<IProtocolConnection> serverTask = serviceProvider.GetServerProtocolConnectionAsync();
        IProtocolConnection clientConnection = await serviceProvider.GetClientProtocolConnectionAsync();
        IProtocolConnection serverConnection = await serverTask;
        return (clientConnection, serverConnection);
    }

    private static async Task<IProtocolConnection> GetProtocolConnectionAsync<T>(
        IServiceProvider serviceProvider,
        bool isServer,
        Func<Task<T>> networkConnectionFactory) where T : INetworkConnection =>
        await serviceProvider.GetRequiredService<IProtocolConnectionFactory<T>>().CreateProtocolConnectionAsync(
            await networkConnectionFactory(),
            connectionInformation: new(),
            connection: null!,
            connectionOptions: isServer ?
                serviceProvider.GetService<ServerConnectionOptions>()?.Value ?? new() :
                serviceProvider.GetService<ClientConnectionOptions>()?.Value ?? new(),
            isServer,
            CancellationToken.None);

    private sealed class ClientConnectionOptions
    {
        internal ConnectionOptions Value { get; }

        internal ClientConnectionOptions(ConnectionOptions options) => Value = options;
    }

    private sealed class ServerConnectionOptions
    {
        internal ConnectionOptions Value { get; }

        internal ServerConnectionOptions(ConnectionOptions options) => Value = options;
    }
}
