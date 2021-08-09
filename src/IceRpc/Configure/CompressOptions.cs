// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Configure
{
    /// <summary>Options class to configure the <see cref="CompressorInterceptor"/> and
    /// <see cref="CompressorMiddleware"/>.</summary>
    public sealed class CompressOptions
    {
        /// <summary>The compression level for the compress operation, the default value is
        /// <see cref="CompressionLevel.Fastest"/>.</summary>
        public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Fastest;
        /// <summary>The minimum size in bytes of the payload to which apply compression. The default value
        /// is 500.</summary>
        public int CompressionMinSize { get; set; } = 500;
        /// <summary>Whether or not to apply compression to the 2.0 encoded payload of a request or response when
        /// <see cref="Features.CompressPayload.Yes"/> is present in the request features or response features
        /// respectively. The default value is <c>true</c>.</summary>
        public bool CompressPayload { get; set; } = true;
        /// <summary>Whether or not to decompress the compressed request or response payload. The default value is
        /// <c>true</c>.</summary>
        public bool DecompressPayload { get; set; } = true;

        internal static CompressOptions Default = new();
    }
}
