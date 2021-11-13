// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO;

namespace IceRpc.Internal
{
    internal static class StreamExtensions
    {
        public static async ValueTask ReadUntilFullAsync(
            this Stream stream,
            Memory<byte> buffer,
            CancellationToken cancel)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int count = await stream.ReadAsync(buffer[read..], cancel).ConfigureAwait(false);
                if (count == 0) // end of stream
                {
                    throw new InvalidDataException(
                        $"reached end of stream while expecting {buffer.Length - read} more bytes");
                }
                read += count;
            }
        }
    }
}
