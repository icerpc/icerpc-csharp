// Copyright (c) ZeroC, Inc.

namespace IceRpc;

/// <summary>Instructs the IceRPC Service Generator (IceRpc.ServiceGenerator) to implement <see cref="IDispatcher" />
/// by generating an implementation of <see cref="IDispatcher.DispatchAsync"/> in the class on which this attribute is
/// applied.</summary>
/// <remarks>Make sure to mark your class as <see langword="partial"/> when you apply this attribute. Your class should
/// also implement one or more interfaces I{Name}Service generated from Ice or Slice interfaces, or from Protobuf
/// services. The generated implementation of <c>DispatchAsync</c> routes requests to the appropriate method based on
/// the operation name carried by the incoming request.</remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ServiceAttribute : Attribute
{
}
