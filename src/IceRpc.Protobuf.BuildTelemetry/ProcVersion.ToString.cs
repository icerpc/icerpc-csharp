// Copyright (c) ZeroC, Inc.

namespace IceRpc.Protobuf.BuildTelemetry;

internal partial record struct ProtocVersion
{
    public override string ToString() => $"{Major}.{Minor}.{Patch}{(string.IsNullOrEmpty(Suffix) ? "" : $"-{Suffix}")}";
}
