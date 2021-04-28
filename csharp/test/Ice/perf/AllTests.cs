// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using IceRpc.Test;

namespace IceRpc.Test.Perf
{
    public static class AllTests
    {
        private const int ByteSeqSize = 1024000; // 1MB

        public static async Task RunTestAsync(
            System.IO.TextWriter output,
            int repetitions,
            string name,
            Func<Task> invocationAsync,
            Func<Task> warmUpInvocationAsync)
        {
            output.Write($"testing {name}... ");
            output.Flush();
            for (int i = 0; i < 1000; i++)
            {
                await warmUpInvocationAsync();
            }

            var watch = new Stopwatch();
            var collections = new int[GC.MaxGeneration].Select((v, i) => GC.CollectionCount(i)).ToArray();
            watch.Start();
            for (int i = 0; i < repetitions; i++)
            {
                await invocationAsync();
            }
            collections = collections.Select((v, i) => GC.CollectionCount(i) - v).ToArray();
            GC.Collect();
            watch.Stop();
            output.WriteLine($"{watch.ElapsedMilliseconds / (float)repetitions}ms, " +
                $"{string.Join("/", collections)} ({string.Join("/", collections.Select((_, i) => $"gen{i}"))}) GCs");
        }

        public static Task RunTestAsync(
            System.IO.TextWriter output,
            int repetitions,
            string name,
            Func<Task> invocationAsync) =>
            RunTestAsync(output, repetitions, name, invocationAsync, invocationAsync);

        public static Task RunTestAsync<T>(
            System.IO.TextWriter output,
            int repetitions,
            string name,
            Func<ReadOnlyMemory<T>, Task> invocationAsync,
            int size) where T : struct
        {
            var seq = new T[size];
            T[] emptySeq = Array.Empty<T>();
            return RunTestAsync(output, repetitions, name, () => invocationAsync(seq), () => invocationAsync(emptySeq));
        }

        public static Task RunTestAsync<T>(
            System.IO.TextWriter output,
            int repetitions,
            string name,
            Func<int, Task<T[]>> invocationAsync, int size) where T : struct
        {
            return RunTestAsync(output, repetitions, name, () => invocationAsync(size), () => invocationAsync(0));
        }

        public static async Task RunAsync(TestHelper helper)
        {
            Communicator communicator = helper.Communicator!;

            System.IO.TextWriter output = helper.Output;

#if DEBUG
            output.WriteLine("warning: performance test built with DEBUG");
#endif

            var perf = IPerformancePrx.Parse(helper.GetTestProxy("perf", 0), communicator);

            await RunTestAsync(output, 10000, "latency", async () => await perf.IcePingAsync());
            await RunTestAsync<byte>(output, 1000, "sending byte sequence", v => perf.SendBytesAsync(v), ByteSeqSize);
            await RunTestAsync<byte>(output, 1000, "received byte sequence", sz => perf.ReceiveBytesAsync(sz), ByteSeqSize);

            await perf.ShutdownAsync();
        }
    }
}
