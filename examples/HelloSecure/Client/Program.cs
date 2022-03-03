// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

// Create the authentication options with the certificate authorities defined in the
// configured certificate authorities file.
var authenticationOptions = new SslClientAuthenticationOptions()
{
    RemoteCertificateValidationCallback =
        CertificateValidaton.GetServerCertificateValidationCallback(
            certificateAuthorities: new X509Certificate2Collection
            {
                new X509Certificate2("../../certs/cacert.der")
            })
};

await using var connection = new Connection("icerpc://127.0.0.1", authenticationOptions);

IHelloPrx hello = HelloPrx.FromConnection(connection);

Console.Write("To say hello to the server, type your name: ");

if (Console.ReadLine() is string name)
{
    Console.WriteLine(await hello.SayHelloAsync(name));
}
