// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

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
    public async Task Abort_cancels_dispatches()
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);
        var tcs = new TaskCompletionSource();
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .UseServerOptions(new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions
                {
                    Dispatcher = new InlineDispatcher(async (request, cancel) =>
                    {
                        start.Release();
                        try
                        {
                            await hold.WaitAsync(cancel);
                        }
                        catch (OperationCanceledException)
                        {
                            tcs.SetResult();
                        }
                        return new OutgoingResponse(request);
                    })
                }
            })
            .BuildServiceProvider();

        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();

        _ = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.IceRpc)), InvalidConnection.IceRpc);

        await start.WaitAsync(); // Wait for the dispatch to start

        // Act
        sut.Server.Abort(new ConnectionAbortedException());

        // Assert
        Assert.That(async () => await tcs.Task, Throws.Nothing);
        hold.Release();
    }

    /// <summary>Verifies that if the shutdown cancellation cancels pending invocations and dispatches.</summary>
    [Test]
    public async Task Shutdown_cancellation_cancels_invocations_and_dispatches()
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);
        var tcs = new TaskCompletionSource();

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .UseServerOptions(new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions
                {
                    Dispatcher = new InlineDispatcher(async (request, cancel) =>
                    {
                        start.Release();
                        try
                        {
                            await hold.WaitAsync(cancel);
                        }
                        catch (OperationCanceledException)
                        {
                            tcs.SetResult();
                        }
                        return new OutgoingResponse(request);
                    })
                }
            })
            .BuildServiceProvider();

        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();

        sut.Server.PeerShutdownInitiated = message => _ = sut.Server.ShutdownAsync(message);

        var invokeTask = sut.Client.InvokeAsync(
            new OutgoingRequest(new Proxy(Protocol.IceRpc)),
            InvalidConnection.IceRpc);

        await start.WaitAsync(); // Wait for the dispatch to start

        // Act
        Task shutdownTask = sut.Client.ShutdownAsync("", new CancellationToken(canceled: true));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(async () => await invokeTask, Throws.TypeOf<OperationCanceledException>());
            Assert.That(async () => await shutdownTask, Throws.Nothing);
            Assert.That(async () => await tcs.Task, Throws.Nothing);
        });
        hold.Release();
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

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .UseServerOptions(new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher }
            })
            .BuildServiceProvider();

        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc));

        // Act
        var response = await sut.Client.InvokeAsync(request, InvalidConnection.IceRpc);

        // Assert
        Assert.That(response.ResultType, Is.EqualTo(ResultType.Failure));
        var exception = await response.DecodeFailureAsync(request) as DispatchException;
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.ErrorCode, Is.EqualTo(errorCode));
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

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .UseServerOptions(new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher }
            })
            .BuildServiceProvider();
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();

        // Act
        _ = sut.Client.InvokeAsync(
            new OutgoingRequest(new Proxy(Protocol.IceRpc)),
            InvalidConnection.IceRpc);

        // Assert
        Assert.That(await payloadDecorator.Completed, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>Ensures that the request payload stream is completed on valid request.</summary>
    [Test]
    public async Task PayloadStream_completed_on_valid_request([Values(true, false)] bool isOneway)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .BuildServiceProvider();
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();

        var payloadStreamDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc))
        {
            IsOneway = isOneway,
            PayloadStream = payloadStreamDecorator
        };

        // Act
        _ = sut.Client.InvokeAsync(request, InvalidConnection.IceRpc);

        // Assert
        Assert.That(await payloadStreamDecorator.Completed, Is.Null);
    }

    /// <summary>Ensures that the request payload is completed if the payload stream is invalid.</summary>
    [Test]
    public async Task PayloadStream_completed_on_invalid_request_payload([Values(true, false)] bool isOneway)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .BuildServiceProvider();
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();

        var payloadStreamDecorator = new PayloadPipeReaderDecorator(InvalidPipeReader.Instance);
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc))
        {
            IsOneway = isOneway,
            PayloadStream = payloadStreamDecorator
        };

        // Act
        _ = sut.Client.InvokeAsync(request, InvalidConnection.IceRpc);

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

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .UseServerOptions(new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher }
            })
            .BuildServiceProvider();
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.IceRpc)), InvalidConnection.IceRpc);

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

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .UseServerOptions(new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher }
            })
            .BuildServiceProvider();
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.IceRpc)), InvalidConnection.IceRpc);

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
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .BuildServiceProvider();
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();

        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc))
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
        _ = sut.Client.InvokeAsync(request, InvalidConnection.IceRpc);

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

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .UseServerOptions(new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher }
            })
            .BuildServiceProvider();
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.IceRpc)), InvalidConnection.IceRpc);

        // Assert
        Assert.That(await (await payloadWriterSource.Task).Completed, Is.InstanceOf<NotSupportedException>());
    }

    [Test]
    public async Task Request_with_header_size_larger_than_max_header_size_fails()
    {
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .UseServerOptions(new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { MaxIceRpcHeaderSize = 100 }
            })
            .BuildServiceProvider();
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();

        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc))
        {
            Operation = new string('x', 100)
        };

        Assert.That(
            async () => await sut.Client.InvokeAsync(request, InvalidConnection.IceRpc),
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
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .UseServerOptions(new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher }
            })
            .BuildServiceProvider();
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();

        var payloadDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc));

        // Act
        var response = await sut.Client.InvokeAsync(request, InvalidConnection.IceRpc);

        // Assert
        Assert.That(
            response.Fields.DecodeValue((ResponseFieldKey)1000, (ref SliceDecoder decoder) => decoder.DecodeString()),
            Is.EqualTo(expectedValue));
    }

    /// <summary>With icerpc protocol the connection shutdown waits for invocations to finish. This is different
    /// with ice protocol see <see cref="IceProtocolConnectionTests.Shutdown_cancels_invocations"/>.</summary>
    [Test]
    public async Task Shutdown_waits_for_pending_invocations_to_finish()
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .UseServerOptions(new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions
                {
                    Dispatcher = new InlineDispatcher(async (request, cancel) =>
                    {
                        start.Release();
                        await hold.WaitAsync(cancel);
                        return new OutgoingResponse(request);
                    })
                }
            })
            .BuildServiceProvider();

        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();

        sut.Server.PeerShutdownInitiated = message => _ = sut.Server.ShutdownAsync(message);

        var invokeTask = sut.Client.InvokeAsync(
            new OutgoingRequest(new Proxy(Protocol.IceRpc)),
            InvalidConnection.IceRpc);

        await start.WaitAsync(); // Wait for the dispatch to start

        // Act
        var shutdownTask = sut.Client.ShutdownAsync("");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(invokeTask.IsCompleted, Is.False);
            Assert.That(shutdownTask.IsCompleted, Is.False);
        });
        hold.Release();
        Assert.Multiple(() =>
        {
            Assert.That(async () => await invokeTask, Throws.Nothing);
            Assert.That(async () => await shutdownTask, Throws.Nothing);
        });
    }
}
