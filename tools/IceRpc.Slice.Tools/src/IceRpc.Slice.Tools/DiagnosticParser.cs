// Copyright (c) ZeroC, Inc.

using System.Text.Json;

namespace IceRpc.Slice.Tools;

public static class DiagnosticParser
{
    private static readonly JsonSerializerOptions _options = new() { PropertyNameCaseInsensitive = true };

    public static Diagnostic? Parse(string data) =>
        JsonSerializer.Deserialize<Diagnostic>(data, _options);
}
