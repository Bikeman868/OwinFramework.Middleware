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

http://localhost:12345/

http://localhost:12345/ui

http://localhost:12345/does-not-exist

## What is what here?

`Package.cs` tells the Ioc.Modules package which interfaces we are implementing in our application.

`Packages.config` defines the NuGet packages that this application needs to run.

`Program.cs` is a console application that listens for http requests using the OWIN self-hosting package.
After starting the application you can open your broswer and make requests to this server.

`Startup.cs` is the OWIN entry point, it builds the OWIN pipeline. When the server receives a request
from a browser it will pass it down the pipeline until a middleware component decides that it can handle
the request. Each Middleware can choose the pass on the request to the next middleware in the pipeline or
not. Each middleware can also choose to modify the request, add something to the request context or
change the response, and it can do these things before running the next middleware or after the next
middleware has run.

`config.json` is the configuration file for this application. This test server uses `Urchin` for its
configuration but this is not required by the Owin Framework which allows you to use any configuration
method you like.

The rest I think you can figure out, but email me if you have any questions.
