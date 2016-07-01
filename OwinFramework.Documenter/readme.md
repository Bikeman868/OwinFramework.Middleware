# OWIN Framework Documenter

This middleware is useful for application developers to understand how to use the 
middleware that they added to their application.

This middleware will examine the other middleware in the OWIN pipeline if it was built 
with the OWIN Framework and identify middleware that implements the `ISelfDocumenting`
interface. It will use the `ISelfDocumenting` interface to extract documentation
and will return an HTML page documenting all the endpoints in the application.

## Configuration

The OWIN Framework supports any configuration mechanism you choose. At the time of writing 
it comes bundled with support for Urchin and `web.config` configuration, but the 
`IConfiguration` interface is trivial to implement.

With the OWIN Framework the application developer chooses where in the configuration structure
each middleware will read its configuration from (this is so that you can have more than one
of the same middleware in your pipeline with different configurations).

For supported configuration options see the `DocumenterConfiguration.cs` file in this folder. This
middleware is also self documenting, and can produce configuration documentation from within.

The most important configuration value is the `path` which defaults to `/owin/endpoints`. 
This is the URL within your application where the documenter will be available.
