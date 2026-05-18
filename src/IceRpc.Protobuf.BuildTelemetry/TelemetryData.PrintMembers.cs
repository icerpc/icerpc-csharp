// Copyright (c) ZeroC, Inc.

using System.Text;

namespace IceRpc.Protobuf.BuildTelemetry;

internal partial record struct TelemetryData
{
    // Override PrintMembers to get a better output for Plugins.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Architecture = ").Append(Architecture);
        builder.Append(", OperatingSystem = ").Append(OperatingSystem);
        builder.Append(", ProtocVersion = ").Append(ProtocVersion);
        builder.Append(", Plugins = [");
        builder.AppendJoin(", ", Plugins.Select(p => $"{p.Name}:{p.Version}"));
        builder.Append("], ServiceCount = ").Append(ServiceCount);
        builder.Append(", RpcCount = ").Append(RpcCount);
        builder.Append(", MessageCount = ").Append(MessageCount);
        return true;
    }
}
