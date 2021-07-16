// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(ParallelScope.All)]
    [Timeout(30000)]
    public class TlsConfigurationTests : ClientServerBaseTest
    {
        [TestCase("c_rsa_ca1.p12", "s_rsa_ca1.p12", "cacert1.der")]
        [TestCase("cacert2.p12", "cacert2.p12", "cacert2.der")] // Using self-signed certs
        public async Task TlsConfiguration_With_TlsOptions(
            string clientCertFile,
            string serverCertFile,
            string caFile)
        {
            await using Server server = CreateServer(
                tlsServerOptions: new()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath(serverCertFile), "password"),
                    RemoteCertificateValidationCallback = CertificateValidaton.GetClientCertificateValidationCallback(
                        clientCertificateRequired: true,
                        certificateAuthorities: new X509Certificate2Collection()
                        {
                            new X509Certificate2(GetCertificatePath(caFile))
                        }),
                    ClientCertificateRequired = true,
                });

            await using var connection = new Connection
            {
                Options = new ClientConnectionOptions()
                {
                    AuthenticationOptions = new()
                    {
                        ClientCertificates = new X509Certificate2Collection
                        {
                            new X509Certificate2(GetCertificatePath(clientCertFile), "password")
                        },
                        RemoteCertificateValidationCallback =
                            CertificateValidaton.GetServerCertificateValidationCallback(
                                certificateAuthorities: new X509Certificate2Collection
                                {
                                    new X509Certificate2(GetCertificatePath(caFile))
                                })
                    }
                },
                RemoteEndpoint = server.ProxyEndpoint
            };
            var prx = ServicePrx.FromConnection(connection);

            Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
            Assert.That(connection.IsSecure, Is.True);
        }

        [TestCase("c_rsa_ca1.p12", "s_rsa_ca1.p12")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Security",
            "CA5359:Do Not Disable Certificate Validation",
            Justification = "Test code")]
        public async Task TlsConfiguration_With_ValidationCallback(string clientCertFile, string serverCertFile)
        {
            bool clientValidationCallbackCalled = false;
            bool serverValidationCallbackCalled = false;
            await using Server server = CreateServer(
                tlsServerOptions: new()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath(serverCertFile), "password"),
                    RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
                    {
                        serverValidationCallbackCalled = true;
                        return true;
                    },
                    ClientCertificateRequired = true,
                });

            await using var connection = new Connection
            {
                Options = new ClientConnectionOptions()
                {
                    AuthenticationOptions = new()
                    {
                        ClientCertificates = new X509Certificate2Collection
                        {
                            new X509Certificate2(GetCertificatePath(clientCertFile), "password")
                        },
                        RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
                        {
                            clientValidationCallbackCalled = true;
                            return true;
                        }
                    }
                },
                RemoteEndpoint = server.ProxyEndpoint
            };
            var prx = ServicePrx.FromConnection(connection);

            Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
            Assert.That(connection.IsSecure, Is.True);
            Assert.That(clientValidationCallbackCalled, Is.True);
            Assert.That(serverValidationCallbackCalled, Is.True);
        }

        [Test]
        public async Task TlsConfiguration_With_CertificateSelectionCallback()
        {
            var cert0 = new X509Certificate2(GetCertificatePath("c_rsa_ca1.p12"), "password");
            var cert1 = new X509Certificate2(GetCertificatePath("c_rsa_ca2.p12"), "password");

            await using Server server = CreateServer(
                tlsServerOptions: new()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath("s_rsa_ca1.p12"), "password"),
                    RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
                    {
                        Assert.AreEqual(cert0.GetCertHash(), certificate!.GetCertHash());
                        return certificate!.GetCertHash().SequenceEqual(cert0.GetCertHash());
                    },
                    ClientCertificateRequired = true,
                });

            await using var connection = new Connection
            {
                Options = new ClientConnectionOptions()
                {
                    AuthenticationOptions = new()
                    {
                        ClientCertificates = new X509Certificate2Collection
                        {
                            cert0,
                            cert1
                        },
                        LocalCertificateSelectionCallback =
                            (sender, targetHost, certs, remoteCertificate, acceptableIssuers) =>
                            {
                                Assert.AreEqual(2, certs.Count);
                                Assert.AreEqual(cert0, certs[0]);
                                Assert.AreEqual(cert1, certs[1]);
                                return certs[0];
                            },
                        RemoteCertificateValidationCallback =
                            CertificateValidaton.GetServerCertificateValidationCallback(
                                certificateAuthorities: new X509Certificate2Collection
                                {
                                    new X509Certificate2(GetCertificatePath("cacert1.der"))
                                }),
                    }
                },
                RemoteEndpoint = server.ProxyEndpoint
            };
            var prx = ServicePrx.FromConnection(connection);

            Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
            Assert.That(connection.IsSecure, Is.True);
        }

        // The client doesn't have a CA certificate to verify the server
        [TestCase("s_rsa_ca1.p12", "")]
        // Server certificate not trusted by the client configured CA
        [TestCase("s_rsa_ca2.p12", "cacert1.der")]
        // Server certificate expired
        [TestCase("s_rsa_ca1_exp.p12", "cacert1.der")]
        public async Task TlsConfiguration_Fail_WithUntrustedServer(string serverCertFile, string caFile)
        {
            SslClientAuthenticationOptions? tlsClientOptions = null;
            if (caFile.Length != 0)
            {
                tlsClientOptions = new()
                {
                    RemoteCertificateValidationCallback = CertificateValidaton.GetServerCertificateValidationCallback(
                        certificateAuthorities: new X509Certificate2Collection
                        {
                            new X509Certificate2(GetCertificatePath(caFile))
                        })
                };
            }

            await using Server server = CreateServer(
                tlsServerOptions: new()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath(serverCertFile), "password")
                });

            await using var connection = new Connection
            {
                Options = new ClientConnectionOptions()
                {
                    AuthenticationOptions = tlsClientOptions
                },
                RemoteEndpoint = server.ProxyEndpoint
            };
            var prx = ServicePrx.FromConnection(connection);

            Assert.ThrowsAsync<TransportException>(async () => await prx.IcePingAsync());
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
            var tlsClientOptions = new SslClientAuthenticationOptions()
            {
                RemoteCertificateValidationCallback = CertificateValidaton.GetServerCertificateValidationCallback(
                    certificateAuthorities: new X509Certificate2Collection
                    {
                        new X509Certificate2(GetCertificatePath("cacert1.der"))
                    })
            };

            if (clientCertFile.Length > 0)
            {
                tlsClientOptions.ClientCertificates = new X509Certificate2Collection()
                {
                    new X509Certificate2(GetCertificatePath(clientCertFile), "password")
                };
            }

            var tlsServerOptions = new SslServerAuthenticationOptions()
            {
                ServerCertificate = new X509Certificate2(GetCertificatePath("s_rsa_ca1.p12"), "password"),
                ClientCertificateRequired = true,
            };

            if (caFile.Length != 0)
            {
                tlsServerOptions.RemoteCertificateValidationCallback =
                    CertificateValidaton.GetClientCertificateValidationCallback(
                        clientCertificateRequired: true,
                        certificateAuthorities: new X509Certificate2Collection
                        {
                            new X509Certificate2(GetCertificatePath(caFile))
                        });
            }

            await using Server server = CreateServer(tlsServerOptions);
            await using var connection = new Connection
            {
                Options = new ClientConnectionOptions()
                {
                    AuthenticationOptions = tlsClientOptions
                },
                RemoteEndpoint = server.ProxyEndpoint
            };
            var prx = ServicePrx.FromConnection(connection);

            Assert.ThrowsAsync<ConnectionLostException>(async () => await prx.IcePingAsync());
        }

        // TODO enable once https://github.com/dotnet/runtime/issues/53447 is fixed
        /*
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
        [TestCase("s_rsa_ca1_cn6.p12", "::1", OperatingSystem.All)]
        // Target host does not match the certificate IP altName
        [TestCase("s_rsa_ca1_cn7.p12", "::1", OperatingSystem.None)]
        // Target host is an IP address that matches the CN and the certificate doesn't include an IP
        // altName
        [TestCase("s_rsa_ca1_cn8.p12", "::1", OperatingSystem.All & ~OperatingSystem.MacOS)]
        public async Task TlsConfiguration_HostnameVerification(
            string serverCertFile,
            string targetHost,
            OperatingSystem mustSucceed)
        {
            await using Server server = CreateServer(
                targetHost,
                tlsServerOptions: new()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath(serverCertFile), "password")
                });

            await using var connection = new Connection
            {
                Options = new ClientConnectionOptions()
                {
                    AuthenticationOptions = new()
                    {
                        RemoteCertificateValidationCallback =
                            CertificateValidaton.GetServerCertificateValidationCallback(
                                certificateAuthorities: new X509Certificate2Collection
                                {
                                    new X509Certificate2(GetCertificatePath("cacert1.der"))
                                }),
                    }
                },
                RemoteEndpoint = server.ProxyEndpoint
            };
            var prx = ServicePrx.FromConnection(connection);

            if ((GetOperatingSystem() & mustSucceed) != 0)
            {
                Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
                Assert.IsTrue(connection.IsSecure);
            }
            else
            {
                Assert.ThrowsAsync<TransportException>(async () => await prx.IcePingAsync());
            }
        }*/

        [Test]
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Security",
            "CA5398:Avoid hardcoding SslProtocols values",
            Justification = "Test code")]
        public async Task TlsConfiguration_With_CommonProtocol()
        {
            await using Server server = CreateServer(
                tlsServerOptions: new()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath("s_rsa_ca1.p12"), "password"),
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                });

            await using var connection = new Connection
            {
                Options = new ClientConnectionOptions()
                {
                    AuthenticationOptions = new()
                    {
                        RemoteCertificateValidationCallback =
                            CertificateValidaton.GetServerCertificateValidationCallback(
                                certificateAuthorities: new X509Certificate2Collection
                                {
                                    new X509Certificate2(GetCertificatePath("cacert1.der"))
                                }),
                        EnabledSslProtocols = SslProtocols.Tls12
                    }
                },
                RemoteEndpoint = server.ProxyEndpoint
            };
            var prx = ServicePrx.FromConnection(connection);

            Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());

            SslStream? sslStream =
                (prx.Connection!.UnderlyingConnection as NetworkSocketConnection)?.NetworkSocket.SslStream;

            Assert.That(prx.Connection.IsSecure, Is.True);
            Assert.That(sslStream, Is.Not.Null);
            Assert.AreEqual(SslProtocols.Tls12, sslStream.SslProtocol);
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
            // This should throw the client and the server doesn't enable a common protocol.
            await using Server server = CreateServer(
                tlsServerOptions: new SslServerAuthenticationOptions()
                {
                    ServerCertificate = new X509Certificate2(GetCertificatePath("s_rsa_ca1.p12"), "password"),
                    EnabledSslProtocols = SslProtocols.Tls11
                });

            await using var connection = new Connection
            {
                Options = new ClientConnectionOptions()
                {
                    AuthenticationOptions = new()
                    {
                        RemoteCertificateValidationCallback =
                            CertificateValidaton.GetServerCertificateValidationCallback(
                                certificateAuthorities: new X509Certificate2Collection
                                {
                                    new X509Certificate2(GetCertificatePath("cacert1.der"))
                                }),
                        EnabledSslProtocols = SslProtocols.Tls12
                    }
                },
                RemoteEndpoint = server.ProxyEndpoint
            };
            var prx = ServicePrx.FromConnection(connection);

            Assert.ThrowsAsync<TransportException>(async () => await prx.IcePingAsync());
        }

        private static string GetCertificatesDir() =>
           Path.Combine(Environment.CurrentDirectory, "certs");

        private static string GetCertificatePath(string file) =>
            Path.Combine(GetCertificatesDir(), file);

        private Server CreateServer(SslServerAuthenticationOptions tlsServerOptions) =>
            CreateServer(null, tlsServerOptions);

        private Server CreateServer(string? hostname, SslServerAuthenticationOptions tlsServerOptions)
        {
            string serverHost = hostname switch
            {
                null => "::1",
                "localhost" => System.OperatingSystem.IsWindows() ? "::1" : "127.0.0.1",
                _ => hostname
            };

            var server = new Server
            {
                Dispatcher = new Greeter(),
                Endpoint = GetTestEndpoint(serverHost, tls: true),
                ConnectionOptions = new()
                {
                    AuthenticationOptions = tlsServerOptions
                },
                HostName = hostname ?? "::1"
            };

            server.Listen();

            return server;
        }

        internal class Greeter : Service, IGreeter
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) => default;
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

        /*static internal OperatingSystem GetOperatingSystem()
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
        }*/
    }
}
