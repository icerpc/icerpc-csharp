// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using System.Net.Security;
using System.Reflection;

namespace IceRpc.Tests.Transports;

/// <summary>Guards SslAuthenticationOptionsExtensions.ShallowClone against new properties added to the .NET
/// Ssl*AuthenticationOptions classes: ShallowClone copies each property by hand, so a property added by a future
/// .NET release would be silently dropped from the user's TLS configuration. When one of these tests fails, add the
/// new property to the corresponding ShallowClone method and to the property list below.</summary>
[Parallelizable(ParallelScope.All)]
public class SslAuthenticationOptionsExtensionsTests
{
    // The properties copied by SslAuthenticationOptionsExtensions.ShallowClone(SslClientAuthenticationOptions).
    private static readonly string[] _clientProperties =
    [
        "AllowRenegotiation",
        "AllowRsaPkcs1Padding",
        "AllowRsaPssPadding",
        "AllowTlsResume",
        "ApplicationProtocols",
        "CertificateChainPolicy",
        "CertificateRevocationCheckMode",
        "CipherSuitesPolicy",
        "ClientCertificateContext",
        "ClientCertificates",
        "EnabledSslProtocols",
        "EncryptionPolicy",
        "LocalCertificateSelectionCallback",
        "RemoteCertificateValidationCallback",
        "TargetHost"
    ];

    // The properties copied by SslAuthenticationOptionsExtensions.ShallowClone(SslServerAuthenticationOptions).
    private static readonly string[] _serverProperties =
    [
        "AllowRenegotiation",
        "AllowRsaPkcs1Padding",
        "AllowRsaPssPadding",
        "AllowTlsResume",
        "ApplicationProtocols",
        "CertificateChainPolicy",
        "CertificateRevocationCheckMode",
        "CipherSuitesPolicy",
        "ClientCertificateRequired",
        "EnabledSslProtocols",
        "EncryptionPolicy",
        "RemoteCertificateValidationCallback",
        "ServerCertificate",
        "ServerCertificateContext",
        "ServerCertificateSelectionCallback"
    ];

    [Test]
    public void ShallowClone_copies_all_ssl_client_authentication_options_properties() =>
        Assert.That(GetSettablePropertyNames(typeof(SslClientAuthenticationOptions)), Is.EquivalentTo(_clientProperties));

    [Test]
    public void ShallowClone_copies_all_ssl_server_authentication_options_properties() =>
        Assert.That(GetSettablePropertyNames(typeof(SslServerAuthenticationOptions)), Is.EquivalentTo(_serverProperties));

    // Only properties with a public setter: ShallowClone can't copy others, and the application can't set them.
    private static IEnumerable<string> GetSettablePropertyNames(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod?.IsPublic is true)
            .Select(property => property.Name);
}
