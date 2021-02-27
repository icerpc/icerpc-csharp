// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZeroC.Ice;

namespace IceRpc.Tests.ClientServer
{
    [Parallelizable]
    public class TlsConfigurationTests : ClientServerBaseTest
    {
        private static int _portNumber;

        [TestCase("c_rsa_ca1.p12", "s_rsa_ca1.p12", "cacert1.der")]
        public async Task TlsConfiguration_With_TlsOptions(string clientCertFile, string serverCertFile, string caFile)
        {
            await using var clientCommunicator = new Communicator(
                tlsClientOptions: new TlsClientOptions()
                {
                    ClientCertificates = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(clientCertFile), "password")
                    },
                    ServerCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(caFile))
                    },
                });

            await using var serverCommunicator = new Communicator(
                tlsServerOptions: new TlsServerOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath(serverCertFile), "password"),
                    ClientCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(caFile))
                    },
                    RequireClientCertificate = true,
                });

            await WithSecureServerAsnyc(clientCommunicator, serverCommunicator,
                (server, prx) =>
                {
                    Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
                    Assert.IsTrue(prx.GetCachedConnection()!.IsSecure);
                });
        }

        [TestCase("c_rsa_ca1.p12", "s_rsa_ca1.p12")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Security",
            "CA5359:Do Not Disable Certificate Validation",
            Justification = "Test code")]
        public async Task TlsConfiguration_With_ValidationCallback(string clientCertFile, string serverCertFile)
        {
            bool clientValiadationCallbackCalled = false;
            await using var clientCommunicator = new Communicator(
                tlsClientOptions: new TlsClientOptions()
                {
                    ClientCertificates = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(clientCertFile), "password")
                    },
                    ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
                    {
                        clientValiadationCallbackCalled = true;
                        return true;
                    }
                });

            bool serverValiadationCallbackCalled = false;
            await using var serverCommunicator = new Communicator(
                tlsServerOptions: new TlsServerOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath(serverCertFile), "password"),
                    ClientCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
                    {
                        serverValiadationCallbackCalled = true;
                        return true;
                    },
                    RequireClientCertificate = true,
                });

            await WithSecureServerAsnyc(clientCommunicator, serverCommunicator,
                (server, prx) =>
                {
                    Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
                    Assert.IsTrue(prx.GetCachedConnection()!.IsSecure);
                });
            Assert.IsTrue(clientValiadationCallbackCalled);
            Assert.IsTrue(serverValiadationCallbackCalled);
        }

        [TestCase("c_rsa_ca1.p12", "s_rsa_ca1.p12", "cacert1.der")]
        public async Task TlsConfiguration_With_CertificateProperties(
            string clientCertFile,
            string serverCertFile,
            string caFile)
        {
            await using var clientCommunicator = CreateCommunicator(clientCertFile);
            await using var serverCommunicator = CreateCommunicator(serverCertFile);

            await WithSecureServerAsnyc(clientCommunicator, serverCommunicator,
                (server, prx) =>
                {
                    Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
                    Assert.IsTrue(prx.GetCachedConnection()!.IsSecure);
                });

            Communicator CreateCommunicator(string certFile) =>
                new Communicator(new Dictionary<string, string>()
                {
                    { "IceSSL.DefaultDir", GetCertificatesDir() },
                    { "IceSSL.CertFile", certFile },
                    { "IceSSL.CAs", caFile },
                    { "IceSSL.Password", "password" }
                });
        }

        [TestCase("s_rsa_ca1.p12", "cacert1.der")]
        public async Task TlsConfiguration_With_CertificateSelectionCallback(string serverCertFile, string caFile)
        {
            var cert0 = new X509Certificate2(GetCertificatePath("c_rsa_ca1.p12"), "password");
            var cert1 = new X509Certificate2(GetCertificatePath("c_rsa_ca2.p12"), "password");
            await using var clientCommunicator = new Communicator(
                tlsClientOptions: new TlsClientOptions()
                {
                    ClientCertificates = new X509Certificate2Collection
                    {
                        cert0,
                        cert1
                    },
                    ClientCertificateSelectionCallback =
                        (sender, targetHost, certs, remoteCertificate, acceptableIssuers) =>
                        {
                            Assert.AreEqual(2, certs.Count);
                            Assert.AreEqual(cert0, certs[0]);
                            Assert.AreEqual(cert1, certs[1]);
                            return certs[0];
                        },
                    ServerCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(caFile))
                    },
                });

            await using var serverCommunicator = new Communicator(
                tlsServerOptions: new TlsServerOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath(serverCertFile), "password"),
                    ClientCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(caFile))
                    },
                    RequireClientCertificate = true,
                });

            await WithSecureServerAsnyc(clientCommunicator, serverCommunicator,
                (server, prx) =>
                {
                    Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
                    Assert.IsTrue(prx.GetCachedConnection()!.IsSecure);
                });
        }

        [TestCase("s_rsa_ca1.p12", "cacert1.der")]
        public async Task TlsConfiguration_Fail_WithOutClientCert(string serverCertFile, string caFile)
        {
            await using var clientCommunicator = new Communicator(
                tlsClientOptions: new TlsClientOptions()
                {
                    ServerCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(caFile))
                    },
                });

            await using var serverCommunicator = new Communicator(
                tlsServerOptions: new TlsServerOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath(serverCertFile), "password"),
                    ClientCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(caFile))
                    },
                    RequireClientCertificate = true,
                });

            // This should throw the server request a certificate and the client doesn't provide one
            await WithSecureServerAsnyc(clientCommunicator, serverCommunicator,
                (server, prx) =>
                {
                    Assert.ThrowsAsync<ConnectionLostException>(async () => await prx.IcePingAsync());
                });
        }

        [TestCase("c_rsa_ca1.p12", "s_rsa_ca1.p12", "cacert1.der")]
        public async Task TlsConfiguration_Fail_WithUntrustedServer(
            string clientCertFile,
            string serverCertFile,
            string caFile)
        {
            await using var clientCommunicator = new Communicator(
                tlsClientOptions: new TlsClientOptions()
                {
                    ClientCertificates = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(clientCertFile), "password")
                    }
                });

            await using var serverCommunicator = new Communicator(
                tlsServerOptions: new TlsServerOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath(serverCertFile), "password"),
                    ClientCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(caFile))
                    },
                    RequireClientCertificate = true,
                });

            // This should throw the client doesn't trust the server certificate.
            await WithSecureServerAsnyc(clientCommunicator, serverCommunicator,
                (server, prx) =>
                {
                    Assert.ThrowsAsync<TransportException>(async () => await prx.IcePingAsync());
                });
        }

        [TestCase("c_rsa_ca1.p12", "s_rsa_ca1.p12", "cacert1.der")]
        public async Task TlsConfiguration_Fail_WithUntrustedClient1(
            string clientCertFile,
            string serverCertFile,
            string caFile)
        {
            await using var clientCommunicator = new Communicator(
                tlsClientOptions: new TlsClientOptions()
                {
                    ClientCertificates = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(clientCertFile), "password")
                    },
                    ServerCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(caFile))
                    },
                });

            await using var serverCommunicator = new Communicator(
                tlsServerOptions: new TlsServerOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath(serverCertFile), "password"),
                    RequireClientCertificate = true,
                });

            // This should throw the server doesn't trust the client certificate.
            await WithSecureServerAsnyc(clientCommunicator, serverCommunicator,
                (server, prx) =>
                {
                    Assert.ThrowsAsync<ConnectionLostException>(async () => await prx.IcePingAsync());
                });
        }

        [TestCase("c_rsa_ca2.p12", "s_rsa_ca1.p12", "cacert1.der")]
        public async Task TlsConfiguration_Fail_WithUntrustedClient2(
            string clientCertFile,
            string serverCertFile,
            string caFile)
        {
            await using var clientCommunicator = new Communicator(
                tlsClientOptions: new TlsClientOptions()
                {
                    ClientCertificates = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(clientCertFile), "password")
                    },
                    ServerCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(caFile))
                    },
                });

            await using var serverCommunicator = new Communicator(
                tlsServerOptions: new TlsServerOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath(serverCertFile), "password"),
                    ClientCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(caFile))
                    },
                    RequireClientCertificate = true,
                });

            // This should throw the server doesn't trust the client certificate.
            await WithSecureServerAsnyc(clientCommunicator, serverCommunicator,
                (server, prx) =>
                {
                    Assert.ThrowsAsync<ConnectionLostException>(async () => await prx.IcePingAsync());
                });
        }

        [Test]
        public async Task TlsConfiguration_HostnameVerification()
        {
            await using var clientCommunicator = new Communicator(
                tlsClientOptions: new TlsClientOptions()
                {
                    ClientCertificates = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath("c_rsa_ca1.p12"), "password")
                    },
                    ServerCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath("cacert1.der"))
                    },
                });

            await using var serverCommunicator = new Communicator(
                new Dictionary<string, string>()
                {
                    ["Ice.PublishedHost"] = "localhost",
                },
                tlsServerOptions: new TlsServerOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath("s_rsa_ca1_cn1.p12"), "password"),
                    ClientCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath("cacert1.der"))
                    },
                    RequireClientCertificate = true,
                });

            await WithSecureServerAsnyc(clientCommunicator, serverCommunicator,
                (server, prx) =>
                {
                    Assert.IsTrue(server.Endpoints.All(endpoint => endpoint.Host == "localhost"));
                    Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
                    Assert.IsTrue(prx.GetCachedConnection()!.IsSecure);
                });
        }

        [TestCase("c_rsa_ca1.p12", "s_rsa_ca1.p12", "cacert1.der")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Security",
            "CA5398:Avoid hardcoding SslProtocols",
            Justification = "Test code")]
        public async Task TlsConfiguration_With_CommonProtocol(
            string clientCertFile,
            string serverCertFile,
            string caFile)
        {
            await using var clientCommunicator = new Communicator(
                tlsClientOptions: new TlsClientOptions()
                {
                    ClientCertificates = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(clientCertFile), "password")
                    },
                    ServerCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(caFile))
                    },
                    EnabledSslProtocols = SslProtocols.Tls12
                });

            await using var serverCommunicator = new Communicator(
                tlsServerOptions: new TlsServerOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath(serverCertFile), "password"),
                    ClientCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(caFile))
                    },
                    RequireClientCertificate = true,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                });

            await WithSecureServerAsnyc(clientCommunicator, serverCommunicator,
                (server, prx) =>
                {
                    Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
                    Assert.IsTrue(prx.GetCachedConnection() is TcpConnection);
                    TcpConnection connection = (TcpConnection)prx.GetCachedConnection()!;
                    Assert.IsTrue(connection.IsSecure);
                    Assert.AreEqual(SslProtocols.Tls12, connection.SslProtocol);
                });
        }

        [TestCase("c_rsa_ca1.p12", "s_rsa_ca1.p12", "cacert1.der")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Security",
            "CA5398:Avoid hardcoding SslProtocols",
            Justification = "Test code")]
        public async Task TlsConfiguration_Fail_NoCommonProtocol(
            string clientCertFile,
            string serverCertFile,
            string caFile)
        {
            await using var clientCommunicator = new Communicator(
                tlsClientOptions: new TlsClientOptions()
                {
                    ClientCertificates = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(clientCertFile), "password")
                    },
                    ServerCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(caFile))
                    },
                    EnabledSslProtocols = SslProtocols.Tls13
                });

            await using var serverCommunicator = new Communicator(
                tlsServerOptions: new TlsServerOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath(serverCertFile), "password"),
                    ClientCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(caFile))
                    },
                    RequireClientCertificate = true,
                    EnabledSslProtocols = SslProtocols.Tls13
                });

            // This should throw the client and the server doesn't enable a common protocol.
            await WithSecureServerAsnyc(clientCommunicator, serverCommunicator,
                (server, prx) =>
                {
                    Assert.ThrowsAsync<TransportException>(async () => await prx.IcePingAsync());
                });
        }

        [TestCase("c_rsa_ca1.p12", "s_rsa_ca1_exp.p12", "cacert1.der")]
        public async Task TlsConfiguration_Fail_ServerCertificateExpired(
            string clientCertFile,
            string serverCertFile,
            string caFile)
        {
            await using var clientCommunicator = new Communicator(
                tlsClientOptions: new TlsClientOptions()
                {
                    ClientCertificates = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(clientCertFile), "password")
                    },
                    ServerCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(caFile))
                    }
                });

            await using var serverCommunicator = new Communicator(
                tlsServerOptions: new TlsServerOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath(serverCertFile), "password"),
                    ClientCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(caFile))
                    },
                    RequireClientCertificate = true
                });

            // This should throw the client and the server doesn't enable a common protocol.
            await WithSecureServerAsnyc(clientCommunicator, serverCommunicator,
                (server, prx) =>
                {
                    Assert.ThrowsAsync<TransportException>(async () => await prx.IcePingAsync());
                });
        }

        [TestCase("c_rsa_ca1_exp.p12", "s_rsa_ca1.p12", "cacert1.der")]
        public async Task TlsConfiguration_Fail_ClientCertificateExpired(
            string clientCertFile,
            string serverCertFile,
            string caFile)
        {
            await using var clientCommunicator = new Communicator(
                tlsClientOptions: new TlsClientOptions()
                {
                    ClientCertificates = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(clientCertFile), "password")
                    },
                    ServerCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(caFile))
                    }
                });

            await using var serverCommunicator = new Communicator(
                tlsServerOptions: new TlsServerOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath(serverCertFile), "password"),
                    ClientCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(caFile))
                    },
                    RequireClientCertificate = true
                });

            // This should throw the client and the server doesn't enable a common protocol.
            await WithSecureServerAsnyc(clientCommunicator, serverCommunicator,
                (server, prx) =>
                {
                    Assert.ThrowsAsync<ConnectionLostException>(async () => await prx.IcePingAsync());
                });
        }

        [Test]
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Security",
            "CA5359:Do Not Disable Certificate Validation",
            Justification = "Test code")]
        public void TlsConfiguration_With_InvalidOptions()
        {
            // Certificate authorities and validation callback settings are mutually exclusive
            var clientOptions = new TlsClientOptions
            {
                ServerCertificateCertificateAuthorities = new X509Certificate2Collection
                {
                    new X509Certificate2(GetCertificatePath("cacert1.der"))
                }
            };
            _ = Assert.Throws<ArgumentException>(() =>
                clientOptions.ServerCertificateValidationCallback =
                    (sender, certificate, chain, sslPolicyErrors) => true);

            clientOptions = new TlsClientOptions
            {
                ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true
            };
            _ = Assert.Throws<ArgumentException>(() =>
                clientOptions.ServerCertificateCertificateAuthorities = new X509Certificate2Collection
                {
                    new X509Certificate2(GetCertificatePath("cacert1.der"))
                });

            var serverOptions = new TlsServerOptions
            {
                ClientCertificateCertificateAuthorities = new X509Certificate2Collection
                {
                    new X509Certificate2(GetCertificatePath("cacert1.der"))
                }
            };
            _ = Assert.Throws<ArgumentException>(() =>
                serverOptions.ClientCertificateValidationCallback =
                    (sender, certificate, chain, sslPolicyErrors) => true);

            serverOptions = new TlsServerOptions
            {
                ClientCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true
            };
            _ = Assert.Throws<ArgumentException>(() =>
                serverOptions.ClientCertificateCertificateAuthorities = new X509Certificate2Collection
                {
                    new X509Certificate2(GetCertificatePath("cacert1.der"))
                });
        }

        private static string GetCertificatesDir() =>
           Path.Combine(Environment.CurrentDirectory, "certs");

        private static string GetCertificatePath(string file) =>
            Path.Combine(GetCertificatesDir(), file);

        private async Task WithSecureServerAsnyc(
            Communicator clientCommunicator,
            Communicator serverCommunicator,
            Action<Server, IServicePrx> closure)
        {
            int portNumber = GetNextPortNumber();
            string hostname = serverCommunicator.GetProperty("Ice.PublishedHost") ?? "127.0.0.1";
            await using var server = new Server(serverCommunicator,
                new ServerOptions()
                {
                    ColocationScope = ColocationScope.None,
                    Endpoints = GetTestEndpoint(hostname, port: portNumber),
                    AcceptNonSecure = NonSecure.Never,
                });

            server.Add("hello", new GreeterTestService());
            await server.ActivateAsync();

            var prx = IServicePrx.Parse(GetTestProxy("hello", hostname, portNumber), clientCommunicator).Clone(
                preferNonSecure: NonSecure.Never);
            closure(server, prx);
        }

        private static int GetNextPortNumber() =>
            Interlocked.Increment(ref _portNumber);

        internal class GreeterTestService : IAsyncGreeterTestService
        {
            public ValueTask SayHelloAsync(Current current, CancellationToken cancel) => default;
        }
    }
}
