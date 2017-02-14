# OWIN Framework Not Found Middleware

This middleware will return a 404 response always. It will place itself after all other 
middleware in the pipeline so that when no other middleware handled the request a 404
response will be returned to the client.

If you want this behaviour only on certain routes through the OWIN pipeline then you can
configure this middleware only on those routes. You can also add multiple instances
of this middleware to the OWIN pipeline to have different 404 templates for different
routes. For example if you have an API that returns JSON, you might want the API to also
return JSON in the 404 case.

## Configuration

The OWIN Framework supports any configuration mechanism you choose. At the time of writing 
it comes bundled with support for Urchin and `web.config` configuration, but the 
`IConfiguration` interface is trivial to implement.

With the OWIN Framework the application developer chooses where in the configuration structure
each middleware will read its configuration from (this is so that you can have more than one
of the same middleware in your pipeline with different configurations).

This middleware has very few configuration options. See the `NotFoundConfiguration.cs`
for details.

This is an example of adding the not found middleware to the OWIN Framework pipeline builder.

```
builder.Register(ninject.Get<OwinFramework.NotFound.NotFoundMiddleware>())
    .As("Not found")
    .ConfigureWith(config, "/middleware/notFound");
```

If you uses the above code, and you use [Urchin](https://github.com/Bikeman868/Urchin) for 
configuration management then your configuration file can be set up like this:

```
{
    "middleware": {
        "notFound": {
            "template": "\\templates\\404.html"
        }
    }
}

```

This configuration specifies that:

* The page template for 404 responses is in a file called "404.html" in a "templates" folder within 
the web site.