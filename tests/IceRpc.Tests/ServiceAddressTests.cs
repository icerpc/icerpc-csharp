// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using System.Collections.Immutable;

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
                yield return new TestCaseData(new ServiceAddress(new Uri(str, UriKind.RelativeOrAbsolute)));
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
                yield return new TestCaseData(new Uri(str, UriKind.RelativeOrAbsolute));
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
                yield return new TestCaseData(new Uri(str, UriKind.RelativeOrAbsolute), path, fragment);
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
                yield return new TestCaseData(new ServiceAddress(new Uri(str, UriKind.RelativeOrAbsolute)));
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
            ServiceAddress serviceAddress = new ServiceAddress(Protocol.Ice) with { Path = "/foo" };
            return new[] {
                (serviceAddress, serviceAddress, true),
                (serviceAddress, null, false),
                (serviceAddress, new ServiceAddress(Protocol.IceRpc), false), // Different protocol.
                // Relative service addresses
                (
                    new ServiceAddress() with { Path = "/foo" },
                    new ServiceAddress() with { Path = "/bar" },
                    false
                ),
                //  Params (Order does not matter)
                (
                    new ServiceAddress(new Uri("ice://localhost:8080/foo?abc=123&def=456")),
                    new ServiceAddress(new Uri("ice://localhost:8080/foo?def=456&abc=123")),
                    true
                ),
                //  AltServerAddresses (Order matters)
                (
                    new ServiceAddress(new Uri("ice://localhost:8080/foo?alt-server=localhost:10000,localhost:10101")),
                    new ServiceAddress(new Uri("ice://localhost:8080/foo?alt-server=localhost:10101,localhost:10000")),
                    false
                ),
            };
        }
    }

    private static (ServiceAddress, string)[] ServiceAddressToStringData
    {
        get
        {
            // Service address with alt servers
            var serviceAddressWithAltServerAddresses = new ServiceAddress(new Uri("ice://localhost:8080/foo?abc=123#bar"));
            serviceAddressWithAltServerAddresses = serviceAddressWithAltServerAddresses with
            {
                AltServerAddresses = ImmutableList.Create(
                    new ServerAddress(new Uri("ice://localhost:10000?transport=fizz")),
                    new ServerAddress(new Uri("ice://localhost:10101?transport=buzz"))
                )
            };

            // Service address with Params
            var serviceAddressWithParams = new ServiceAddress(Protocol.IceRpc);
            var myParams = new Dictionary<string, string> { ["foo"] = "bar" }.ToImmutableDictionary();
            serviceAddressWithParams = serviceAddressWithParams with { Params = myParams };

            return new[]
            {
                (
                    serviceAddressWithAltServerAddresses,
                    "ice://localhost:8080/foo?abc=123&alt-server=localhost:10000?transport=fizz,localhost:10101?transport=buzz#bar"
                ),
                (
                    serviceAddressWithParams,
                    "icerpc:/?foo=bar"
                )
            };
        }
    }

    private static (ServiceAddress, string)[] ServiceAddressToUriData
    {
        get
        {
            var serviceAddress = new ServiceAddress(new Uri("ice://localhost:8080/foo?abc=123#bar"));
            ServiceAddress relativeServiceAddress = new ServiceAddress() with { Path = "/foo" };
            ServiceAddress protocolRelativeServiceAddress = new ServiceAddress(Protocol.IceRpc) with { Path = "/foo" };
            return new (ServiceAddress, string)[]
            {
                // OriginalUri set
                (serviceAddress, new Uri("ice://localhost:8080/foo?abc=123#bar").ToString()),
                // Relative service address with no protocol
                (relativeServiceAddress, new Uri("/foo", UriKind.Relative).ToString()),
                // Protocol relative service address
                (protocolRelativeServiceAddress, "icerpc:/foo"),
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
            "ice://host/s1/s2/s3",          // too many slashes in path
            "ice://host/cat/",              // empty identity name
            "ice://host/",                  // empty identity name
            "ice://host//",                 // empty identity name
            "ice:/path?alt-server=foo",     // alt-server service address parameter
            "ice:/path?adapter-id",         // empty adapter-id
            "ice:/path?adapter-id=foo&foo", // extra parameter
            "http://host/path",             // unknown protocol
        };

    /// <summary>A collection of service address URI strings that are valid, with its expected path and fragment.
    /// </summary>
    private static readonly (string uriString, string Path, string Fragment)[] _validServiceAddressUris = new (string, string, string)[]
        {
            /* spellchecker:disable */
            ("icerpc://host.zeroc.com/path?encoding=foo", "/path", ""),
            ("ice://host.zeroc.com/identity#facet", "/identity", "facet"),
            ("ice://host.zeroc.com/identity#facet#?!$x", "/identity", "facet#?!$x"),
            ("ice://host.zeroc.com/identity#", "/identity", ""),
            ("ice://host.zeroc.com/identity#%24%23f", "/identity", "%24%23f"),
            ("ice://host.zeroc.com/identity?xyz=false", "/identity", ""),
            ("ice://host.zeroc.com/identity?xyz=true", "/identity", ""),
            ("ice:/path?adapter-id=foo", "/path", ""),
            ("icerpc:?foo=bar", "/", ""),
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
            ("icerpc://[::1]/path?alt-server=host1?adapter-id=foo=bar$name=value&alt-server=host2?foo=bar$123=456",
             "/path",
             ""),
            ("ice:/location/identity#facet", "/location/identity", "facet"),
            ("ice:///location/identity#facet", "/location/identity", "facet"), // we tolerate an empty host
            ("icerpc://host.zeroc.com//identity", "//identity", ""),
            ("ice://host.zeroc.com/\x7f€$%/!#$'()*+,:;=@[] %2F", "/%7F%E2%82%AC$%25/!", "$'()*+,:;=@[]%20%2F"),
            // TODO: add test with # in fragment
            ("ice://host.zeroc.com/identity#\x7f€$%/!$'()*+,:;=@[] %2F", "/identity", "%7F%E2%82%AC$%25/!$'()*+,:;=@[]%20%2F"),
            (@"icerpc://host.zeroc.com/foo\bar\n\t!", "/foo/bar/n/t!", ""), // \ becomes / another syntax for empty port
            ("icerpc://host.zeroc.com:/identity", "/identity", ""),
            ("icerpc://com.zeroc.ice/identity?transport=iaps&option=a,b%2Cb,c&option=d", "/identity", ""),
            ("icerpc://host.zeroc.com/identity?transport=100", "/identity", ""),
            // leading :: to make the address IPv6-like
            ("icerpc://[::ab:cd:ef:00]/identity?transport=bt", "/identity", ""),
            ("icerpc://host.zeroc.com:10000/identity?transport=tcp", "/identity", ""),
            ("icerpc://host.zeroc.com/identity?transport=ws&option=/foo%2520/bar", "/identity", ""),
            ("icerpc://mylocation.domain.com/foo/bar?transport=loc", "/foo/bar", ""),
            ("icerpc://host:10000?transport=coloc", "/", ""),
            ("icerpc:/tcp -p 10000", "/tcp%20-p%2010000", ""), // not recommended
            ("icerpc://host.zeroc.com/identity?transport=ws&option=/foo%2520/bar", "/identity", ""),
            ("ice://0.0.0.0/identity#facet", "/identity", "facet"), // Any IPv4 in service address server address (unusable but parses ok)
            ("ice://[::0]/identity#facet", "/identity", "facet"), // Any IPv6 in service address (unusable but parses ok)
            // IDN
            ("icerpc://München-Ost:10000/path", "/path", ""),
            ("icerpc://xn--mnchen-ost-9db.com/path", "/path", ""),
            // relative proxies
            ("/foo/bar", "/foo/bar", ""),
            ("//foo/bar", "//foo/bar", ""),
            ("/foo:bar", "/foo:bar", ""),
            /* spellchecker:enable */
        };

    private static readonly Dictionary<string, ServerAddress[]> _altServerAddresses = new()
    {
        ["icerpc://localhost/path?alt-server=host1,host2"] = new ServerAddress[]
        {
            new ServerAddress { Host = "host1" },
            new ServerAddress { Host = "host2" },
        },
        ["icerpc://localhost/path?alt-server=host1:10001,host2:10002"] = new ServerAddress[]
        {
            new ServerAddress { Host = "host1", Port = 10001 },
            new ServerAddress { Host = "host2", Port = 10002 },
        },
        ["icerpc://localhost/path?alt-server=host1:10001&alt-server=host2:10002"] = new ServerAddress[]
        {
            new ServerAddress { Host = "host1", Port = 10001 },
            new ServerAddress { Host = "host2", Port = 10002 },
        },
    };

    /// <summary>Verifies that adapter-id param cannot be set to an empty value.</summary>
    [Test]
    public void Adapter_id_cannot_be_empty()
    {
        // Arrange
        var serviceAddress = new ServiceAddress(new Uri("ice://localhost/hello"));
        var myParams = new Dictionary<string, string> { ["adapter-id"] = "" }.ToImmutableDictionary();

        // Act/Assert
        Assert.That(() => serviceAddress = serviceAddress with { Params = myParams }, Throws.ArgumentException);
    }

    [Test]
    public void Cannot_set_server_address_on_relative_proxy()
    {
        // Arrange
        var serviceAddress = new ServiceAddress(new Uri("/foo", UriKind.Relative));

        var serverAddress = new ServerAddress(new Uri("icerpc://localhost:10000?transport=foobar"));

        // Act/Assert
        Assert.Throws<InvalidOperationException>(() =>
            serviceAddress = serviceAddress with { ServerAddress = serverAddress });
    }

    /// <summary>Verifies that the service address server address cannot be set when the service address contains any
    /// params.</summary>
    [Test]
    public void Cannot_set_server_address_on_a_service_address_with_parameters()
    {
        // Arrange
        var serviceAddress = new ServiceAddress(Protocol.Ice)
        {
            Params = new Dictionary<string, string> { ["adapter-id"] = "value" }.ToImmutableDictionary(),
        };

        // Act/Assert
        Assert.That(
            () => serviceAddress = serviceAddress with
            {
                ServerAddress = new ServerAddress(serviceAddress.Protocol!) { Host = "localhost" }
            },
            Throws.TypeOf<InvalidOperationException>());
    }

    /// <summary>Verifies that the service address cannot contain alt servers when the service address server address is
    /// null.</summary>
    [Test]
    public void Service_address_cannot_contain_alt_server_when_server_address_is_null()
    {
        // Arrange
        // Construct a serviceAddress from a protocol since it will have an empty serverAddress.
        var serviceAddress = new ServiceAddress(Protocol.IceRpc);

        // Constructing alternate server addresses.
        var altServerAddresses = ImmutableList.Create(new ServerAddress(new Uri("icerpc://localhost:10000?transport=foobar")));

        // Act/Assert
        Assert.Throws<InvalidOperationException>(() =>
            serviceAddress = serviceAddress with { AltServerAddresses = altServerAddresses }
        );
    }

    /// <summary>Verifies that the service address server address cannot be null when the service address contains has
    /// alt server addresses.</summary>
    [Test]
    public void Cannot_clear_server_address_when_alt_server_is_not_empty()
    {
        // Arrange
        // Creating a proxy with an alternate serverAddress.
        var serviceAddress = new ServiceAddress(new Uri("icerpc://localhost:8080/foo?alt-server=localhost:10000"));

        // Act/Assert
        Assert.Throws<InvalidOperationException>(() => serviceAddress = serviceAddress with { ServerAddress = null });
    }

    /// <summary>Verifies that the "fragment" cannot be set when the protocol is null or has no fragment.</summary>
    [TestCase("icerpc")]
    [TestCase("")]
    public void Cannot_set_fragment_if_protocol_has_no_fragment(string protocolName)
    {
        Protocol? protocol = protocolName.Length > 0 ? Protocol.Parse(protocolName) : null;
        var serviceAddress = new ServiceAddress(protocol);

        Assert.That(() => serviceAddress = serviceAddress with { Fragment = "bar" }, Throws.TypeOf<InvalidOperationException>());

        if (protocol is not null)
        {
            Assert.That(protocol.HasFragment, Is.False);
        }
    }

    /// <summary>Verifies that the service address params cannot be set when the service address has a server address.
    /// </summary>
    [Test]
    public void Cannot_set_params_on_a_service_address_with_a_server_address()
    {
        var serviceAddress = new ServiceAddress(new Uri("icerpc://localhost/hello"));
        var myParams = new Dictionary<string, string> { ["name"] = "value" }.ToImmutableDictionary();

        Assert.That(
            () => serviceAddress = serviceAddress with { Params = myParams },
            Throws.TypeOf<InvalidOperationException>());
    }

    /// <summary>Verifies that a service address can be converted into a string.</summary>
    /// <param name="serviceAddress">The service address.</param>
    [Test, TestCaseSource(nameof(ServiceAddressToStringSource))]
    public void Convert_a_service_address_to_a_string(ServiceAddress serviceAddress)
    {
        string str2 = serviceAddress.ToString();

        Assert.That(new ServiceAddress(new Uri(str2, UriKind.RelativeOrAbsolute)), Is.EqualTo(serviceAddress));
    }

    /// <summary>Verifies that two equal proxies always produce the same hash code.</summary>
    /// <param name="serviceAddress1">The service address to test.</param>
    [Test, TestCaseSource(nameof(ServiceAddressHashCodeSource))]
    public void Equal_service_addresses_produce_the_same_hash_code(ServiceAddress serviceAddress1)
    {
        var serviceAddress2 = new ServiceAddress(new Uri(serviceAddress1.ToString(), UriKind.RelativeOrAbsolute));

        int hashCode1 = serviceAddress1.GetHashCode();

        Assert.That(serviceAddress1, Is.EqualTo(serviceAddress2));
        Assert.That(hashCode1, Is.EqualTo(serviceAddress1.GetHashCode()));
        Assert.That(hashCode1, Is.EqualTo(serviceAddress2.GetHashCode()));
    }

    /// <summary>Verifies that a service address created from a path has the expected protocol, path and serverAddress
    /// properties.</summary>
    [TestCase("/")]
    [TestCase("/foo/bar/")]
    public void From_path(string path)
    {
        var serviceAddress = new ServiceAddress { Path = path };

        Assert.That(serviceAddress.Protocol, Is.Null);
        Assert.That(serviceAddress.Path, Is.EqualTo(path));
        Assert.That(serviceAddress.ServerAddress, Is.Null);
    }

    [Test]
    public void Invalid_fragment_throws_exception()
    {
        // Arrange
        var serviceAddress = new ServiceAddress(Protocol.IceRpc);

        // Act/Assert
        Assert.Throws<ArgumentException>(() => serviceAddress = serviceAddress with { Fragment = "foo<" });
    }

    [Test]
    public void Invalid_path_throws_exception()
    {
        // Arrange
        var serviceAddress = new ServiceAddress(Protocol.IceRpc);

        // Act/Assert
        Assert.Throws<ArgumentException>(() => serviceAddress = serviceAddress with { Path = "foo<" });
    }

    /// <summary>Verifies that a service address can be created from a URI.</summary>
    /// <param name="uri">The URI to create the service address from.</param>
    /// <param name="path">The expected path for the parsed service address</param>
    /// <param name="fragment">The expected fragment for the parsed service address</param>
    [Test, TestCaseSource(nameof(ServiceAddressUriSource))]
    public void Create_service_address_from_uri(Uri uri, string path, string fragment)
    {
        var serviceAddress = new ServiceAddress(uri);

        Assert.That(serviceAddress.Path, Is.EqualTo(path));
        Assert.That(serviceAddress.Fragment, Is.EqualTo(fragment));
    }

    /// <summary>Verifies that an invalid URI results in an <see cref="ArgumentException" />.</summary>
    /// <param name="uri">The URI to parse as a service address</param>
    [Test, TestCaseSource(nameof(ServiceAddressInvalidUriSource))]
    public void Create_service_address_from_invalid_uri(Uri uri) =>
        Assert.Throws(Is.InstanceOf<ArgumentException>(), () => new ServiceAddress(uri));

    [Test, TestCaseSource(nameof(AltServerAddressesSource))]
    public void Create_service_address_with_alt_server(
        ServiceAddress serviceAddress,
        ServerAddress[] altServerAddresses) =>
        Assert.That(serviceAddress.AltServerAddresses, Is.EqualTo(altServerAddresses));

    [Test, TestCaseSource(nameof(ServiceAddressToUriSource))]
    public void Relative_service_address_to_uri(ServiceAddress serviceAddress, string expected)
    {
        // Act
        var result = serviceAddress.ToUri();

        // Assert
        Assert.That(result.ToString(), Is.EqualTo(expected));
    }

    [Test, TestCaseSource(nameof(ServiceAddressEqualitySource))]
    public void Service_address_equality(ServiceAddress serviceAddress1, ServiceAddress? serviceAddress2, bool expected)
    {
        // Act
        bool result = serviceAddress1 == serviceAddress2;

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test, TestCaseSource(nameof(ServiceAddressToStringWithSetupSource))]
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
        var serviceAddress = new ServiceAddress(new Uri("ice://host.zeroc.com:10000/hello"));
        var altServerAddresses = new ServerAddress[]
        {
            new ServerAddress(Protocol.Ice),
            new ServerAddress(Protocol.IceRpc)
        }.ToImmutableList();

        // Act/Assert
        Assert.That(() =>
            serviceAddress = serviceAddress with { AltServerAddresses = altServerAddresses }, Throws.ArgumentException
        );

        // Ensure the alt servers weren't updated
        Assert.That(serviceAddress.AltServerAddresses, Is.Empty);
    }

    /// <summary>Verifies that setting a server address that uses a protocol different than the service address protocol
    /// throws <see cref="ArgumentException" />.</summary>
    [Test]
    public void Setting_server_address_with_a_different_protocol_fails()
    {
        var serviceAddress = new ServiceAddress(new Uri("ice://host.zeroc.com/hello"));
        ServerAddress? serverAddress = serviceAddress.ServerAddress;
        ServerAddress newServerAddress = new ServiceAddress(new Uri("icerpc://host.zeroc.com/hello")).ServerAddress!.Value;

        Assert.That(() => serviceAddress = serviceAddress with { ServerAddress = newServerAddress }, Throws.ArgumentException);

        // Ensure the server address wasn't updated
        Assert.That(serviceAddress.ServerAddress, Is.EqualTo(serverAddress));
    }

    /// <summary>Verifies that we can set the fragment on an ice service address</summary>
    [Test]
    public void Set_fragment_on_an_ice_service_address()
    {
        var serviceAddress = new ServiceAddress(Protocol.Ice);

        serviceAddress = serviceAddress with { Fragment = "bar" };

        Assert.That(serviceAddress.Fragment, Is.EqualTo("bar"));
        Assert.That(serviceAddress.Protocol!.HasFragment, Is.True);
    }

    [Test]
    public void Uri_constructor_with_relative_uri_produces_relative_service_address()
    {
        // Arrange
        var uri = new Uri("/foo", UriKind.Relative);

        // Act
        var serviceAddress = new ServiceAddress(uri);

        // Assert
        Assert.That(serviceAddress.Path, Is.EqualTo("/foo"));
        Assert.That(serviceAddress.Protocol, Is.Null);
    }
}
