# Telemetry

`IceRpc.Protobuf.Tools` and `IceRpc.Slice.Tools` collect anonymous
telemetry data about general usage. Participation in this anonymous program is
optional, and you may [opt-out](#opting-out) if you'd prefer to not share any
information.

This package includes the `IceRpc.BuildTelemetry.Reporter` client. This client is an
IceRPC C# client that sends anonymous telemetry data over a secure connection
to the IceRPC telemetry server during the compilation of Slice and Protobuf
files. This data includes:

- The IceRPC version.
- The system's operating system and version.
- The source of the telemetry data (either `IceRpc.Slice.Tools or
 `IceRpc.Protobuf.Tools`).
- A SHA256 hash which is computed from the Slice files or Protobuf files.

This data is used to help the IceRPC team understand how the tools are being
used and to help prioritize future development efforts. The data is stored in a
secure database and is not shared with any third parties.

## Opting Out

To opt out of the telemetry program, add the following property to your
csharp project file:

```xml
<PropertyGroup>
 <DisableTelemetry>true</DisableTelemetry>
</PropertyGroup>
```

This will prevent the telemetry client from sending any data to the IceRPC
telemetry server.
