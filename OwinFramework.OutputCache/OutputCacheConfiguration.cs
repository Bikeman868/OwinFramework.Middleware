using System;
using Newtonsoft.Json;
using OwinFramework.InterfacesV1.Middleware;

namespace OwinFramework.OutputCache
{
    [Serializable]
    internal class OutputCacheConfiguration
    {
        /// <summary>
        /// Rules will be evaluated in the order thet appear here. The first matching
        /// rule will be used to determine output cache behavior. If no rules match then
        /// the output will not be cached
        /// </summary>
        [JsonProperty("rules")]
        public OutputCacheRule[] Rules { get; set; }

        [JsonProperty("documentationRootUrl")]
        public string DocumentationRootUrl { get; set; }

        public OutputCacheConfiguration()
        {
            DocumentationRootUrl = "/outputCache";

            Rules = new[]
            {
                new OutputCacheRule
                {
                    Priority = CachePriority.Always,
                    ServerCacheTime = TimeSpan.FromHours(3),
                    BrowserCacheTime = TimeSpan.FromHours(48)
                },
                new OutputCacheRule
                {
                    Priority = CachePriority.High,
                    ServerCacheTime = TimeSpan.FromHours(1),
                    BrowserCacheTime = TimeSpan.FromHours(6)
                },
                new OutputCacheRule
                {
                    Priority = CachePriority.Medium,
                    ServerCacheTime = TimeSpan.FromMinutes(10),
                    BrowserCacheTime = TimeSpan.FromHours(1)
                }
            };
        }
    }

    internal class OutputCacheRule
    {
        /// <summary>
        /// The output cache category to match. This is set for the
        /// request by downstream middleware
        /// </summary>
        [JsonProperty("category")]
        public string CategoryName { get; set; }

        /// <summary>
        /// The output cache priority to match. This is set for the
        /// request by downstream middleware
        /// </summary>
        [JsonProperty("priority")]
        public CachePriority? Priority { get; set; }

        /// <summary>
        /// When this output is cached, this category name will be used.
        /// The cache facility can be configured to handle these categories
        /// differently
        /// </summary>
        [JsonProperty("cacheCategory")]
        public string CacheCategory { get; set; }

        /// <summary>
        /// Specifies how long to cache this content on the server. If an
        /// identical request is received within this time, the output
        /// cache will return the cached response rather than chaining
        /// to downtream middleware. Set to null to disable server caching
        /// </summary>
        [JsonProperty("serverCacheTime")]
        public TimeSpan? ServerCacheTime { get; set; }

        /// <summary>
        /// Specifies how long to cache this content in the browser. Headers
        /// will be sent to the browser in the response that tells the browser
        /// to cache this content. Set to null to instruct the browser not to
        /// cache this content.
        /// </summary>
        [JsonProperty("browserCacheTime")]
        public TimeSpan? BrowserCacheTime { get; set; }

        public OutputCacheRule()
        {
            CacheCategory = "OutputCache";
        }
    }

}
