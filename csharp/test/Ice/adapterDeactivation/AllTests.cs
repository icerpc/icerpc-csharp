// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.AdapterDeactivation
{
    public static class AllTests
    {
        public static async Task RunAsync(TestHelper helper)
        {
            Communicator communicator = helper.Communicator;

            bool ice1 = TestHelper.GetTestProtocol(communicator.GetProperties()) == Protocol.Ice1;
            TextWriter output = helper.Output;
            output.Write("testing stringToProxy... ");
            output.Flush();
            var obj = ITestIntfPrx.Parse(helper.GetTestProxy("test", 0), communicator);
            output.WriteLine("ok");

            {
                output.Write("creating/destroying/recreating server... ");
                output.Flush();
                {
                    await using var server = new Server(
                        communicator,
                        new() { Endpoints = helper.GetTestEndpoint(1) });
                }

                // Use a different port than the first server to avoid an "address already in use" error.
                {
                    await using var server = new Server(
                        communicator,
                        new() { Endpoints = helper.GetTestEndpoint(2) });

                    TestHelper.Assert(!server.ShutdownComplete.IsCompleted);
                    await server.DisposeAsync();
                    TestHelper.Assert(server.ShutdownComplete.IsCompletedSuccessfully);
                }
                output.WriteLine("ok");
            }

            output.Write("creating/activating/disposing server in one operation... ");
            output.Flush();
            obj.Transient();
            obj.TransientAsync().Wait();
            output.WriteLine("ok");

            {
                output.Write("testing connection closure... ");
                output.Flush();
                for (int i = 0; i < 10; ++i)
                {
                    await using var comm = new Communicator(communicator.GetProperties());
                    _ = IServicePrx.Parse(helper.GetTestProxy("test", 0), communicator).IcePingAsync();
                }
                output.WriteLine("ok");
            }

            {
                output.Write("testing invalid server endpoints... ");
                output.Flush();
                try
                {
                    await using var server = new Server(
                        communicator,
                        new() { Endpoints = ice1 ? "tcp -h localhost -p 0" : "ice+tcp://localhost:0" });
                    TestHelper.Assert(false);
                }
                catch (ArgumentException)
                {
                    // expected
                }

                try
                {
                    await using var server = new Server(
                        communicator,
                        new()
                        {
                            Endpoints = ice1 ? "tcp -h 127.0.0.1 -p 0:tcp -h \"::1\" -p 10000" :
                                "ice+tcp://127.0.0.1:0?alt-endpoint=[::1]:10000"
                        });
                    TestHelper.Assert(false);
                }
                catch (ArgumentException)
                {
                    // expected
                }
                output.WriteLine("ok");
            }

            {
                output.Write("testing server default published endpoints... ");
                string testHost = "testhost";
                {
                    await using var server = new Server(
                        communicator,
                        new()
                        {
                            AcceptNonSecure = ice1 ? NonSecure.Always :
                                communicator.GetPropertyAsEnum<NonSecure>("Ice.AcceptNonSecure") ?? NonSecure.Always,
                            Endpoints = ice1 ? "tcp -h \"::0\" -p 0" : "ice+tcp://[::0]:0",
                            ServerName = testHost
                        });
                    TestHelper.Assert(server.PublishedEndpoints.Count == 1);
                    Endpoint publishedEndpoint = server.PublishedEndpoints[0];
                    TestHelper.Assert(publishedEndpoint.Host == testHost);
                }
                {
                    await using var server = new Server(
                        communicator,
                        new()
                        {
                            Endpoints = ice1 ? $"{helper.GetTestEndpoint(1)}:{helper.GetTestEndpoint(2)}" :
                                $"{helper.GetTestEndpoint(1)}?alt-endpoint={helper.GetTestEndpoint(2)}",
                            ServerName = testHost
                        });

                    TestHelper.Assert(server.PublishedEndpoints.Count == 2);
                    Endpoint publishedEndpoint0 = server.PublishedEndpoints[0];
                    TestHelper.Assert(publishedEndpoint0.Host == testHost);
                    TestHelper.Assert(publishedEndpoint0.Port == helper.BasePort + 1);
                    Endpoint publishedEndpoint1 = server.PublishedEndpoints[1];
                    TestHelper.Assert(publishedEndpoint1.Host == testHost);
                    TestHelper.Assert(publishedEndpoint1.Port == helper.BasePort + 2);
                }
                output.WriteLine("ok");
            }

            output.Write("testing server published endpoints... ");
            output.Flush();
            {
                await using var server = new Server(
                    communicator,
                    new()
                    {
                        PublishedEndpoints = ice1 ? "tcp -h localhost -p 12345 -t 30000" : "ice+tcp://localhost:12345"
                    });

                TestHelper.Assert(server.PublishedEndpoints.Count == 1);
                Endpoint? endpt = server.PublishedEndpoints[0];
                TestHelper.Assert(endpt != null);
                if (ice1)
                {
                    TestHelper.Assert(endpt.ToString() == "tcp -h localhost -p 12345 -t 30000");
                }
                else
                {
                    TestHelper.Assert(endpt.ToString() == "ice+tcp://localhost:12345");
                }
            }
            output.WriteLine("ok");

            Connection connection = await obj.GetConnectionAsync();
            {
                output.Write("testing server with bi-dir connection... ");
                output.Flush();
                await using var server = new Server(communicator);
                connection.Server = server;
                connection.Server = null;
                await server.DisposeAsync();
                // Setting a deactivated server on a connection no longer raise ServerDeactivatedException
                connection.Server = server;
                output.WriteLine("ok");
            }

            output.Write("testing server creation with port in use... ");
            output.Flush();
            {
                await using var server1 = new Server(
                    communicator,
                    new() { Endpoints = helper.GetTestEndpoint(10) });
                try
                {
                    await using var server2 = new Server(
                        communicator,
                        new() { Endpoints = helper.GetTestEndpoint(10) });
                    TestHelper.Assert(false);
                }
                catch
                {
                    // Expected can't re-use the same endpoint.
                }
            }
            output.WriteLine("ok");

            output.Write("deactivating server in the server... ");
            output.Flush();
            obj.Deactivate();
            output.WriteLine("ok");

            output.Write("testing whether server is gone... ");
            output.Flush();
            try
            {
                await obj.IcePingAsync();
                TestHelper.Assert(false);
            }
            catch
            {
                output.WriteLine("ok");
            }
        }
    }
}
