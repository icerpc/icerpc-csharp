// Copyright (c) ZeroC, Inc.

using System.Text.Json;

namespace IceRpc.Slice.Tools;

public static class JSONParser
{
    private static readonly JsonSerializerOptions _options = new() { PropertyNameCaseInsensitive = true };

    public static T? Parse<T>(string data) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(data, _options);
        }
        catch (JsonException)
        {
        }
        return null;
    }
}
