// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Tests
{
    public static class PipeReaderExtensions
    {
        /// <summary>Reads a reader until it's completed or canceled.</summary>
        public static async ValueTask<ReadResult> ReadAllAsync(this PipeReader reader, CancellationToken cancel)
        {
            while (true)
            {
                ReadResult readResult = await reader.ReadAsync(cancel);

                if (readResult.IsCompleted || readResult.IsCanceled)
                {
                    return readResult;
                }
                else
                {
                    // Can't advance examined more without giving "examined" to the caller.
                    reader.AdvanceTo(readResult.Buffer.Start);
                    await Task.Yield(); // avoid tight loop
                }
            }
        }
    }
}
