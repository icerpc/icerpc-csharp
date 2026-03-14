// Copyright (c) ZeroC, Inc.

namespace IceRpc;

/// <summary>Instructs the Service generator to implement <see cref="IDispatcher" /> by generating an implementation of
/// <see cref="IDispatcher.DispatchAsync"/> in the class on which this attribute is applied.
/// Make sure to mark your class as <see langword="partial"/> when you apply this attribute. Your class should also
/// implement one or more interfaces <c>I*Name*Service</c> generated from Ice or Slice interfaces, or Protobuf
/// services.
/// The generated implementation of <c>DispatchAsync</c> routes requests to the appropriate method based on the
/// operation name carried by the incoming request.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ServiceAttribute : Attribute
{
}
