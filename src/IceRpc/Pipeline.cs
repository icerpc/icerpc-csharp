// Copyright (c) ZeroC, Inc.

namespace IceRpc;

/// <summary>A pipeline is an invoker created from zero or more interceptors installed by calling
/// <see cref="Use(Func{IInvoker, IInvoker})"/>, and a final invoker installed by calling <see cref="Into(IInvoker)"/>.
/// Requests using this pipeline flow through the interceptors into the final invoker. The final invoker then sends the
/// request over a connection.</summary>
/// <example>
/// The following example demonstrates how an application would typically create the pipeline and use it as the invoker
/// for a proxy.
/// <code source="../../docfx/examples/IceRpc.Examples/PipelineExamples.cs" region="CreatingAndUsingThePipeline" lang="csharp" />
/// You can easily create your own interceptor and add it to the pipeline. The next example shows how you can create an
/// interceptor using an <see cref="InlineInvoker"/> and add it to the pipeline with
/// <see cref="Use(Func{IInvoker, IInvoker})"/>.
/// <code source="../../docfx/examples/IceRpc.Examples/PipelineExamples.cs" region="UseWithInlineInterceptor" lang="csharp" />
/// </example>
/// <seealso cref="Router"/>
/// <seealso href="https://docs.icerpc.dev/icerpc/invocation/invocation-pipeline"/>
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
