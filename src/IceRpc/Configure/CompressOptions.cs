// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Compression;

namespace IceRpc.Configure
{
    /// <summary>Options class to configure the <see cref="CompressorInterceptor"/> and
    /// <see cref="CompressorMiddleware"/>.</summary>
    public sealed record class CompressOptions
    {
        /// <summary>Gets or sets the compression level for the compress operation.</summary>
        /// <value> The default value is <see cref="CompressionLevel.Fastest"/>. </value>
        public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Fastest;

        /// <summary>Gets or sets whether or not to compress the request or response payload.</summary>
        /// <value>When <c>true</c>, compress the payload if it is not compressed yet. When <c>false</c>, do not
        /// compress the payload. The default value is <c>true</c>.</value>
        public bool CompressPayload { get; set; } = true;

        /// <summary>Gets or sets whether or not to decompress the compressed request or response payload. </summary>
        /// <value>When <c>true</c>, decompress the payload if it is compressed. When <c>false</c>, do not
        /// decompress the payload. The default value is <c>true</c>.</value>
        public bool DecompressPayload { get; set; } = true;
    }
}
