// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using NUnit.Framework;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace IceRpc.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class ServiceAddressTests
{
    /// <summary>Provides test case data for <see cref="Equal_service_addresses_produce_the_same_hash_code" />
    /// test.</summary>
    private static IEnumerable<TestCaseData> ServiceAddressHashCodeSource
    {
        get
        {
            foreach ((string str, string _, string _) in _validServiceAddressUris)
            {
                yield return new TestCaseData(new ServiceAddress(new Uri(str)));
            }
        }
    }

    /// <summary>Provides test case data for <see cref="Create_service_address_from_invalid_uri(Uri)" />
    /// test.</summary>
    private static IEnumerable<TestCaseData> ServiceAddressInvalidUriSource
    {
        get
        {
            foreach (string str in _invalidServiceAddressUris)
            {
                yield return new TestCaseData(new Uri(str));
            }
        }
    }

    /// <summary>Provides test case data for <see cref="Create_service_address_from_uri(Uri, string, string)" />
    /// test.</summary>
    private static IEnumerable<TestCaseData> ServiceAddressUriSource
    {
        get
        {
            foreach ((string str, string path, string fragment) in _validServiceAddressUris)
            {
                yield return new TestCaseData(new Uri(str), path, fragment);
            }
        }
    }

    /// <summary>Provides test case data for <see cref="Convert_a_service_address_to_a_string(ServiceAddress)" /> test.
    /// </summary>
    private static IEnumerable<TestCaseData> ServiceAddressToStringSource
    {
        get
        {
            foreach ((string str, string _, string _) in _validServiceAddressUris)
            {
                yield return new TestCaseData(new ServiceAddress(new Uri(str)));
            }
        }
    }

    private static IEnumerable<TestCaseData> ServiceAddressToStringWithSetupSource
    {
        get
        {
            foreach ((ServiceAddress serviceAddress, string expected) in ServiceAddressToStringData)
            {
                yield return new TestCaseData(serviceAddress, expected);
            }
        }
    }

    /// <summary>Provides test case data for
    /// <see cref="Create_service_address_with_alt_server(ServiceAddress, ServerAddress[])" /> test.</summary>
    private static IEnumerable<TestCaseData> AltServerAddressesSource
    {
        get
        {
            foreach ((string str, ServerAddress[] altServerAddresses) in _altServerAddresses)
            {
                yield return new TestCaseData(new ServiceAddress(new Uri(str)), altServerAddresses);
            }
        }
    }

    private static IEnumerable<TestCaseData> ServiceAddressEqualitySource
    {
        get
        {
            foreach ((ServiceAddress serviceAddress1, ServiceAddress? serviceAddress2, bool expected) in ServiceAddressEqualityData)
            {
                yield return new TestCaseData(serviceAddress1, serviceAddress2, expected);
            }
        }
    }

    private static IEnumerable<TestCaseData> ServiceAddressToUriSource
    {
        get
        {
            foreach ((ServiceAddress serviceAddress, string expected) in ServiceAddressToUriData)
            {
                yield return new TestCaseData(serviceAddress, expected);
            }
        }
    }

    private static (ServiceAddress, ServiceAddress?, bool)[] ServiceAddressEqualityData
    {
        get
        {
            ServiceAddress serviceAddress = new ServiceAddress.Ice { Path = "/foo" };
            return new (ServiceAddress, ServiceAddress?, bool)[]
            {
                (serviceAddress, serviceAddress, true),
                (serviceAddress, null, false),
                (serviceAddress, new ServiceAddress.IceRpc(), false), // Different protocol.

                // Server address params (Order does not matter)
                (
                    new ServiceAddress(new Uri("ice://localhost:8080/foo?abc=123&def=456")),
                    new ServiceAddress(new Uri("ice://localhost:8080/foo?def=456&abc=123")),
                    true),

                // AltServerAddresses (Order matters)
                (
                    new ServiceAddress(new Uri("ice://localhost:8080/foo?alt-server=localhost:10000,localhost:10101")),
                    new ServiceAddress(new Uri("ice://localhost:8080/foo?alt-server=localhost:10101,localhost:10000")),
                    false),
            };
        }
    }

    private static (ServiceAddress, string)[] ServiceAddressToStringData
    {
        get
        {
            // Service address with alt servers
            var serviceAddressWithTransport = new ServiceAddress.IceRpc
            {
                ServerAddress = new ServerAddress(new Uri("icerpc://host1?transport=tcp")),
                AltServerAddresses = [new ServerAddress(new Uri("icerpc://host2"))]
            };

            var serviceAddressWithAltServerAddresses =
                new ServiceAddress.Ice(new Uri("ice://localhost:8080/foo?abc=123#bar")) with
                {
                    AltServerAddresses = ImmutableList.Create(
                        new ServerAddress(new Uri("ice://localhost:10000?transport=fizz")),
                        new ServerAddress(new Uri("ice://localhost:10101?transport=buzz")))
                };

            // Service address with an adapter ID that needs escaping
            var serviceAddressWithAdapterId = new ServiceAddress.Ice { Path = "/foo", AdapterId = "my adapter" };

            return
            [
                (serviceAddressWithTransport, "icerpc://host1/?transport=tcp&alt-server=host2"),
                (
                    serviceAddressWithAltServerAddresses,
                    "ice://localhost:8080/foo?abc=123&alt-server=localhost:10000?transport=fizz,localhost:10101?transport=buzz#bar"),
                (serviceAddressWithAdapterId, "ice:/foo?adapter-id=my%20adapter")
            ];
        }
    }

    private static (ServiceAddress, string)[] ServiceAddressToUriData
    {
        get
        {
            var serviceAddress = new ServiceAddress(new Uri("ice://localhost:8080/foo?abc=123#bar"));
            var serviceAddressWithoutServerAddress = new ServiceAddress.IceRpc { Path = "/foo" };
            return new (ServiceAddress, string)[]
            {
                (serviceAddress, new Uri("ice://localhost:8080/foo?abc=123#bar").ToString()),
                (serviceAddressWithoutServerAddress, "icerpc:/foo"),
            };
        }
    }

    /// <summary>A collection of service address URIs that are valid URIs but invalid service addresses.</summary>
    private static readonly string[] _invalidServiceAddressUris = new string[]
        {
            "icerpc://host/path?alt-server=", // alt-server authority cannot be empty
            "icerpc://host/path?alt-server=/foo", // alt-server cannot have a path
            "icerpc://host/path?alt-server=icerpc://host", // alt-server cannot have a scheme
            "icerpc:path",                  // bad path
            "icerpc:/host/path#fragment",   // bad fragment
            "icerpc:/path#fragment",        // bad fragment
            "icerpc://user@host/path",      // bad user info
            "icerpc:/path?foo=bar",         // icerpc service address parameter
            "icerpc://host/path?foo=bar",   // icerpc server address parameter
            "ice://host/s1/s2/s3",          // too many slashes in path
            "ice:/path?alt-server=foo",     // alt-server service address parameter
            "ice:/path?adapter-id",         // empty adapter-id
            "ice:/path?adapter-id=foo&foo", // extra parameter
            "http://host/path",             // unknown protocol
        };

    /// <summary>A collection of service address URI strings that are valid, with its expected path and fragment.
    /// </summary>
    private static readonly (string UriString, string Path, string Fragment)[] _validServiceAddressUris =
        new (string, string, string)[]
        {
            /* spellchecker:disable */
            ("ice://host.zeroc.com/identity#facet", "/identity", "facet"),
            ("ice://host.zeroc.com/identity#facet#?!$x", "/identity", "facet#?!$x"),
            ("ice://host.zeroc.com/identity#", "/identity", ""),
            ("ice://host.zeroc.com/identity#%24%23f", "/identity", "%24%23f"),
            ("ice://host.zeroc.com/identity?xyz=false", "/identity", ""),
            ("ice://host.zeroc.com/identity?xyz=true", "/identity", ""),
            ("ice://host/cat/", "/cat/", ""),
            ("ice://host/", "/", ""),
            ("ice://host//", "//", ""),
            ("ice:/path?adapter-id=foo", "/path", ""),
            ("icerpc://host.zeroc.com", "/", ""),
            ("icerpc://host.zeroc.com:1000/category/name", "/category/name", ""),
            ("icerpc://host.zeroc.com:1000/loc0/loc1/category/name", "/loc0/loc1/category/name", ""),
            ("icerpc://host.zeroc.com/category/name%20with%20space", "/category/name%20with%20space", ""),
            ("icerpc://host.zeroc.com/category/name with space", "/category/name%20with%20space", ""),
            ("icerpc://host.zeroc.com//identity", "//identity", ""),
            ("icerpc://host.zeroc.com//identity?alt-server=host2.zeroc.com", "//identity", ""),
            ("icerpc://host.zeroc.com//identity?alt-server=host2.zeroc.com:10000", "//identity", ""),
            ("icerpc://[::1]:10000/identity?alt-server=host1:10000,host2,host3,host4", "/identity", ""),
            ("icerpc://[::1]:10000/identity?alt-server=host1:10000&alt-server=host2,host3&alt-server=[::2]",
             "/identity",
             ""),
            ("ice:/location/identity#facet", "/location/identity", "facet"),
            ("ice:///location/identity#facet", "/location/identity", "facet"), // we tolerate an empty host
            ("icerpc://host.zeroc.com//identity", "//identity", ""),
            ("ice://host.zeroc.com/\x7f€$%/!#$'()*+,:;=@[] %2F", "/%7F%E2%82%AC$%25/!", "$'()*+,:;=@[]%20%2F"),
            ("ice://host.zeroc.com/foo##", "/foo", "#"),
            ("ice://host.zeroc.com/identity#\x7f€$%/!$'()*+,:;=@[] %2F", "/identity", "%7F%E2%82%AC$%25/!$'()*+,:;=@[]%20%2F"),
            (@"icerpc://host.zeroc.com/foo\bar\n\t!", "/foo/bar/n/t!", ""), // \ becomes / another syntax for empty port
            ("icerpc://host.zeroc.com:/identity", "/identity", ""),
            ("icerpc://host.zeroc.com/identity?transport=100", "/identity", ""),
            // leading :: to make the address IPv6-like
            ("icerpc://[::ab:cd:ef:00]/identity?transport=bt", "/identity", ""),
            ("icerpc://host.zeroc.com:10000/identity?transport=tcp", "/identity", ""),
            ("icerpc://mylocation.domain.com/foo/bar?transport=loc", "/foo/bar", ""),
            ("icerpc://host:10000?transport=coloc", "/", ""),
            ("icerpc:/tcp -p 10000", "/tcp%20-p%2010000", ""), // not recommended
            ("ice://0.0.0.0/identity#facet", "/identity", "facet"), // Any IPv4 in service address server address (unusable but parses ok)
            ("ice://[::0]/identity#facet", "/identity", "facet"), // Any IPv6 in service address (unusable but parses ok)
            // IDN
            ("icerpc://München-Ost:10000/path", "/path", ""),
            ("icerpc://xn--mnchen-ost-9db.com/path", "/path", ""),
            /* spellchecker:enable */
        };

    private static readonly Dictionary<string, ServerAddress[]> _altServerAddresses = new()
    {
        ["icerpc://localhost/path?alt-server=host1,host2"] = new ServerAddress[]
        {
            new ServerAddress(Protocol.IceRpc) { Host = "host1" },
            new ServerAddress(Protocol.IceRpc) { Host = "host2" },
        },
        ["icerpc://localhost/path?alt-server=host1:10001,host2:10002"] = new ServerAddress[]
        {
            new ServerAddress(Protocol.IceRpc) { Host = "host1", Port = 10001 },
            new ServerAddress(Protocol.IceRpc) { Host = "host2", Port = 10002 },
        },
        ["icerpc://localhost/path?alt-server=host1:10001&alt-server=host2:10002"] = new ServerAddress[]
        {
            new ServerAddress(Protocol.IceRpc) { Host = "host1", Port = 10001 },
            new ServerAddress(Protocol.IceRpc) { Host = "host2", Port = 10002 },
        },
    };

    /// <summary>Verifies that the adapter ID of a service address is unescaped, and escaped again in its URI.
    /// </summary>
    [Test]
    public void Adapter_id_is_unescaped()
    {
        var serviceAddress = new ServiceAddress.Ice(new Uri("ice:/hello?adapter-id=my%20adapter%25"));

        Assert.That(serviceAddress.AdapterId, Is.EqualTo("my adapter%"));
        Assert.That(serviceAddress.ToString(), Is.EqualTo("ice:/hello?adapter-id=my%20adapter%25"));
    }

    /// <summary>Verifies that the service address server address cannot be set when the service address has an
    /// adapter ID.</summary>
    [Test]
    public void Cannot_set_server_address_on_a_service_address_with_an_adapter_id()
    {
        // Arrange
        var serviceAddress = new ServiceAddress.Ice { AdapterId = "value" };

        // Act/Assert
        Assert.That(
            () => serviceAddress with { ServerAddress = new ServerAddress(Protocol.Ice) { Host = "localhost" } },
            Throws.InvalidOperationException);
    }

    /// <summary>Verifies that the service address cannot contain alt servers when the service address server address is
    /// null.</summary>
    [Test]
    public void Service_address_cannot_contain_alt_server_when_server_address_is_null()
    {
        // Arrange
        // Construct a serviceAddress from a protocol since it will have an empty serverAddress.
        var serviceAddress = new ServiceAddress.IceRpc();

        // Constructing alternate server addresses.
        var altServerAddresses = ImmutableList.Create(new ServerAddress(
            new Uri("icerpc://localhost:10000?transport=foobar")));

        // Act/Assert
        Assert.That(
            () => serviceAddress with { AltServerAddresses = altServerAddresses },
            Throws.InvalidOperationException);
    }

    /// <summary>Verifies that the service address server address cannot be null when the service address contains has
    /// alt server addresses.</summary>
    [Test]
    public void Cannot_clear_server_address_when_alt_server_is_not_empty()
    {
        // Arrange
        // Creating a proxy with an alternate serverAddress.
        var serviceAddress =
            new ServiceAddress.IceRpc(new Uri("icerpc://localhost:8080/foo?alt-server=localhost:10000"));

        // Act/Assert
        Assert.That(() => serviceAddress with { ServerAddress = null }, Throws.InvalidOperationException);
    }

    /// <summary>Verifies that the adapter ID cannot be set when the service address has a server address.</summary>
    [Test]
    public void Cannot_set_adapter_id_on_a_service_address_with_a_server_address()
    {
        var serviceAddress = new ServiceAddress.Ice(new Uri("ice://localhost/hello"));

        Assert.That(() => serviceAddress with { AdapterId = "value" }, Throws.InvalidOperationException);
    }

    /// <summary>Verifies that a service address can be converted into a string.</summary>
    /// <param name="serviceAddress">The service address.</param>
    [Test]
    [TestCaseSource(nameof(ServiceAddressToStringSource))]
    public void Convert_a_service_address_to_a_string(ServiceAddress serviceAddress)
    {
        string str2 = serviceAddress.ToString();

        Assert.That(new ServiceAddress(new Uri(str2)), Is.EqualTo(serviceAddress));
    }

    /// <summary>Verifies that two equal proxies always produce the same hash code.</summary>
    /// <param name="serviceAddress1">The service address to test.</param>
    [Test]
    [TestCaseSource(nameof(ServiceAddressHashCodeSource))]
    public void Equal_service_addresses_produce_the_same_hash_code(ServiceAddress serviceAddress1)
    {
        var serviceAddress2 = new ServiceAddress(new Uri(serviceAddress1.ToString()));

        int hashCode1 = serviceAddress1.GetHashCode();

        Assert.That(serviceAddress1, Is.EqualTo(serviceAddress2));
        Assert.That(hashCode1, Is.EqualTo(serviceAddress1.GetHashCode()));
        Assert.That(hashCode1, Is.EqualTo(serviceAddress2.GetHashCode()));
    }

    /// <summary>Verifies that a service address created from a protocol and a path has the expected protocol, path
    /// and serverAddress properties.</summary>
    [TestCase("/")]
    [TestCase("/foo/bar/")]
    public void From_protocol_and_path(string path)
    {
        ServiceAddress serviceAddress = new ServiceAddress.IceRpc { Path = path };

        Assert.That(serviceAddress.Protocol, Is.EqualTo(Protocol.IceRpc));
        Assert.That(serviceAddress.Path, Is.EqualTo(path));
        Assert.That(serviceAddress.ServerAddress, Is.Null);
    }

    [Test]
    public void Invalid_fragment_throws_exception()
    {
        // Arrange
        var serviceAddress = new ServiceAddress.Ice();

        // Act/Assert
        Assert.That(() => serviceAddress with { Fragment = "foo<" }, Throws.ArgumentException);
    }

    [TestCase("icerpc", "/foo<")]
    [TestCase("ice", "/a/b/c")]
    public void Invalid_path_throws_exception(string protocol, string path)
    {
        // Arrange
        ServiceAddress serviceAddress = Protocol.Parse(protocol).CreateServiceAddress();

        // Act/Assert
        Assert.That(() => serviceAddress.WithPath(path), Throws.ArgumentException);
    }

    /// <summary>Verifies that a service address can be created from a URI.</summary>
    /// <param name="uri">The URI to create the service address from.</param>
    /// <param name="path">The expected path for the parsed service address</param>
    /// <param name="fragment">The expected fragment for the parsed service address</param>
    [Test]
    [TestCaseSource(nameof(ServiceAddressUriSource))]
    public void Create_service_address_from_uri(Uri uri, string path, string fragment)
    {
        var serviceAddress = new ServiceAddress(uri);

        Assert.That(serviceAddress.Path, Is.EqualTo(path));
        Assert.That(serviceAddress is ServiceAddress.Ice ice ? ice.Fragment : "", Is.EqualTo(fragment));
    }

    /// <summary>Verifies that an invalid URI results in an <see cref="ArgumentException" />.</summary>
    /// <param name="uri">The URI to parse as a service address</param>
    [Test]
    [TestCaseSource(nameof(ServiceAddressInvalidUriSource))]
    public void Create_service_address_from_invalid_uri(Uri uri) =>
        Assert.That(() => new ServiceAddress(uri), Throws.ArgumentException);

    [Test]
    [TestCaseSource(nameof(AltServerAddressesSource))]
    public void Create_service_address_with_alt_server(
        ServiceAddress serviceAddress,
        ServerAddress[] altServerAddresses) =>
        Assert.That(serviceAddress.AltServerAddresses, Is.EqualTo(altServerAddresses));

    [Test]
    [TestCaseSource(nameof(ServiceAddressToUriSource))]
    public void Service_address_to_uri(ServiceAddress serviceAddress, string expected)
    {
        // Act
        var result = serviceAddress.ToUri();

        // Assert
        Assert.That(result.ToString(), Is.EqualTo(expected));
    }

    [Test]
    [TestCaseSource(nameof(ServiceAddressEqualitySource))]
    public void Service_address_equality(ServiceAddress serviceAddress1, ServiceAddress? serviceAddress2, bool expected)
    {
        // Act
        bool result = serviceAddress1 == serviceAddress2;

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    [TestCaseSource(nameof(ServiceAddressToStringWithSetupSource))]
    public void Service_address_to_string(ServiceAddress serviceAddress, string expected)
    {
        // Act
        string result = serviceAddress.ToString();

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    /// <summary>Verifies that setting the alt servers containing server addresses that uses a protocol different than
    /// the proxy protocol throws <see cref="ArgumentException" />.</summary>
    [Test]
    public void Setting_alt_server_with_a_different_protocol_fails()
    {
        // Arrange
        var serviceAddress = new ServiceAddress.Ice(new Uri("ice://host.zeroc.com:10000/hello"));
        var altServerAddresses = new ServerAddress[]
        {
            new ServerAddress(Protocol.Ice),
            new ServerAddress(Protocol.IceRpc)
        }.ToImmutableList();

        // Act/Assert
        Assert.That(() => serviceAddress with { AltServerAddresses = altServerAddresses }, Throws.ArgumentException);
    }

    /// <summary>Verifies that setting a server address that uses a protocol different than the service address protocol
    /// throws <see cref="ArgumentException" />.</summary>
    [Test]
    public void Setting_server_address_with_a_different_protocol_fails()
    {
        var serviceAddress = new ServiceAddress.Ice(new Uri("ice://host.zeroc.com/hello"));
        ServerAddress newServerAddress = new ServerAddress(Protocol.IceRpc) { Host = "host.zeroc.com" };

        Assert.That(() => serviceAddress with { ServerAddress = newServerAddress }, Throws.ArgumentException);
    }

    /// <summary>Verifies that we can set the fragment on an ice service address</summary>
    [Test]
    public void Set_fragment_on_an_ice_service_address()
    {
        var serviceAddress = new ServiceAddress.Ice();

        serviceAddress = serviceAddress with { Fragment = "bar" };

        Assert.That(serviceAddress.Fragment, Is.EqualTo("bar"));
    }

    /// <summary>Verifies that the variant of a service address created from a URI matches the URI scheme.</summary>
    [TestCase("ice://host/path", true)]
    [TestCase("icerpc://host/path", false)]
    public void Service_address_variant_matches_uri_scheme(Uri uri, bool isIce)
    {
        var serviceAddress = new ServiceAddress(uri);

        Assert.That(serviceAddress is ServiceAddress.Ice, Is.EqualTo(isIce));
        Assert.That(serviceAddress is ServiceAddress.IceRpc, Is.EqualTo(!isIce));
    }

    /// <summary>Verifies that the constructor of a variant rejects a URI with the scheme of the other variant.
    /// </summary>
    [Test]
    public void Variant_constructor_rejects_uri_with_other_scheme()
    {
        Assert.That(() => new ServiceAddress.Ice(new Uri("icerpc://host/path")), Throws.ArgumentException);
        Assert.That(() => new ServiceAddress.IceRpc(new Uri("ice://host/path")), Throws.ArgumentException);
    }

    [TestCase("icerpc://127.0.0.1/path?transport=foo", "icerpc://127.0.0.1:4062/path?transport=foo")]
    [TestCase("ice:/path?adapter-id=a%20b", "ice:/path?adapter-id=a b")]
    public void Service_address_equal(ServiceAddress lhs, ServiceAddress rhs) => Assert.That(lhs, Is.EqualTo(rhs));

    [TestCase("icerpc://127.0.0.1/path", "icerpc://localhost/path")]
    [TestCase("icerpc://127.0.0.1/path", "ice://127.0.0.1/path")]
    [TestCase("ice://127.0.0.1/path#foo", "ice://127.0.0.1/path#bar")]
    [TestCase("ice:/path?adapter-id=foo", "ice:/path?adapter-id=bar")]
    public void Service_address_not_equal(ServiceAddress lhs, ServiceAddress rhs) =>
        Assert.That(lhs, Is.Not.EqualTo(rhs));

    /// <summary>Verifies that the protocol constructor creates the default service address of the protocol.</summary>
    [Test]
    public void Protocol_constructor_creates_the_default_variant()
    {
        Assert.That(new ServiceAddress(Protocol.Ice), Is.EqualTo((ServiceAddress)new ServiceAddress.Ice()));
        Assert.That(new ServiceAddress(Protocol.IceRpc), Is.EqualTo((ServiceAddress)new ServiceAddress.IceRpc()));
    }

    /// <summary>Verifies that a with expression on a service address keeps its variant.</summary>
    [TestCase("ice:/foo", "/bar")]
    [TestCase("icerpc://host/foo", "/bar")]
    public void With_expression_keeps_the_variant(Uri uri, string path)
    {
        var serviceAddress = new ServiceAddress(uri);

        ServiceAddress result = serviceAddress with { Path = path };

        Assert.That(result.Protocol, Is.EqualTo(serviceAddress.Protocol));
        Assert.That(result.Path, Is.EqualTo(path));
        Assert.That(result.ServerAddress, Is.EqualTo(serviceAddress.ServerAddress));
    }

    /// <summary>Verifies that a property of a service address that holds no variant cannot be initialized.
    /// </summary>
    [Test]
    public void Initializing_a_property_of_an_empty_service_address_fails() =>
        Assert.That(() => default(ServiceAddress) with { Path = "/foo" }, Throws.TypeOf<SwitchExpressionException>());
}
