# OWIN Framework Default Document

This middleware will rewrite the request path when requests are received for the root folder
of the web iste. This allows you to serve a static file for example which is not possible
otherwise.

## Configuration

The OWIN Framework supports any configuration mechanism you choose. At the time of writing 
it comes bundled with support for Urchin and `web.config` configuration, but the 
`IConfiguration` interface is trivial to implement.

With the OWIN Framework the application developer chooses where in the configuration structure
each middleware will read its configuration from (this is so that you can have more than one
of the same middleware in your pipeline with different configurations).

This middleware has very few configuration options. See the `DefaultDocumentConfiguration.cs`
for details.

The default configuration will serve `/index.html`, this means that if visitors to
your site type `http://yoursite.com/` the owin pipeline will see `http://yoursite.com/index.html`.