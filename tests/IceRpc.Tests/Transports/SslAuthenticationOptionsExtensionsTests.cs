// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Internal;
using NUnit.Framework;
using System.Net.Security;
using System.Reflection;

namespace IceRpc.Tests.Transports;

/// <summary>Guards SslAuthenticationOptionsExtensions.ShallowClone. The drift tests fail when the .NET
/// Ssl*AuthenticationOptions property set changes (a new property is silently reset in the authentication options);
/// the round-trip tests fail when ShallowClone stops copying a property it used to copy. When a drift test
/// fails, add the new property to the corresponding ShallowClone method and to the property list below.</summary>
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
    public void ShallowClone_property_list_matches_ssl_client_authentication_options() =>
        Assert.That(GetSettablePropertyNames(typeof(SslClientAuthenticationOptions)), Is.EquivalentTo(_clientProperties));

    [Test]
    public void ShallowClone_property_list_matches_ssl_server_authentication_options() =>
        Assert.That(GetSettablePropertyNames(typeof(SslServerAuthenticationOptions)), Is.EquivalentTo(_serverProperties));

    [Test]
    public void ShallowClone_copies_every_ssl_client_authentication_options_property()
    {
        var source = new SslClientAuthenticationOptions();
        IReadOnlyList<string> set = SetDistinctValues(source);

        SslClientAuthenticationOptions clone = source.ShallowClone();

        AssertCopied(source, clone, set);
    }

    [Test]
    public void ShallowClone_copies_every_ssl_server_authentication_options_property()
    {
        var source = new SslServerAuthenticationOptions();
        IReadOnlyList<string> set = SetDistinctValues(source);

        SslServerAuthenticationOptions clone = source.ShallowClone();

        AssertCopied(source, clone, set);
    }

    // Only properties with a public setter: ShallowClone can't copy others, and the application can't set them.
    private static IEnumerable<PropertyInfo> GetSettableProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod?.IsPublic is true);

    private static IEnumerable<string> GetSettablePropertyNames(Type type) =>
        GetSettableProperties(type).Select(property => property.Name);

    // Assigns each settable public property a distinct non-default value and returns the names of the properties that
    // were successfully assigned. Properties whose value can't be cheaply constructed (certificates, certificate
    // contexts, the cipher suites policy, and the selection/validation callbacks) are skipped; they remain covered by
    // the drift tests above.
    private static IReadOnlyList<string> SetDistinctValues(object options)
    {
        var assigned = new List<string>();
        foreach (PropertyInfo property in GetSettableProperties(options.GetType()))
        {
            if (TryCreateDistinctValue(property.PropertyType, property.GetValue(options), out object? value))
            {
                property.SetValue(options, value);
                assigned.Add(property.Name);
            }
        }
        // Guard against the helper silently skipping everything (e.g. a refactor breaking value creation).
        Assert.That(assigned, Has.Count.GreaterThan(6), "Too few properties exercised; the round-trip test is not meaningful.");
        return assigned;
    }

    private static bool TryCreateDistinctValue(Type type, object? current, out object? value)
    {
        try
        {
            if (type == typeof(bool))
            {
                value = !(bool)(current ?? false);
                return true;
            }
            if (type == typeof(string))
            {
                value = "icerpc-sentinel";
                return true;
            }
            if (type.IsEnum)
            {
                value = Enum.GetValues(type).Cast<object>().FirstOrDefault(v => !v.Equals(current))
                    ?? Enum.GetValues(type).Cast<object>().First();
                return !value.Equals(current);
            }
            // Reference types with a public parameterless constructor (e.g. ApplicationProtocols, ClientCertificates,
            // CertificateChainPolicy). A fresh instance is a distinct reference, which a shallow copy must preserve.
            if (!type.IsValueType && type.GetConstructor(Type.EmptyTypes) is not null)
            {
                value = Activator.CreateInstance(type);
                return value is not null && !ReferenceEquals(value, current);
            }
        }
        catch
        {
            // Fall through: value can't be constructed for this property; it stays covered by the drift tests.
        }
        value = null;
        return false;
    }

    private static void AssertCopied(object source, object clone, IReadOnlyList<string> propertyNames)
    {
        Type type = source.GetType();
        Assert.Multiple(() =>
        {
            foreach (string name in propertyNames)
            {
                PropertyInfo property = type.GetProperty(name)!;
                Assert.That(
                    property.GetValue(clone),
                    Is.EqualTo(property.GetValue(source)),
                    $"ShallowClone did not copy {type.Name}.{name}.");
            }
        });
    }
}
