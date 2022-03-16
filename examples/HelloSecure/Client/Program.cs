// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using System.Net.Security;

// Create the authentication options with a custom certificate validation callback that uses our Root CA certificate.
var authenticationOptions = new SslClientAuthenticationOptions()
{
    RemoteCertificateValidationCallback =
        CertificateValidaton.GetServerCertificateValidationCallback("../../certs/cacert.der")
};

await using var connection = new Connection("icerpc://127.0.0.1", authenticationOptions);

IHelloPrx hello = HelloPrx.FromConnection(connection);

Console.Write("To say hello to the server, type your name: ");

if (Console.ReadLine() is string name)
{
    Console.WriteLine(await hello.SayHelloAsync(name));
}
