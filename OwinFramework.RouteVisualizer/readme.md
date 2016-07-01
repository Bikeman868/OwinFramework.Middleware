# OWIN Framework Route Visualizer

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
