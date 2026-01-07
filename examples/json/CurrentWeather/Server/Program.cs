// Copyright (c) ZeroC, Inc.

using CurrentWeatherServer;
using IceRpc;
using System.Security.Cryptography.X509Certificates;

// Load the server certificate.
using var serverCertificate = X509CertificateLoader.LoadPkcs12FromFile(
    "../../../../certs/server.p12",
    password: null,
    keyStorageFlags: X509KeyStorageFlags.Exportable);

// Create our WebService dispatchers.
using var httpClient = new HttpClient();
var geoService = new WebService(httpClient, new Uri("https://geocoding-api.open-meteo.com/v1/search"));
var forecastService = new WebService(httpClient, new Uri("https://api.open-meteo.com/v1/forecast"));

// Create a router that maps service paths to these web services.
Router router = new Router()
    .Map("/v1/search", geoService)
    .Map("/v1/forecast", forecastService);

// Create a server that dispatches requests to the router.
await using var server = new Server(
    router,
    serverAuthenticationOptions: CreateServerAuthenticationOptions(serverCertificate));

// Start listening for incoming connections on the default TCP port 4062.
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
