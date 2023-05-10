// Copyright (c) ZeroC, Inc.

namespace IceRpc;

/// <summary>A pipeline is an invoker created from zero or more interceptors installed by calling <see cref="Use"/>,
/// and a final invoker installed by calling <see cref="Into"/>.</summary>
/// <example>
/// The following code demonstrates how an application would typically create the pipeline and use it as the invoker
/// for a proxy.
/// <code source="../../docfx/examples/IceRpc.Examples/PipelineExamples.cs" region="CreatingAndUsingThePipeline" lang="csharp" />
/// In the example above, the logger and retry interceptors are added to the pipeline, but the pipeline allows for the addition
/// of any <see cref="IInvoker"/> implementation using <see cref="Use(Func{IInvoker, IInvoker})"/>.
/// The next code example shows how you can add an <see cref="InlineInvoker"/> to the pipeline.
/// <code source="../../docfx/examples/IceRpc.Examples/PipelineExamples.cs" region="UseWithInlineInvoker" lang="csharp" />
/// </example>
/// <remarks>A pipeline allows you to compose a chain of invokers through which requests flow into the final invoker.
/// The final invoker is responsible for sending the request to the peer. A typical IceRPC application uses either the
/// <see cref="ClientConnection"/> or the <see cref="ConnectionCache"/> as the final invoker. However, it is also
/// possible to use your custom final invoker to handle the request transmission.</remarks>
/// <seealso cref="InlineInvoker"/>
/// <seealso cref="Router"/>
public sealed class Pipeline : IInvoker
{
    private readonly Stack<Func<IInvoker, IInvoker>> _interceptorStack = new();
    private readonly Lazy<IInvoker> _invoker;
    private IInvoker? _lastInvoker;

    /// <summary>Constructs a pipeline.</summary>
    public Pipeline() => _invoker = new Lazy<IInvoker>(CreateInvokerPipeline);

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken = default) =>
        _invoker.Value.InvokeAsync(request, cancellationToken);

    /// <summary>Sets the last invoker of this pipeline. The pipeline flows into this invoker.</summary>
    /// <param name="lastInvoker">The last invoker.</param>
    /// <returns>This pipeline.</returns>
    /// <exception cref="InvalidOperationException">Thrown if this method is called after the first call to
    /// <see cref="InvokeAsync" />.</exception>
    public Pipeline Into(IInvoker lastInvoker)
    {
        if (_invoker.IsValueCreated)
        {
            throw new InvalidOperationException($"{nameof(Into)} must be called before {nameof(InvokeAsync)}.");
        }

        _lastInvoker = lastInvoker;
        return this;
    }

    /// <summary>Installs an interceptor at the end of the pipeline.</summary>
    /// <param name="interceptor">The interceptor to install.</param>
    /// <returns>This pipeline.</returns>
    /// <exception cref="InvalidOperationException">Thrown if this method is called after the first call to
    /// <see cref="InvokeAsync" />.</exception>
    public Pipeline Use(Func<IInvoker, IInvoker> interceptor)
    {
        if (_invoker.IsValueCreated)
        {
            throw new InvalidOperationException(
                $"The interceptors must be installed before the first call to {nameof(InvokeAsync)}.");
        }
        _interceptorStack.Push(interceptor);
        return this;
    }

    /// <summary>Creates a pipeline of invokers by starting with the last invoker installed. This method is called
    /// by the first call to <see cref="InvokeAsync" />.</summary>
    /// <returns>The pipeline of invokers.</returns>
    private IInvoker CreateInvokerPipeline()
    {
        if (_lastInvoker is null)
        {
            throw new InvalidOperationException(
                $"{nameof(Into)} must be called before calling {nameof(InvokeAsync)} on a Pipeline.");
        }

        IInvoker pipeline = _lastInvoker;

        foreach (Func<IInvoker, IInvoker> interceptor in _interceptorStack)
        {
            pipeline = interceptor(pipeline);
        }
        return pipeline;
    }
}
