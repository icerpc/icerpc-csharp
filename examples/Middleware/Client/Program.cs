using Demo;
using IceRpc;
using IceRpc.Features;

// Establish the connection to the server
await using var connection = new ClientConnection("icerpc://127.0.0.1");
IHelloPrx hello = HelloPrx.FromConnection(connection);

Console.Write("To say authenticate with the server you must type the password: ");

if (Console.ReadLine() is string password)
{
    Console.Write("To say hello to the server, type your name: ");

    if (Console.ReadLine() is string name)
    {
        // Creating a dictionary to use as a feature
        Dictionary<string, string> headers = new Dictionary<string, string>();
        headers.Add("Authorization", password);

        // Creating a feature collection and setting the headers dictionary as a feature
        IFeatureCollection features = new FeatureCollection();
        features.Set<Dictionary<string, string>>(headers);

        Console.WriteLine(await hello.SayHelloAsync(name, features));
    }
}
