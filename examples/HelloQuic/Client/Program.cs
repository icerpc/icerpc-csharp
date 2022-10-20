// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using IceRpc.Transports;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Demo;

[System.Runtime.Versioning.SupportedOSPlatform("macOS")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class Program
{
    static async Task Main(string[] args)
    {
        using var rootCA = new X509Certificate2("../../certs/cacert.der");
        var authenticationOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true,
            ClientCertificates = new X509CertificateCollection()
            {
                new X509Certificate2("../../certs/client.p12", "password")
            }
        };

        await using var connection = new ClientConnection(
            new ClientConnectionOptions
            {
                ServerAddress = new ServerAddress(new Uri("icerpc://127.0.0.1")),
                ClientAuthenticationOptions = authenticationOptions
            },
            multiplexedClientTransport: new QuicClientTransport());

        var helloProxy = new HelloProxy(connection);
        string greeting = await helloProxy.SayHelloAsync(Environment.UserName);

        Console.WriteLine(greeting);
    }
}
