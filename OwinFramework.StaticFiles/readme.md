# OWIN Framework Static Files

This middleware will serve static files from your web site. You can configure the location
on your file system where the files reside and the URL on your site where these files can
be downloaded from.

You can limit the files that are served by file extension and restrict access to files using
any Authorization middleware that supports the OWIN Framework.

You can configure multiple static file middleware components in your owin pipeline to serve
files from different locations with different configurations.

You can configure static file caching. For this to work you will need to add some output
caching middleware to your Owin pipeline.

## Configuration

The OWIN Framework supports any configuration mechanism you choose. At the time of writing 
it comes bundled with support for Urchin and `web.config` configuration, but the 
`IConfiguration` interface is trivial to implement.

With the OWIN Framework the application developer chooses where in the configuration structure
each middleware will read its configuration from (this is so that you can have more than one
of the same middleware in your pipeline with different configurations).

For supported configuration options see the `StaticFilesConfiguration.cs` file in this folder. This
middleware is also self documenting, and can produce configuration documentation from within.

The default configuration will serve files from an `assets` folder in your web site via urls
starting with `/assets`. For example if you place an image called `logo.png` in the `assets`
folder of your site, then you can retrieve this file from `http://mydomain/assets/logo.png`.