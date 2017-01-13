# OWIN Framework No Found Middleware

This middleware will improve the performance of your web site by capturing the output from
downstream middleware and saving it in the Cache facility, then using the cached response
when another identical request is received.

The output is only cached when dwnstream middleware indicates that it is valid to cache
the response. Other Middleware (like the static file middleware) will communicate with
the output cache when installed to tell it when the response can be cached.

Downstream middleware can set a category and a priority in the output cache for each request.
The output cache can be configured with different cache behavours for all combinations
of these two values.

## Configuration

The OWIN Framework supports any configuration mechanism you choose. At the time of writing 
it comes bundled with support for Urchin and `web.config` configuration, but the 
`IConfiguration` interface is trivial to implement.

With the OWIN Framework the application developer chooses where in the configuration structure
each middleware will read its configuration from (this is so that you can have more than one
of the same middleware in your pipeline with different configurations).

The default configuration of this middleware will work well for many web applications. If you want
to change it, you need to define a list of rules. These rules are evaluated in order until one
matches the request. If no rules match the request then the output is not cached, so configuring
an empty set of rules effectively disables the output cache.

Each rule can match a priority value, a category, both or neither. When a rule is matched it
defines how long the cached data will be retained on the server or on the browser or both
or neither. It also specifies the category name to pass to the cache facility. The cache
facility can use this category to configure it's behavior.