// Copyright (c) ZeroC, Inc.

using System.Text.Json.Serialization;

namespace IceRpc.Slice.Tools;

public class BuildTelemetry
{
    [JsonPropertyName("hash")]
    public string CompilationHash { get; set; } = "";

    [JsonPropertyName("contains_slice1")]
    public bool ContainsSlice1 { get; set; }

    [JsonPropertyName("contains_slice2")]
    public bool ContainsSlice2 { get; set; }

    [JsonPropertyName("src_file_count")]
    public int SourceFileCount { get; set; }

    [JsonPropertyName("ref_file_count")]
    public int ReferenceFileCount { get; set; }
}
