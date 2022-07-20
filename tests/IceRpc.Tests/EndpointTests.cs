// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class EndpointTests
{
    /// <summary>Provides test case data for
    /// <see cref="Create_endpoint_from_valid_uri(Uri, string, ushort, IDictionary{string, string})"/> test.
    /// </summary>
    private static IEnumerable<TestCaseData> EndpointUriSource
    {
        get
        {
            foreach ((Uri uri,
                      string host,
                      ushort port,
                      IDictionary<string, string>? parameters) in _validEndpoints)
            {
                yield return new TestCaseData(
                    uri,
                    host,
                    port,
                    parameters ?? new Dictionary<string, string>());
            }
        }
    }

    /// <summary>Provides test case data for <see cref="Convert_an_endpoint_into_a_string(Uri)"/> test.</summary>
    private static IEnumerable<TestCaseData> EndpointToStringSource
    {
        get
        {
            foreach ((Uri uri, string _, ushort _, IDictionary<string, string>? _) in _validEndpoints)
            {
                yield return new TestCaseData(uri);
            }
        }
    }

    /// <summary>A collection of valid endpoint strings with its expected host, port, and parameters.</summary>
    private static readonly (Uri Uri, string Host, ushort Port, IDictionary<string, string>? Parameters)[] _validEndpoints =
        new (Uri, string, ushort, IDictionary<string, string>?)[]
        {
            (new Uri("icerpc://host:10000"), "host", 10000, null),
            (new Uri("icerpc://host:10000?transport=foobar"), "host", 10000, new Dictionary<string, string>() { ["transport"] = "foobar" }),
            (new Uri("icerpc://host"), "host", 4062, null),
            (new Uri("icerpc://[::0]"), "::", 4062, null),
            (new Uri("ice://[::0]?foo=bar&xyz=true"),
             "::",
             4061,
             new Dictionary<string, string>() { ["foo"] = "bar", ["xyz"] = "true" }),
            (new Uri("icerpc://[::0]?xyz=false&xyz=true&foo=&b="),
             "::",
             4062,
             new Dictionary<string, string>() { ["xyz"] = "false,true", ["foo"] = "", ["b"] = "" }),
            (new Uri("icerpc://host:10000?xyz=foo"),
             "host",
             10000,
             new Dictionary<string, string> { ["xyz"] = "foo" }),
            (new Uri("icerpc://host:10000?transport=coloc"),
             "host",
             10000,
             new Dictionary<string, string>() { ["transport"] = "coloc" }),
            (new Uri("ice://localhost?transport=tcp"),
             "localhost",
             4061,
             new Dictionary<string, string>{ ["transport"] = "tcp" }),
            (new Uri("ice://host:10000"), "host", 10000, null),
            (new Uri("icerpc://host:10000?xyz"),
             "host",
             10000,
             new Dictionary<string, string>{ ["xyz"] = "" }),
            (new Uri("icerpc://host:10000?xyz&adapter-id=ok"),
             "host",
             10000,
             new Dictionary<string, string> { ["xyz"] = "", ["adapter-id"] = "ok" }),
            (new Uri("IceRpc://host:10000"), "host", 10000, null),
            // parses ok even though not a valid name
            (new Uri("icerpc://host:10000? =bar"),
             "host",
             10000,
             new Dictionary<string, string>() { ["%20"] = "bar" })
        };

    /// <summary>Verifies that an endpoint can be correctly converted into a string.</summary>
    /// <param name="uri1">The endpoint URI to test.</param>
    [Test, TestCaseSource(nameof(EndpointToStringSource))]
    public void Convert_an_endpoint_into_a_string(Uri uri1)
    {
        var endpoint1 = new Endpoint(uri1);

        string str2 = endpoint1.ToString();

        Assert.That(endpoint1, Is.EqualTo(new Endpoint(new Uri(str2))));
    }

    /// <summary>Verifies that the properties of a default constructed endpoint have the expected default values.
    /// </summary>
    [Test]
    public void Endpoint_default_values()
    {
        var endpoint = new Endpoint();

        Assert.Multiple(() =>
        {
            Assert.That(endpoint.Protocol, Is.EqualTo(Protocol.IceRpc));
            Assert.That(endpoint.Host, Is.EqualTo("::0"));
            Assert.That(endpoint.Port, Is.EqualTo(Protocol.IceRpc.DefaultUriPort));
            Assert.That(endpoint.Params, Has.Count.EqualTo(0));
        });
    }

    /// <summary>Verifies that the <see cref="Endpoint.OriginalUri"/> property is set for an endpoint created from an
    /// URI.</summary>
    [Test]
    public void Endpoint_original_URI()
    {
        var endpoint = new Endpoint(new Uri("icerpc://host:10000?transport=foobar"));

        Assert.That(endpoint.OriginalUri, Is.Not.Null);
    }

    /// <summary>Verifies that the <see cref="Endpoint.OriginalUri"/> property of an endpoint is set to null after
    /// modifying the endpoint.</summary>
    [Test]
    public void Endpoint_original_URI_is_null_after_updating_the_endpoint()
    {
        var endpoint = new Endpoint(new Uri("icerpc://host:10000?transport=foobar"));

        Endpoint endpoint2 = endpoint with { Host = "localhost", Port = 10001 };

        Assert.That(endpoint.OriginalUri, Is.Not.Null);
        Assert.That(endpoint2.OriginalUri, Is.Null);
    }

    /// <summary>Verifies that Endpoint's constructor fails when a URI is not a valid endpoint.</summary>
    [TestCase("icerpc://host:10000/category/name")]                // unexpected path
    [TestCase("icerpc://host:10000#fragment")]                     // unexpected fragment
    [TestCase("icerpc://host:10000?alt-endpoint=host2")]           // alt-endpoint is service address only
    [TestCase("icerpc://host:10000?=bar")]                         // empty param name
    [TestCase("icerpc:///foo")]                                    // path, empty authority
    [TestCase("icerpc:///")]                                       // empty authority
    [TestCase("icerpc://")]                                        // empty authority
    [TestCase("icerpc:/foo")]                                      // no authority
    [TestCase("icerpc:")]                                          // no authority
    [TestCase("foo://host:10000")]                                 // protocol not supported
    [TestCase("icerpc://user:password@host:10000")]                // bad user-info
    public void Cannot_create_endpoint_from_non_endpoint_uri(Uri uri) =>
        Assert.Catch<ArgumentException>(() => new Endpoint(uri));

    /// <summary>Verifies that an endpoint can be created from a URI.</summary>
    /// <param name="uri">The endpoint URI.</param>
    /// <param name="host">The expected host for the new endpoint.</param>
    /// <param name="port">The expected port for the new endpoint.</param>
    /// <param name="parameters">The expected parameters for the new endpoint.</param>
    [Test, TestCaseSource(nameof(EndpointUriSource))]
    public void Create_endpoint_from_valid_uri(
        Uri uri,
        string host,
        ushort port,
        IDictionary<string, string> parameters)
    {
        var endpoint = new Endpoint(uri);

        Assert.Multiple(() =>
        {
            Assert.That(endpoint.Host, Is.EqualTo(host));
            Assert.That(endpoint.Port, Is.EqualTo(port));
            Assert.That(endpoint.Params, Is.EquivalentTo(parameters));
        });
    }

    /// <summary>Verifies that setting the host works with a supported host name.</summary>
    /// <param name="host">The value to set the <see cref="Endpoint.Host"/> property to.</param>
    [TestCase("localhost")]
    [TestCase("[::0]")]
    [TestCase("::1")]
    public void Setting_the_endpoint_host(string host)
    {
        var endpoint = new Endpoint(new Uri("icerpc://localhost"));

        endpoint = endpoint with { Host = host };

        Assert.That(endpoint.Host, Is.EqualTo(host));
    }

    [Test]
    public void Construction_with_unsupported_protocol_fails()
    {
        // Arrange
        var unsupportedProtocol = Protocol.FromString("foo");

        // Act / Assert
        Assert.Throws<ArgumentException>(() => new Endpoint(unsupportedProtocol));
    }

    [Test]
    public void Construction_with_relative_uri_fails()
    {
        // Arrange
        var relativeUri = new Uri("foo", UriKind.Relative);

        // Act / Assert
        Assert.Throws<ArgumentException>(() => new Endpoint(relativeUri));
    }

    [Test]
    public void To_Uri_returns_uri_from_Endpoint_uri_constructor()
    {
        // Arrange
        var uri = new Uri("icerpc://bar:1234");
        var endpoint = new Endpoint(uri);

        // Act
        var result = endpoint.ToUri();

        // Assert
        Assert.That(endpoint.OriginalUri, Is.EqualTo(uri));
        Assert.That(result, Is.EqualTo(uri));
    }

    [Test]
    public void Original_uri_set_to_null_when_setting_property()
    {
        // Arrange
        var endpoint = new Endpoint(new Uri("icerpc://localhost"));
        endpoint = endpoint with { Host = "foo" }; // new host invalidates OriginalUri

        // Act
        var endpointUri = endpoint.ToUri();

        // Assert
        Assert.That(endpointUri.Scheme, Is.EqualTo("icerpc"));
        Assert.That(endpointUri.Host, Is.EqualTo("foo"));
        Assert.That(endpoint.OriginalUri, Is.Null);
    }

    /// <summary>Verifies that setting the endpoint parameters works.</summary>
    /// <param name="name">The name of the endpoint parameter to set.</param>
    /// <param name="value">The value of the endpoint parameter to set.</param>
    [TestCase("name", "value")]
    [TestCase("name%23[]", "value%25[]@!")]
    public void Setting_the_endpoint_params(string name, string value)
    {
        var endpoint = new Endpoint(new Uri("icerpc://localhost"));

        endpoint = endpoint with { Params = endpoint.Params.Add(name, value) };

        Assert.That(endpoint.Params.ContainsKey(name), Is.True);
        Assert.That(endpoint.Params[name], Is.EqualTo(value));
    }

    /// <summary>Verifies that trying to set the <see cref="Endpoint.Host"/> to an invalid value throws
    /// <see cref="ArgumentException"/> and the <see cref="Endpoint.Host"/> property remains unchanged.</summary>
    /// <param name="host">The invalid value for <see cref="Endpoint.Host"/> property.</param>
    [TestCase("")]
    [TestCase("::1.2")]
    public void Setting_invalid_endpoint_host_fails(string host)
    {
        var endpoint = new Endpoint(new Uri("icerpc://localhost"));

        Assert.Throws<ArgumentException>(() => _ = endpoint with { Host = host });

        Assert.That(endpoint.Host, Is.EqualTo("localhost"));
    }

    /// <summary>Verifies that trying to add an invalid endpoint parameter throws <see cref="ArgumentException"/> and
    /// the <see cref="Endpoint.Params"/> property remains unchanged.</summary>
    /// <param name="name">The endpoint parameter name.</param>
    /// <param name="value">The endpoint parameter value.</param>
    [TestCase("alt-endpoint", "x")]
    [TestCase("", "value")]
    [TestCase(" name", "value")]
    [TestCase("name", "valu#e")]
    [TestCase("name", "valu&e")]
    public void Setting_invalid_endpoint_params_fails(string name, string value)
    {
        var endpoint = new Endpoint(new Uri("icerpc://localhost"));

        Assert.Throws<ArgumentException>(() => _ = endpoint with { Params = endpoint.Params.Add(name, value) });

        Assert.That(endpoint.Params, Has.Count.EqualTo(0));
    }
}
