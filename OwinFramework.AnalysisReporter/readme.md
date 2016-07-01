# OWIN Framework Analysis Reporter

This middleware is useful for during development and for diagnosing issues in production.
It should not generally be exposed to the public users of your site.

This middleware will examine the other middleware in the OWIN pipeline if it was built 
with the OWIN Framework and identify middleware that implements the `IAnalysable`
interface. It will use the `IAnalysable` interface to extract analysis information
and will return this formatted according to the `Accept` header in the request.

The middleware supports the following MIME types in the `Accept` header. It will use
the first supported MIME type that it encounters. If the `Accept` header is missing
then JSON will be returned by default. If the `Accept` header is present but contains
no supported formats then a 406 response will be returned.

* `text/html` returns a very simple HTML page with very little formatting.

* `text/plain` returns unformatted text.

* `text/markdown` returns plain text with [markdown](https://tools.ietf.org/html/rfc7763) format.

* `application/json` returns a JSON formatted response.

* `application/xml` returns an XML formatted response.

## Configuration

The OWIN Framework supports any configuration mechanism you choose. At the time of writing 
it comes bundled with support for Urchin and `web.config` configuration, but the 
`IConfiguration` interface is trivial to implement.

With the OWIN Framework the application developer chooses where in the configuration structure
each middleware will read its configuration from (this is so that you can have more than one
of the same middleware in your pipeline with different configurations).

For supported configuration options see the `AnalysisReporterConfiguration.cs` file in this folder. This
middleware is also self documenting, and can produce configuration documentation from within.

The most important configuration value is the `path` which defaults to `/owin/analytics`. 
This is the URL within your application where the analysis reporter will be available.
