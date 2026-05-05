// Copyright (c) ZeroC, Inc.

using System.Collections;

namespace IceRpc.Features;

/// <summary>Default implementation of <see cref="IRequestContextFeature" />.</summary>
/// <remarks>This class implements <see cref="IEnumerable" />, <see cref="Add" /> and the indexer operator to allow you
/// to use a collection initializer.</remarks>
public sealed class RequestContextFeature : IRequestContextFeature, IEnumerable<KeyValuePair<string, string>>
{
    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> Value => _dictionary;

    private readonly Dictionary<string, string> _dictionary;

    /// <summary>Gets or sets an entry in <see cref="Value" />.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The value.</returns>>
    public string this[string key]
    {
        get => _dictionary[key];
        set => _dictionary[key] = value;
    }

    /// <summary>Constructs an empty request context feature.</summary>
    public RequestContextFeature() => _dictionary = new Dictionary<string, string>();

    /// <summary>Constructs a request context feature wrapping the specified dictionary.</summary>
    /// <param name="dictionary">The dictionary that the new feature will hold. The feature stores the reference; it
    /// does not copy. The caller must not mutate <paramref name="dictionary"/> after constructing the feature, since
    /// the request context is encoded lazily when the request is sent.</param>
    public RequestContextFeature(Dictionary<string, string> dictionary) => _dictionary = dictionary;

    /// <summary>Adds a new entry to <see cref="Value" />.</summary>
    /// <param name="key">The key of the new entry.</param>
    /// <param name="value">The value of the new entry.</param>
    public void Add(string key, string value) => _dictionary.Add(key, value);

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _dictionary.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_dictionary).GetEnumerator();
}
