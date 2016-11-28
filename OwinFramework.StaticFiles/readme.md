# OWIN Framework Static Files

This middleware will serve static files from your web site. Static files are where you have a
one-one mapping between URLs on your web site and files on disk. You can configure the location
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

This is an example configurations that assumes you use [Urchin](https://github.com/Bikeman868/Urchin) 
for as your configuration mechanism.

Example of configuring the OWIN Framework builder to specify the path to the middleware configuration.
This example configures two instances of the static files middleware called "Public resources" and 
"Protected resources" that are configured at "/middleware/staticFiles/public" and 
"/middleware/staticFiles/public" respectively:

```
builder.Register(ninject.Get<OwinFramework.StaticFiles.StaticFilesMiddleware>())
    .As("Public resources")
    .ConfigureWith(config, "/middleware/staticFiles/public");

builder.Register(ninject.Get<OwinFramework.StaticFiles.StaticFilesMiddleware>())
    .As("Protected resources")
    .ConfigureWith(config, "/middleware/staticFiles/protected");

```

This is an example Urchin configuration that will work with the code above:

```
{
    "middleware": {
        "staticFiles": {
            "public": {
                "staticFilesRootUrl": "/assets/public",
                "documentationRootUrl": "/config/assets/public",
                "rootDirectory": "~\\assets",
                "enabled": "true",
                "includeSubFolders": "true",
                "FileExtensions": [
                    { "extension": ".html", "mimeType": "text/html" },
                    { "extension": ".css", "mimeType": "text/css" },
                    { "extension": ".js", "mimeType": "application/javascript" },
                    { "extension": ".jpg", "mimeType": "image/jpeg" },
                    { "extension": ".jpeg", "mimeType": "image/jpeg" }
                ],
                "maximumFileSizeToCache": 10000,
                "totalCacheSize": 1000000,
                "maximumCacheTime": "00:30:00",
                "requiredPermission": ""
            },
            "protected": {
                "staticFilesRootUrl": "/assets/protected",
                "documentationRootUrl": "/config/assets/protected",
                "rootDirectory": "D:\\assets",
                "enabled": "true",
                "includeSubFolders": "true",
                "FileExtensions": [
                    { "extension": ".html", "mimeType": "text/html" }
                ],
                "maximumFileSizeToCache": 10000,
                "totalCacheSize": 1000000,
                "maximumCacheTime": "00:30:00",
                "requiredPermission": "user"
            }
        }
    }
}
```

This configuration specifies that:

* The url `http://mysite/assets/public` is mapped to the files in the 
`\assets` sub-folder beneath the root folder of the web site. It also specified that the configuration
of this middleware can examined by retreieving the url `http://mysite/config/assets/public`.

* The url `http://mysite/assets/protected` is mapped to the files in the absolute file path
`D:\\assets`. It also specified that the configuration of this middleware can examined by 
retreieving the url `http://mysite/config/assets/protected`.

* For the protected assets this configuration also specifies that the request must be made in the
context of a user with the `user` permission. This required permission relies on having some
authorization middleware installed and configured on the same route. If there is no authorization
middleware confgured then the required permission will not be enforced.

* All static files are cached in memory for 30 minutes if they are less than 10,000 bytes in size up to a 
maximum total memory consumption of 1,000,000 bytes for all files. This featre relies on
output caching middleware. If there is no output caching middleware configured in your OWIN pipeline
then static files will not be cached.

