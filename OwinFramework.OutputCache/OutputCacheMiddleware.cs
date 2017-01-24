using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Owin;
using Newtonsoft.Json;
using OwinFramework.Builder;
using OwinFramework.Interfaces.Builder;
using OwinFramework.InterfacesV1.Capability;
using OwinFramework.InterfacesV1.Facilities;
using OwinFramework.InterfacesV1.Middleware;
using OwinFramework.InterfacesV1.Upstream;
using OwinFramework.MiddlewareHelpers.Analysable;
using OwinFramework.MiddlewareHelpers.ResponseRewriter;

namespace OwinFramework.OutputCache
{
    public class OutputCacheMiddleware:
        IMiddleware<IOutputCache>,
        IUpstreamCommunicator<IUpstreamOutputCache>,
        IConfigurable,
        ISelfDocumenting,
        IAnalysable
    {
        private readonly InterfacesV1.Facilities.ICache _cache;
        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        IList<IDependency> IMiddleware.Dependencies { get { return _dependencies; } }

        string IMiddleware.Name { get; set; }

        private int _cacheHitCount;
        private int _cacheMissCount;
        private int _useCachedContentCount;
        private int _addedToCacheCount;

        private IDisposable _configurationRegistration;
        private OutputCacheConfiguration _configuration;

        public OutputCacheMiddleware(ICache cache)
        {
            _cache = cache;
            ConfigurationChanged(new OutputCacheConfiguration());
        }

        public Task RouteRequest(IOwinContext context, Func<Task> next)
        {
            var upstreamOutputCache = new OutputCacheContext(_cache, context, _configuration);
            context.SetFeature<IUpstreamOutputCache>(upstreamOutputCache);

            if (upstreamOutputCache.CachedContentIsAvailable)
                _cacheHitCount++;
            else
                _cacheMissCount++;

            return next();
        }

        public Task Invoke(IOwinContext context, Func<Task> next)
        {
#if DEBUG
            var trace = (TextWriter)context.Environment["host.TraceOutput"];
#endif

            if (!string.IsNullOrEmpty(_configuration.DocumentationRootUrl) &&
                context.Request.Path.Value.Equals(_configuration.DocumentationRootUrl, StringComparison.OrdinalIgnoreCase))
            {
#if DEBUG
                if (trace != null) trace.WriteLine(GetType().Name + " returning configuration documentation");
#endif
                return DocumentConfiguration(context);
            }
            
            var outputCache = context.GetFeature<IUpstreamOutputCache>() as OutputCacheContext;
            if (outputCache == null)
            {
                return next();
            }

            if (outputCache.CachedContentIsAvailable
                && outputCache.UseCachedContent)
            {
#if DEBUG
                if (trace != null) trace.WriteLine(GetType().Name + " returning cached response");
#endif
                _useCachedContentCount++;
                return outputCache.SendCachedResponse();
            }

            outputCache.CaptureResponse();
            context.SetFeature<IOutputCache>(outputCache);

            return next().ContinueWith(t =>
            {
#if DEBUG
                if (trace != null) trace.WriteLine(GetType().Name + " flushing captured response to actual response stream");
#endif
                outputCache.SendCapturedOutput();

#if DEBUG
                if (trace != null) trace.WriteLine(GetType().Name + " saving response to cache");
#endif
                if (outputCache.SaveToCache())
                    _addedToCacheCount++;
            });
        }

        #region IConfigurable

        public void Configure(IConfiguration configuration, string path)
        {
            _configurationRegistration = configuration.Register(path, ConfigurationChanged, _configuration);
        }

        private void ConfigurationChanged(OutputCacheConfiguration configuration)
        {
            _configuration = configuration;
        }

        #endregion

        #region ISelfDocumenting

        private Task DocumentConfiguration(IOwinContext context)
        {
            Func<IEnumerable<OutputCacheRule>, string> formatRules = (rules) =>
            {
                var sb = new StringBuilder();
                sb.Append("<pre>[<br/>");
                var first = true;
                foreach (var rule in rules)
                {
                    if (first)
                    {
                        sb.Append("&nbsp;&nbsp;{<br/>");
                        first = false;
                    }
                    else
                        sb.Append(",<br/>&nbsp;&nbsp;{<br/>");
                    sb.Append("&nbsp;&nbsp;&nbsp;&nbsp;\"category\":\"" + rule.CategoryName + "\",<br/>");
                    sb.Append("&nbsp;&nbsp;&nbsp;&nbsp;\"priority\":\"" + rule.Priority + "\",<br/>");
                    sb.Append("&nbsp;&nbsp;&nbsp;&nbsp;\"cacheCategory\":\"" + rule.CacheCategory + "\",<br/>");
                    sb.Append("&nbsp;&nbsp;&nbsp;&nbsp;\"serverCacheTime\":\"" + rule.ServerCacheTime + "\",<br/>");
                    sb.Append("&nbsp;&nbsp;&nbsp;&nbsp;\"browserCacheTime\":\"" + rule.BrowserCacheTime + "\"<br/>");
                    sb.Append("&nbsp;&nbsp;}");
                }
                sb.Append("<br/>]</pre>");
                return sb.ToString();
            };
            
            var document = GetEmbeddedResource("configuration.html");
            document = document.Replace("{rules}", formatRules(_configuration.Rules));
            document = document.Replace("{documentationUrl}", _configuration.DocumentationRootUrl);

            var defaultConfiguration = new OutputCacheConfiguration();
            document = document.Replace("{rules.default}", formatRules(defaultConfiguration.Rules));
            document = document.Replace("{documentationUrl.default}", defaultConfiguration.DocumentationRootUrl);

            context.Response.ContentType = "text/html";
            return context.Response.WriteAsync(document);
        }

        public string LongDescription
        {
            get { return "Captures output from downstream middleware and caches it only if the downstream middleware indicates that the response can be cached and reused."; }
        }

        public string ShortDescription
        {
            get { return "Caches output from downstream middleware"; }
        }

        Uri ISelfDocumenting.GetDocumentation(DocumentationTypes documentationType)
        {
            switch (documentationType)
            {
                case DocumentationTypes.Configuration:
                    return string.IsNullOrEmpty(_configuration.DocumentationRootUrl) 
                        ? null
                        : new Uri(_configuration.DocumentationRootUrl, UriKind.Relative);
                case DocumentationTypes.Overview:
                    return new Uri("https://github.com/Bikeman868/OwinFramework.Middleware", UriKind.Absolute);
                case DocumentationTypes.SourceCode:
                    return new Uri("https://github.com/Bikeman868/OwinFramework.Middleware/tree/master/OwinFramework.RouteVisualizer", UriKind.Absolute);
            }
            return null;
        }

        IList<IEndpointDocumentation> ISelfDocumenting.Endpoints
        {
            get
            {
                var documentation = new List<IEndpointDocumentation>();

                if (!string.IsNullOrEmpty(_configuration.DocumentationRootUrl))
                {
                    documentation.Add(
                        new EndpointDocumentation
                        {
                            RelativePath = _configuration.DocumentationRootUrl,
                            Description = "Documentation of the configuration options for the output cache middleware",
                            Attributes = new List<IEndpointAttributeDocumentation>
                            {
                                new EndpointAttributeDocumentation
                                {
                                    Type = "Method",
                                    Name = "GET",
                                    Description = "Returns output cache configuration documentation in HTML format"
                                }
                            }
                        });
                }
                return documentation;
            }
        }

        private class EndpointDocumentation : IEndpointDocumentation
        {
            public string RelativePath { get; set; }
            public string Description { get; set; }
            public string Examples { get; set; }
            public IList<IEndpointAttributeDocumentation> Attributes { get; set; }
        }

        private class EndpointAttributeDocumentation : IEndpointAttributeDocumentation
        {
            public string Type { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
        }

        #endregion

        #region IAnalysable

        public IList<IStatisticInformation> AvailableStatistics
        {
            get
            {
                var stats = new List<IStatisticInformation>();
                stats.Add(
                    new StatisticInformation
                    {
                        Id = "CacheHitCount",
                        Name = "Hit count",
                        Description = "The number of times that cached data was available"
                    });
                stats.Add(
                    new StatisticInformation
                    {
                        Id = "CacheMissCount",
                        Name = "Miss count",
                        Description = "The number of times that no cached data was available"
                    });
                stats.Add(
                    new StatisticInformation
                    {
                        Id = "UseCachedContentCount",
                        Name = "Cache use count",
                        Description = "The number of times that cached content was returned to the browser"
                    });
                stats.Add(
                    new StatisticInformation
                    {
                        Id = "AddedToCacheCount",
                        Name = "Cached count",
                        Description = "The number of times that generated output was saved in the cache"
                    });
                return stats;
            }
        }

        public IStatistic GetStatistic(string id)
        {
            switch (id)
            {
                case "CacheHitCount":
                    return new IntStatistic(() => _cacheHitCount);
                case "CacheMissCount":
                    return new IntStatistic(() => _cacheMissCount);
                case "UseCachedContentCount":
                    return new IntStatistic(() => _useCachedContentCount);
                case "AddedToCacheCount":
                    return new IntStatistic(() => _addedToCacheCount);
            }
            return null;
        }

        #endregion

        #region Embedded resources

        private string GetEmbeddedResource(string filename)
        {
            var scriptResourceName = Assembly.GetExecutingAssembly()
                .GetManifestResourceNames()
                .FirstOrDefault(n => n.Contains(filename));
            if (scriptResourceName == null)
                throw new Exception("Failed to find embedded resource " + filename);

            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(scriptResourceName))
            {
                if (stream == null)
                    throw new Exception("Failed to open embedded resource " + scriptResourceName);

                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        #endregion

        #region Serializable DTOs to store in cache

        [Serializable]
        private class CachedResponse
        {
            [JsonProperty("when")]
            public DateTime WhenCached { get; set; }
            
            [JsonProperty("content")]
            public byte[] CachedContent { get; set; }

            [JsonProperty("headers")]
            public CachedHeader[] Headers { get; set; }
        }

        [Serializable]
        private class CachedHeader
        {
            [JsonProperty("n")]
            public string HeaderName { get; set; }

            [JsonProperty("v")]
            public string HeaderValue { get; set; }
        }

        #endregion

        #region Request specific context

        /// <summary>
        /// An instance of this class is stored in the OWIN context for rach request
        /// that involves the output cache.
        /// </summary>
        private class OutputCacheContext : IUpstreamOutputCache, IOutputCache
        {
            // IUpstreamOutputCache
            public bool CachedContentIsAvailable { get { return _cachedResponse != null; } }
            public TimeSpan? TimeInCache { get; private set; }
            public bool UseCachedContent { get; set; }

            // IOutputCache
            public string Category { get; set; }
            public TimeSpan MaximumCacheTime { get; set; }
            public CachePriority Priority { get; set; }
            public byte[] OutputBuffer 
            {
                get { return _responseCapture == null ? null : _responseCapture.OutputBuffer; }
                set { _responseCapture.OutputBuffer = value; }
            }

            private static readonly IList<string> _noCacheHeaders = new List<string>{ "content-length" };

            private readonly string _cacheKey;
            private readonly ICache _cache;
            private readonly IOwinContext _context;
            private readonly OutputCacheConfiguration _configuration;

            private OutputCacheRule _rule;
            private ResponseCapture _responseCapture;
            private CachedResponse _cachedResponse;


            public OutputCacheContext(
                ICache cache, 
                IOwinContext context,
                OutputCacheConfiguration configuration)
            {
                _cache = cache;
                _context = context;
                _configuration = configuration;

                _cacheKey = GetCacheKey(context);
                _cachedResponse = cache.Get<CachedResponse>(_cacheKey);
                if (_cachedResponse != null)
                {
                    TimeInCache = DateTime.UtcNow - _cachedResponse.WhenCached;
                    UseCachedContent = true;
                }

                MaximumCacheTime = TimeSpan.FromHours(1);
                Category = "None";
                Priority = CachePriority.Never;
            }

            public Task SendCachedResponse()
            {
                if (_cachedResponse == null)
                    throw new Exception("Output cache middleware attempt to send cached response when there was none");

                if (_cachedResponse.Headers != null)
                {
                    foreach (var header in _cachedResponse.Headers)
                        _context.Response.Headers[header.HeaderName] = header.HeaderValue;
                }
                _context.Response.Headers["x-output-cache"] = TimeInCache.ToString();

                return _context.Response.WriteAsync(_cachedResponse.CachedContent);
            }

            public void CaptureResponse()
            {
                _responseCapture = new ResponseCapture(_context);

                _context.Response.OnSendingHeaders(c => 
                {
                    _rule = GetMatchingRule();
                    if (_rule.BrowserCacheTime.HasValue)
                    {
                        _context.Response.Expires = DateTime.UtcNow + _rule.BrowserCacheTime;
                        _context.Response.Headers.Set(
                            "Cache-Control",
                            "public, max-age=" + (int)_rule.BrowserCacheTime.Value.TotalSeconds);
                    }
                    else
                    {
                        _context.Response.Headers.Set("Cache-Control", "no-cache");
                    }

                }, _context);
            }

            public void SendCapturedOutput()
            {
                if (_responseCapture != null)
                    _responseCapture.Send();
            }

            public bool SaveToCache()
            {
                if (_responseCapture != null)
                {
                    if (_rule != null && _rule.ServerCacheTime.HasValue && _rule.ServerCacheTime.Value > TimeSpan.Zero)
                    {
                        if (_cachedResponse == null) _cachedResponse = new CachedResponse();

                        var buffer = _responseCapture.OutputBuffer;
                        if (buffer != null)
                        {
                            _cachedResponse.CachedContent = new byte[buffer.Length];
                            buffer.CopyTo(_cachedResponse.CachedContent, 0);

                            _cachedResponse.Headers = _context.Response.Headers.Keys
                                .Where(n => !_noCacheHeaders.Contains(n.ToLower()))
                                .Select(n => new CachedHeader { HeaderName = n, HeaderValue = _context.Response.Headers[n] })
                                .ToArray();

                            _cachedResponse.WhenCached = DateTime.UtcNow;

                            _cache.Put(_cacheKey, _cachedResponse, _rule.ServerCacheTime.Value, _rule.CacheCategory);
                            return true;
                        }
                    }
                }
                return false;
            }

            private OutputCacheRule GetMatchingRule()
            {
                if (_configuration.Rules != null)
                {
                    foreach (var rule in _configuration.Rules)
                    {
                        if (rule.Priority.HasValue && rule.Priority.Value != Priority) continue;
                        if (!string.IsNullOrEmpty(rule.CategoryName) && !string.Equals(rule.CategoryName, Category, StringComparison.OrdinalIgnoreCase)) continue;
                        return rule;
                    }
                }
                return new OutputCacheRule();
            }

            public void Clear(string urlRegex)
            {
            }

            public void Clear()
            {
                _cache.Delete(_cacheKey);
            }

            private string GetCacheKey(IOwinContext context)
            {
                var uri = context.Request.Uri;
                return "OutputCache:" + uri.PathAndQuery.ToLower();
            }
        }

        #endregion
    }
}
