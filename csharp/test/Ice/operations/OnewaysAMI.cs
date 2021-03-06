// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IceRpc.Test;

namespace IceRpc.Test.Operations
{
    public static class OnewaysAMI
    {
        private class CallbackBase
        {
            private bool _called;
            private readonly object _mutex = new();

            public virtual void Check()
            {
                lock (_mutex)
                {
                    while (!_called)
                    {
                        Monitor.Wait(_mutex);
                    }
                    _called = false;
                }
            }

            public virtual void Called()
            {
                lock (_mutex)
                {
                    TestHelper.Assert(!_called);
                    _called = true;
                    Monitor.Pulse(_mutex);
                }
            }

            internal CallbackBase() => _called = false;
        }

        private class Callback : CallbackBase
        {
            public void Sent() => Called();
        }

        internal static void Run(TestHelper helper, IMyClassPrx proxy)
        {
            Communicator communicator = helper.Communicator;
            IMyClassPrx p = proxy.Clone(oneway: true);

            {
                var cb = new Callback();
                p.IcePingAsync(progress: new Progress<bool>(sentSynchronously => cb.Sent()));
                cb.Check();
            }

            bool b = p.IceIsAAsync("::IceRpc::Test::Operations::MyClass").Result;
            string id = p.IceIdAsync().Result;
            string[] ids = p.IceIdsAsync().Result;

            {
                var cb = new Callback();
                p.OpVoidAsync(progress: new Progress<bool>(sentSynchronously => cb.Sent()));
                cb.Check();
            }

            {
                var cb = new Callback();
                p.OpIdempotentAsync(progress: new Progress<bool>(sentSynchronously => cb.Sent()));
                cb.Check();
            }

            {
                var cb = new Callback();
                p.OpOnewayAsync(progress: new Progress<bool>(sentSynchronously => cb.Sent()));
                cb.Check();
            }

            {
                var cb = new Callback();
                p.OpOnewayMetadataAsync(progress: new Progress<bool>(sentSynchronously => cb.Sent()));
                cb.Check();
            }

            (byte ReturnValue, byte p3) = p.OpByteAsync(0xff, 0x0f).Result;

            Task.Run(async () =>
            {
                if (p.Protocol == Protocol.Ice1)
                {
                    return;
                }

                foreach (int size in new List<int>() { 32, 1024, 1024 * 1024 })
                {
                    byte[] buffer = new byte[size];
                    for (int i = 0; i < buffer.Length; ++i)
                    {
                        buffer[i] = (byte)(i % 256);
                    }

                    var streams = new List<MemoryStreamWithDisposeCheck>();
                    try
                    {
                        streams.Add(new MemoryStreamWithDisposeCheck(buffer));
                        await p.OpSendStream1Async(streams.Last()).ConfigureAwait(false);
                        streams.Last().WaitForDispose();

                        streams.Add(new MemoryStreamWithDisposeCheck(buffer));
                        await p.OpSendStream2Async(buffer.Length, streams.Last()).ConfigureAwait(false);
                        streams.Last().WaitForDispose();
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"test failed: {ex}");
                        throw;
                    }
                    finally
                    {
                        foreach (Stream stream in streams)
                        {
                            try
                            {
                                stream.ReadByte();
                                TestHelper.Assert(false);
                            }
                            catch (ObjectDisposedException)
                            {
                            }
                        }
                    }
                }
            }).Wait();
        }
    }
}
