// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary>An option class to customize the decoding of a request or response payloads.</summary>
    public record class DecodePayloadOptions
    {
        /// <summary>The default decode payload options.</summary>
        public static DecodePayloadOptions Default { get; } = new();

        /// <summary>The activator to use when decoding Slice classes, exceptions and traits. When <c>null</c>, the
        /// decoding of a request or response payload uses the activator injected by the Slice generated code.</summary>
        public IActivator? Activator { get; init; }

        /// <summary>The maximum depth when decoding a type recursively.</summary>
        /// <value>A value greater than 0, or <c>-1</c> for the default value.</value>
        public int MaxDepth
        {
            get => _maxDepth;
            init => _maxDepth = value is -1 or > 0 ? value :
                throw new ArgumentException("value must be -1 or greater than 0", nameof(MaxDepth));
        }

        /// <summary>The invoker assigned to decoded proxies. When null, a proxy decoded from an incoming request gets
        /// <see cref="Proxy.DefaultInvoker"/> while a proxy decoded from an incoming response gets the invoker of the
        /// proxy that created the request.</summary>
        public IInvoker? ProxyInvoker { get; init; }

        /// <summary>The options for decoding a Slice stream.</summary>
        public StreamDecoderOptions StreamDecoderOptions { get; init; } = StreamDecoderOptions.Default;

        private int _maxDepth = -1;
    }
}
