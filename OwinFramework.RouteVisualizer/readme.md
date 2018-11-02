This middleware is useful for during development and for diagnosing issues in production.
It should not generally be exposed to the public users of your site.

This middleware can be configured to any URL within your site, and responds at that URL
with an SVG drawing of the OWIN Framework pipeline. The drawing includes routers, routes
and middleware. For middleware that implement optional interfaces like `IAnalysable` and 
`ISelfDocumenting` the visualizer will use these interfaces to extract additional information
and include that on the drawing.

## Configuration

The OWIN Framework supports any configuration mechanism you choose. At the time of writing 
it comes bundled with support for Urchin and `web.config` configuration, but the 
`IConfiguration` interface is trivial to implement.

With the OWIN Framework the application developer chooses where in the configuration structure
each middleware will read its configuration from (this is so that you can have more than one
of the same middleware in your pipeline with different configurations).

For supported configuration options see the `RouteVisualizerConfiguration.cs` file in this folder. This
middleware is also self documenting, and can produce configuration documentation from within.

The most important configuration value is the `path` which defaults to `/owin/visualization`. 
This is the URL within your application where the visualizer will be available.

This is an example of adding the not found middleware to the OWIN Framework pipeline builder.

```
builder.Register(ninject.Get<OwinFramework.RouteVisualizer.RouteVisualizerMiddleware>())
    .As("Route visualizer")
    .ConfigureWith(config, "/middleware/routeVisualizer");
```

If you use the above code, and you use [Urchin](https://github.com/Bikeman868/Urchin) for 
configuration management then your configuration file can be set up like this:

```
{
    "middleware": {
        "routeVisualizer": {
            "path": "/config/routes",
			"enabled": true,
			"requiredPermission":"developer"
        }
    }
}

```

This configuration specifies that:

* The route visualization will be available at http://mycompany.com/config/routes. Please ensure that 
  the route visualizer middleware is configured on the route that this request will be routed to.
* The route visualizer is enabled. This setting exists so that you can disable the visualizer in
  other environments via configuration.
* The route visualizer requires the "developer" permission. This setting only has any effect if you
  configured some authorization middleware to run before the route visualizer middleware.
