// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading.Tasks;
using NUnit.Framework;
using ZeroC.Ice;

namespace IceRpc.Tests.Api
{
    [Parallelizable(scope: ParallelScope.All)]
    public class ServerTests
    {
        [Test]
        public async Task Server_ArgumentException()
        {
            await using var communicator = new Communicator();

            // A hostname cannot be used with an ephemereal port 0
            Assert.Throws<System.ArgumentException>(
                () => new Server(new Communicator(), new ServerOptions() { Endpoints = "tcp -h foo -p 0" }));

            // ReplicaGroupId is set but options.AdapterId is not
            Assert.Throws<System.ArgumentException>(
                () => new Server(communicator, new ServerOptions() { ReplicaGroupId = "replica-group" }));

            // LocatorRegistry is set but options.AdapterId is not
            Assert.Throws<System.ArgumentException>(
                () => new Server(communicator,
                                 new ServerOptions()
                                 {
                                     LocatorRegistry = ILocatorRegistryPrx.Parse("default", communicator)
                                 }));

            // IncomingFrameMaxSize cannot be less than 1KB
            Assert.Throws<System.ArgumentException>(
                () => new Server(communicator, new ServerOptions() { IncomingFrameMaxSize = 1000 }));

            // AdapterId set for an ice2 server
            Assert.Throws<System.ArgumentException>(
                () => new Server(communicator,
                                 new ServerOptions()
                                 {
                                     AdapterId = "adapter-id",
                                     Endpoints = "ice+tcp://localhost:10000"
                                 }));

            // AdapterId set for an ice2 server
            Assert.Throws<System.ArgumentException>(
                () => new Server(communicator,
                                 new ServerOptions()
                                 {
                                     AdapterId = "adapter-id",
                                     Protocol = Protocol.Ice2
                                 }));

            // Server can only accept secure connections
            Assert.Throws<System.ArgumentException>(
                () => new Server(communicator,
                                 new ServerOptions()
                                 {
                                     AcceptNonSecure = NonSecure.Never,
                                     Endpoints = "tcp -h localhost -p 10000"
                                 }));

            // only one endpoint is allowed when a dynamic IP port (:0) is configured
            Assert.Throws<System.ArgumentException>(
                () => new Server(communicator,
                                 new ServerOptions()
                                 {
                                     AcceptNonSecure = NonSecure.Never,
                                     Endpoints = "ice+tcp://localhost:0?alt-endpoint=localhost1:10000"
                                 }));

            // both PublishedHost and PublishedEndpoints are empty"
            Assert.Throws<System.ArgumentException>(
                () => new Server(communicator,
                                 new ServerOptions()
                                 {
                                     PublishedEndpoints = "",
                                     PublishedHost = "",
                                     Endpoints = "ice+tcp://localhost:10000"
                                 }));

        }

        [Test]
        public async Task Server_EndpointInformation()
        {
            await using var communicator = new Communicator();
            var server = new Server(
                communicator,
                new()
                {
                    AcceptNonSecure = NonSecure.Always,
                    Endpoints = $"tcp -h 127.0.0.1 -p 0 -t 15000",
                    PublishedHost = "localhost"
                });

            Assert.AreEqual(1, server.Endpoints.Count);

            CollectionAssert.AreEquivalent(server.PublishedEndpoints, server.PublishedEndpoints);

            Assert.IsNotNull(server.Endpoints[0]);
            Assert.AreEqual(Transport.TCP, server.Endpoints[0].Transport);
            Assert.AreEqual("127.0.0.1", server.Endpoints[0].Host);
            Assert.IsTrue(server.Endpoints[0].Port > 0);
            Assert.AreEqual("15000", server.Endpoints[0]["timeout"]);

            Assert.IsNotNull(server.PublishedEndpoints[0]);
            Assert.AreEqual(Transport.TCP, server.PublishedEndpoints[0].Transport);
            Assert.AreEqual("localhost", server.PublishedEndpoints[0].Host);
            Assert.IsTrue(server.PublishedEndpoints[0].Port > 0);
            Assert.AreEqual("15000", server.PublishedEndpoints[0]["timeout"]);

            await server.DisposeAsync();


            await CheckServerEndpoint(communicator, "tcp -h {0} -p {1}", 10001);
            await CheckServerEndpoint(communicator, "ice+tcp://{0}:{1}", 10001);

            static async Task CheckServerEndpoint(Communicator communicator, string endpoint, int port)
            {
                await using var server = new Server(
                    communicator,
                    new()
                    {
                        Endpoints = string.Format(endpoint, "0.0.0.0", port),
                        PublishedEndpoints = string.Format(endpoint, "127.0.0.1", port)

                    });

                Assert.IsTrue(server.Endpoints.Count >= 1);
                Assert.IsTrue(server.PublishedEndpoints.Count == 1);

                foreach (Endpoint e in server.Endpoints)
                {
                    Assert.AreEqual(port, e.Port);
                }

                Assert.AreEqual("127.0.0.1", server.PublishedEndpoints[0].Host);
                Assert.AreEqual(port, server.PublishedEndpoints[0].Port);
            }
        }
    }
}
