// Copyright (c) ZeroC, Inc.

using System.Text.Json.Serialization;

namespace IceRpc.Slice.Tools;

public struct Location
{
    public int Row { get; set; }
    [JsonPropertyName("col")]
    public int Column { get; set; }
}
