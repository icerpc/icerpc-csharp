// Copyright (c) ZeroC, Inc.

using IceRpc;
using TwoD;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

var areaCalculator = new AreaCalculatorProxy(connection);

// An array of various shapes (C# record classes).
Shape[] shapes =
{
    new Shape.Rectangle(3.0F, 2.0F),
    new Shape.Ellipse(3.0F, 2.0F),
    new Shape.Circle(2.0F),
    new Shape.Square(4.0F),
};

foreach (var shape in shapes)
{
    double area = await areaCalculator.ComputeAreaAsync(shape);
    Console.WriteLine($"The area of {shape} is {area:F2}");
}

await connection.ShutdownAsync();
