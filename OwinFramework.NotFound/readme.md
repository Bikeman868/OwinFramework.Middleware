# OWIN Framework No Found Middleware

This middleware will return a 404 response always. It will place itself after all other 
middleware in the pipeline so that when no other middleware handled the request a 404
response will be returned to the client.

## Configuration

The OWIN Framework supports any configuration mechanism you choose. At the time of writing 
it comes bundled with support for Urchin and `web.config` configuration, but the 
`IConfiguration` interface is trivial to implement.

With the OWIN Framework the application developer chooses where in the configuration structure
each middleware will read its configuration from (this is so that you can have more than one
of the same middleware in your pipeline with different configurations).

This middleware has very few configuration options. See the `NotFoundConfiguration.cs`
for details.