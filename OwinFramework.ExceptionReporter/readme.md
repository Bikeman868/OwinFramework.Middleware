# OWIN Framework Exception Reporter



## Configuration

The OWIN Framework supports any configuration mechanism you choose. At the time of writing 
it comes bundled with support for Urchin and `web.config` configuration, but the 
`IConfiguration` interface is trivial to implement.

With the OWIN Framework the application developer chooses where in the configuration structure
each middleware will read its configuration from (this is so that you can have more than one
of the same middleware in your pipeline with different configurations).

For supported configuration options see the `ExceptionReporterConfiguration.cs` file in this folder. This
middleware is also self documenting, and can produce configuration documentation from within.

