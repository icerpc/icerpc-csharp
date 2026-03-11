// Copyright (c) ZeroC, Inc.

namespace IceRpc;

/// <summary>Instructs the Service Generator (a C# source generator) to implement <see cref="IDispatcher"/> by
/// generating an implementation of <see cref="IDispatcher.DispatchAsync"/> in the class on which this attribute is
/// applied. Make sure to mark your class as <see langword="partial"/> when you apply this attribute.
/// Your class should also implement one or more interfaces <c>I*Name*Service</c> generated from Ice or Slice
/// interfaces, or Protobuf services.
/// The generated implementation of <c>DispatchAsync</c> routes requests to the appropriate method based on the
/// operation name carried by the incoming request.
/// </summary>
/// <remarks>Apply this attribute to any class for which you want the Service Generator to generate a
/// <c>DispatchAsync</c> method. This attribute is not inherited by derived classes.</remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ServiceAttribute : Attribute
{
}
