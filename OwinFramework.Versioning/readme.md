This middleware will improve the performance of your web site by adding a version number to
the URLs of static assets and instructing the browser to cache them indefinately.

Each time you release a new version of your web site you should increment the version number
so that the browser sees them as new assets and fetches them from the server. Once the server
has a particular version of an asset it will keep hold of it any not request it again, this
improves the user experience and reduces load on your servers.

This middleware does two things

1. It will modify HTML, CSS and JS (actual mime types are configurable) on its way out to the 
   browser, replacing a special marker with the current version number. This marker must be
   placed immediately before the file extension. For example `<img src="button{_v_}.png" />`
   will be replaced with `<img src ="button_v3.png" />` if the current version number is 3.
2. It will intercept incomming requests for assets (based on file extension) and strip off
   the version number before forwarding the request to the downstream middleware.

You can place this middleware in front of any downstream middleware that produces HTML and
serves the assets referenced by that HTML. Typically this will be a combination of some
page rendering middleware and teh static files middleware.

## Configuration

The OWIN Framework supports any configuration mechanism you choose. At the time of writing 
it comes bundled with support for Urchin and `web.config` configuration, but the 
`IConfiguration` interface is trivial to implement.

With the OWIN Framework the application developer chooses where in the configuration structure
each middleware will read its configuration from (this is so that you can have more than one
of the same middleware in your pipeline with different configurations).

If might want to configure it to:

1. Change the list of output mime types where version markers should be replaced by the 
   current version number.
2. Change the file extensions that should have version numbers stripped from the request
   URL before chaining to the rest of the middleware pipeline.
3. Set the current version number. You can also disable the versioning by setting the
   version number to `null`. This is useful for development. Setting the version number to
   `null` also supresses the headers telling the browser to cache the content, so the browser
   will request it every time, which is more useful behavior during development.

This middleware is self-documenting. Use the `Documenter` middleware to extract and
format documentation, or look at the `VersioningConfiguration` class which is deserialized
from the configuration file.
