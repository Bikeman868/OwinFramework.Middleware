# Owin Framework middleware test server

Use this test server to explore the middleware compoennts that were developed by the team that created the framework.

## Getting started

1. Open this solution in Visual Studio.
2. In the Solution Explorer right click on the TestServer project and choose Debug|Start new instance from the menu.
3. Open your browser and go to http://localhost:12345/owin/pipeline

This will show you the middleware components that are currently configured in the test server.
You can browse to various URLs documented below to see how these middleware components handle requests.
To experiment try opening the `Startup.cs` file and making some changes before running the server again.

## Some URLs you can try

http://localhost:12345/owin/pipeline

http://localhost:12345/owin/pipeline/docs/configuration
