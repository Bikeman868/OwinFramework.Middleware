# OWIN Framework Session Middleware

This middleware will store a cookie on the user agent to maintain server-side state per user.

If you want this behaviour only on certain routes through the OWIN pipeline then you can
configure this middleware only on those routes. You can also add multiple instances
of this middleware to the OWIN pipeline to have different session behaviour for different
routes.

## Configuration

The OWIN Framework supports any configuration mechanism you choose. At the time of writing 
it comes bundled with support for Urchin and `web.config` configuration, but the 
`IConfiguration` interface is trivial to implement.

With the OWIN Framework the application developer chooses where in the configuration structure
each middleware will read its configuration from (this is so that you can have more than one
of the same middleware in your pipeline with different configurations).

This middleware has very few configuration options. See the `CacheSessionMiddleware.cs`
for details.

This is an example of adding the cache session middleware to the OWIN Framework pipeline builder.

```
builder.Register(ninject.Get<OwinFramework.Session.CacheSessionMiddleware>())
    .As("Session")
    .ConfigureWith(config, "/middleware/session");
```

If you uses the above code, and you use [Urchin](https://github.com/Bikeman868/Urchin) for 
configuration management then your configuration file can be set up like this:

```
{
    "middleware": {
        "session": {
            "cacheCategory": "session",
            "sessionDuration": "00:20:00",
            "cookieName": "session-id",
        }
    }
}

```

