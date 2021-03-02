// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Interceptor
{
    public class RetryException : Exception
    {
    }

    public static class DispatchInterceptors
    {
        public static AsyncLocal<int> LocalContext { get; } = new();

        public static async ValueTask ActivateAsync(Server server)
        {
            server.Use(async (current, next, cancel) =>
            {
                if (current.Context.TryGetValue("raiseBeforeDispatch", out string? context))
                {
                    if (context == "invalidInput")
                    {
                        throw new InvalidInputException("intercept");
                    }
                    else if (context == "notExist")
                    {
                        throw new ServiceNotFoundException();
                    }
                }

                OutgoingResponseFrame response = await next();

                if (current.Context.TryGetValue("raiseAfterDispatch", out context))
                {
                    if (context == "invalidInput")
                    {
                        throw new InvalidInputException("raiseAfterDispatch");
                    }
                    else if (context == "notExist")
                    {
                        throw new ServiceNotFoundException();
                    }
                }

                return response;
            });

            server.Use(async (current, next, cancel) =>
            {
                    if (current.Operation == "addWithRetry")
                    {
                        for (int i = 0; i < 10; ++i)
                        {
                            try
                            {
                                await next();
                                TestHelper.Assert(false);
                            }
                            catch (RetryException)
                            {
                                // Expected, retry
                            }
                        }
                        current.Context["retry"] = "no";
                    }
                    return await next();
            });

            server.Use(async (current, next, cancel) =>
                {
                    if (current.Context.TryGetValue("retry", out string? context) && context.Equals("yes"))
                    {
                        // Retry the dispatch to ensure that abandoning the result of the dispatch works fine and is
                        // thread-safe
                        ValueTask<OutgoingResponseFrame> vt1 = next();
                        ValueTask<OutgoingResponseFrame> vt2 = next();
                        await vt1;
                        return await vt2;
                    }
                    return await next();
                });

            server.Use(async (current, next, cancel) =>
                {
                    if (current.Operation == "opWithBinaryContext" && current.Protocol == Protocol.Ice2)
                    {
                        Debug.Assert(current.BinaryContext.ContainsKey(3));
                        short size = current.BinaryContext[3].Read(istr => istr.ReadShort());
                        var t2 = new Token(1, "mytoken", Enumerable.Range(0, size).Select(i => (byte)2).ToArray());
                        Debug.Assert(current.BinaryContext.ContainsKey(1));
                        Token t1 = current.BinaryContext[1].Read(Token.IceReader);
                        TestHelper.Assert(t1.Hash == t2.Hash);
                        TestHelper.Assert(t1.Expiration == t2.Expiration);
                        TestHelper.Assert(t1.Payload.SequenceEqual(t2.Payload));
                        Debug.Assert(current.BinaryContext.ContainsKey(2));
                        string[] s2 = current.BinaryContext[2].Read(istr =>
                            istr.ReadArray(1, InputStream.IceReaderIntoString));
                        TestHelper.Assert(Enumerable.Range(0, 10).Select(i => $"string-{i}").SequenceEqual(s2));

                        if (current.IncomingRequestFrame.HasCompressedPayload)
                        {
                            current.IncomingRequestFrame.DecompressPayload();

                            Debug.Assert(current.BinaryContext.ContainsKey(3));
                            size = current.BinaryContext[3].Read(istr => istr.ReadShort());

                            Debug.Assert(current.BinaryContext.ContainsKey(1));
                            t1 = current.BinaryContext[1].Read(Token.IceReader);
                            t2 = current.IncomingRequestFrame.ReadArgs(current.Connection, Token.IceReader);
                            TestHelper.Assert(t1.Hash == t2.Hash);
                            TestHelper.Assert(t1.Expiration == t2.Expiration);
                            TestHelper.Assert(t1.Payload.SequenceEqual(t2.Payload));
                            Debug.Assert(current.BinaryContext.ContainsKey(2));
                            s2 = current.BinaryContext[2].Read(istr =>
                                istr.ReadArray(1, InputStream.IceReaderIntoString));
                            TestHelper.Assert(Enumerable.Range(0, 10).Select(i => $"string-{i}").SequenceEqual(s2));
                        }
                    }
                    return await next();
                });

            server.Use(async (current, next, cancel) =>
                {
                    if (current.Operation == "op1")
                    {
                        LocalContext.Value = int.Parse(current.Context["local-user"]);
                        if (current.Protocol == Protocol.Ice2)
                        {
                            OutgoingResponseFrame response = await next();
                            response.BinaryContextOverride.Add(110, ostr => ostr.WriteInt(110));
                            response.BinaryContextOverride.Add(120, ostr => ostr.WriteInt(120));
                            return response;
                        }
                    }
                    return await next();
                });

            await server.ActivateAsync();
        }
    }
}
