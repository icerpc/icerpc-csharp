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
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(scope: ParallelScope.All)]
    public class TlsConfigurationTests : ClientServerBaseTest
    {
        private static int _portNumber;

        [TestCase("c_rsa_ca1.p12", "s_rsa_ca1.p12", "cacert1.der")]
        [TestCase("cacert2.p12", "cacert2.p12", "cacert2.pem")] // Using self-signed certs
        public async Task TlsConfiguration_With_TlsOptions(
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

            await WithSecureServerAsync(clientCommunicator, serverCommunicator,
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
            bool clientValidationCallbackCalled = false;
            await using var clientCommunicator = new Communicator(
                tlsClientOptions: new TlsClientOptions()
                {
                    ClientCertificates = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(clientCertFile), "password")
                    },
                    ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
                    {
                        clientValidationCallbackCalled = true;
                        return true;
                    }
                });

            bool serverValidationCallbackCalled = false;
            await using var serverCommunicator = new Communicator(
                tlsServerOptions: new TlsServerOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath(serverCertFile), "password"),
                    ClientCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
                    {
                        serverValidationCallbackCalled = true;
                        return true;
                    },
                    RequireClientCertificate = true,
                });

            await WithSecureServerAsync(clientCommunicator, serverCommunicator,
                (server, prx) =>
                {
                    Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
                    Assert.IsTrue(prx.GetCachedConnection()!.IsSecure);
                });
            Assert.IsTrue(clientValidationCallbackCalled);
            Assert.IsTrue(serverValidationCallbackCalled);
        }

        [TestCase("c_rsa_ca1.p12", "s_rsa_ca1.p12", "cacert1.der")]
        public async Task TlsConfiguration_With_CertificateProperties(
            string clientCertFile,
            string serverCertFile,
            string caFile)
        {
            await using var clientCommunicator = CreateCommunicator(clientCertFile);
            await using var serverCommunicator = CreateCommunicator(serverCertFile);

            await WithSecureServerAsync(clientCommunicator, serverCommunicator,
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

        [Test]
        public async Task TlsConfiguration_With_CertificateSelectionCallback()
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
                        new X509Certificate2(GetCertificatePath("cacert1.der"))
                    },
                });

            await using var serverCommunicator = new Communicator(
                tlsServerOptions: new TlsServerOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath("s_rsa_ca1.p12"), "password"),
                    ClientCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
                    {
                        Assert.AreEqual(cert0.GetCertHash(), certificate!.GetCertHash());
                        return certificate!.GetCertHash().SequenceEqual(cert0.GetCertHash());
                    },
                    RequireClientCertificate = true,
                });

            await WithSecureServerAsync(clientCommunicator, serverCommunicator,
                (server, prx) =>
                {
                    Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
                    Assert.IsTrue(prx.GetCachedConnection()!.IsSecure);
                });
        }

        // The client doesn't have a CA certificate to verify the server
        [TestCase("s_rsa_ca1.p12", "")]
        // Server certificate not trusted by the client configured CA
        [TestCase("s_rsa_ca2.p12", "cacert1.der")]
        // Server certificate expired
        [TestCase("s_rsa_ca1_exp.p12", "cacert1.der")]
        public async Task TlsConfiguration_Fail_WithUntrustedServer(string serverCertFile, string caFile)
        {
            TlsClientOptions? tlsClientOptions = null;
            if (caFile.Length != 0)
            {
                tlsClientOptions = new TlsClientOptions()
                {
                    ServerCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath(caFile))
                    },
                };
            }
            await using var clientCommunicator = new Communicator(tlsClientOptions: tlsClientOptions);

            await using var serverCommunicator = new Communicator(
                tlsServerOptions: new TlsServerOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath(serverCertFile), "password")
                });

            await WithSecureServerAsync(clientCommunicator, serverCommunicator,
                (server, prx) =>
                {
                    Assert.ThrowsAsync<TransportException>(async () => await prx.IcePingAsync());
                });
        }

        // The server doesn't have a CA certificate to verify the client
        [TestCase("c_rsa_ca1.p12", "")]
        // Client certificate not trusted by the server CA
        [TestCase("c_rsa_ca2.p12", "cacert1.der")]
        // Client certificate expired
        [TestCase("c_rsa_ca1_exp.p12", "cacert1.der")]
        // The server requests a certificate but the client doesn't provide one
        [TestCase("", "cacert1.der")]
        public async Task TlsConfiguration_Fail_WithUntrustedClient(string clientCertFile, string caFile)
        {
            var tlsClientOptions = new TlsClientOptions()
            {
                ServerCertificateCertificateAuthorities = new X509Certificate2Collection
                {
                    new X509Certificate2(GetCertificatePath("cacert1.der"))
                }
            };

            if (clientCertFile.Length > 0)
            {
                tlsClientOptions.ClientCertificates = new X509Certificate2Collection()
                {
                    new X509Certificate2(GetCertificatePath(clientCertFile), "password")
                };
            }
            await using var clientCommunicator = new Communicator(tlsClientOptions: tlsClientOptions);

            var tlsServerOptions = new TlsServerOptions()
            {
                ServerCertificate = new X509Certificate2(GetCertificatePath("s_rsa_ca1.p12"), "password"),
                RequireClientCertificate = true,
            };

            if (caFile.Length != 0)
            {
                tlsServerOptions.ClientCertificateCertificateAuthorities = new X509Certificate2Collection
                {
                    new X509Certificate2(GetCertificatePath(caFile))
                };
            }
            await using var serverCommunicator = new Communicator(tlsServerOptions: tlsServerOptions);

            await WithSecureServerAsync(clientCommunicator, serverCommunicator,
                (server, prx) =>
                {
                    Assert.ThrowsAsync<ConnectionLostException>(async () => await prx.IcePingAsync());
                });
        }

        // This must succeed, the target host matches the certificate DNS altName.
        [TestCase("s_rsa_ca1_cn1.p12", "localhost", OperatingSystem.All)]
        // This must fail, the target host does not match the certificate DNS altName.
        [TestCase("s_rsa_ca1_cn2.p12", "localhost", OperatingSystem.None)]
        // This must succeed, the target host matches the certificate Common Name and the certificate
        // does not include a DNS altName.
        [TestCase("s_rsa_ca1_cn3.p12", "localhost", OperatingSystem.All & ~OperatingSystem.MacOS)]
        // This must fail, the target host does not match the certificate Common Name and the
        // certificate does not include a DNS altName.
        [TestCase("s_rsa_ca1_cn4.p12", "localhost", OperatingSystem.None)]
        // This must fail, the target host matches the certificate Common Name and the certificate has
        // a DNS altName that does not matches the target host
        [TestCase("s_rsa_ca1_cn5.p12", "localhost", OperatingSystem.None)]
        // Target host matches the certificate IP altName
        [TestCase("s_rsa_ca1_cn6.p12", "127.0.0.1", OperatingSystem.All)]
        // Target host does not match the certificate IP altName
        [TestCase("s_rsa_ca1_cn7.p12", "127.0.0.1", OperatingSystem.None)]
        // Target host is an IP address that matches the CN and the certificate doesn't include an IP
        // altName
        [TestCase("s_rsa_ca1_cn8.p12", "127.0.0.1", OperatingSystem.All & ~OperatingSystem.MacOS)]
        public async Task TlsConfiguration_HostnameVerification(
            string serverCertFile,
            string targetHost,
            OperatingSystem mustSucceed)
        {
            await using var clientCommunicator = new Communicator(
                tlsClientOptions: new TlsClientOptions()
                {
                    ServerCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath("cacert1.der"))
                    },
                });

            await using var serverCommunicator = new Communicator(
                new Dictionary<string, string>()
                {
                    ["Ice.PublishedHost"] = targetHost,
                },
                tlsServerOptions: new TlsServerOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath(serverCertFile), "password"),
                    ClientCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath("cacert1.der"))
                    }
                });

            await WithSecureServerAsync(clientCommunicator, serverCommunicator,
                (server, prx) =>
                {
                    Assert.IsTrue(server.Endpoints.All(endpoint => endpoint.Host == targetHost));
                    if ((GetOperatingSystem() & mustSucceed) != 0)
                    {
                        Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
                        Assert.IsTrue(prx.GetCachedConnection()!.IsSecure);
                    }
                    else
                    {
                        Assert.ThrowsAsync<TransportException>(async () => await prx.IcePingAsync());
                    }
                });
        }

        [Test]
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Security",
            "CA5398:Avoid hardcoding SslProtocols values",
            Justification = "Test code")]
        public async Task TlsConfiguration_With_CommonProtocol()
        {
            await using var clientCommunicator = new Communicator(
                tlsClientOptions: new TlsClientOptions()
                {
                    ServerCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath("cacert1.der"))
                    },
                    EnabledSslProtocols = SslProtocols.Tls12
                });

            await using var serverCommunicator = new Communicator(
                tlsServerOptions: new TlsServerOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath("s_rsa_ca1.p12"), "password"),
                    ClientCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath("cacert1.der"))
                    },
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                });

            await WithSecureServerAsync(clientCommunicator, serverCommunicator,
                (server, prx) =>
                {
                    Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
                    Assert.IsTrue(prx.GetCachedConnection() is TcpConnection);
                    TcpConnection connection = (TcpConnection)prx.GetCachedConnection()!;
                    Assert.IsTrue(connection.IsSecure);
                    Assert.AreEqual(SslProtocols.Tls12, connection.SslProtocol);
                });
        }

        [Test]
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Security",
            "CA5397:Do not use deprecated SslProtocols values",
            Justification = "Test code")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Security",
            "CA5398:Avoid hardcoding SslProtocols values",
            Justification = "Test code")]
        public async Task TlsConfiguration_Fail_NoCommonProtocol()
        {
            await using var clientCommunicator = new Communicator(
                tlsClientOptions: new TlsClientOptions()
                {
                    ServerCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath("cacert1.der"))
                    },
                    EnabledSslProtocols = SslProtocols.Tls12
                });

            await using var serverCommunicator = new Communicator(
                tlsServerOptions: new TlsServerOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath("s_rsa_ca1.p12"), "password"),
                    ClientCertificateCertificateAuthorities = new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath("cacert1.der"))
                    },
                    EnabledSslProtocols = SslProtocols.Tls11
                });

            // This should throw the client and the server doesn't enable a common protocol.
            await WithSecureServerAsync(clientCommunicator, serverCommunicator,
                (server, prx) =>
                {
                    Assert.ThrowsAsync<TransportException>(async () => await prx.IcePingAsync());
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

        private async Task WithSecureServerAsync(
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

        [Flags]
        public enum OperatingSystem
        {
            None = 0,
            Linux = 1,
            Windows = 2,
            MacOS = 4,
            Other = 8,
            All = Linux | Windows | MacOS | Other
        }

        static internal OperatingSystem GetOperatingSystem()
        {
            if (System.OperatingSystem.IsMacOS())
            {
                return OperatingSystem.MacOS;
            }
            else if (System.OperatingSystem.IsLinux())
            {
                return OperatingSystem.Linux;
            }
            else if (System.OperatingSystem.IsWindows())
            {
                return OperatingSystem.Windows;
            }
            else
            {
                return OperatingSystem.Other;
            }
        }
    }
}
