# OWIN Framework Output Cache Middleware

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
facility can use this category to configure its behavior.

## Example scenario

This is an example of a scenario in which you might want to use the output cache middleware.

For this example assume that this webiste is an API that can return user profiles and product 
information, and that this website runs on a server farm without sticky sessions so that 
requests from the same browser instance can be handled by different servers.

In this scenario the application code needs to tell the output cache whether the data being
returned is a user profile or a product because they must be handled differently. The sample
code below shows how you might do that:

```
private void ReturnUserProfile(IOwinContext context)
{
  var outputCache = context.GetFeature<IOutputCache>();
  if (outputCache != null)
  {
    outputCache.Category = "UserProfile";
    outputCache.Priority = CachePriority.High;
  }
}

private void ReturnProduct(IOwinContext context, bool isOnPromotion)
{
  var outputCache = context.GetFeature<IOutputCache>();
  if (outputCache != null)
  {
    outputCache.Category = "Product";
    outputCache.Priority = isOnPromotion ? CachePriority.Always : CachePriority.Medium;
  }
}
```

In this example the application uses different output cache categories for user profiles and
products so that they can be configured differently in the output cache. This example also
demosstrates setting different priorities, in this cases products that are on promotion are
always cached and products that are not on promotion are cached with medium prity. In this 
example the output cache rules might be configured like this:

```
"outputCache": {
    "rules": [
        {
            "category": "UserProfile",
            "priority": "High",
            "cacheCategory": "Shared",
            "serverCacheTime": "00:30:00",
            "browserCacheTime": "00:10:00"
        },
        {
            "category": "Product",
            "priority": "Always",
            "cacheCategory": "Local",
            "serverCacheTime": "01:00:00",
            "browserCacheTime": "00:30:00"
        },
        {
            "category": "Product",
            "priority": "Medium",
            "cacheCategory": "Local",
            "serverCacheTime": "00:10:00",
            "browserCacheTime": "2"
        }
    ],
    "documentationRootUrl": "/owin/outputCache"
}
```

This output cache configuration says that: 

* User profiles are cached on a shared cache server so that when the cache is updated
  all servers see the cache update. User profiles are cached on the server side for 30
  minutes maximum before being retrieved from the database again, and are cached on the
  browser for 10 minutes.

* Products are cached on the local machine to avoid network latency of fetching themm from
  a shared cache. This means that when products are updated in the database some servers
  will serve the old cached version for while until the cache expires.

* Different priority products have different cache times on the server and the browser.

This configuration assumes that the cache facility is configured to store items in the "Shared"
category in a centralzed cache server and items in the "Local" category in memory on the
local server.

Instead of setting cache expiry times, your application can also tell the output cache if
the cached content is valid or not. For example the static files middleware compares the
date/time when content was added to the cache with the last modification date/time on the
file, and if the file was modified it tells the output cache not to use the cached data.
See the source code for the static files middleware for an example of how to do this.