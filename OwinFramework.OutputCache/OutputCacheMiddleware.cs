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
using OwinFramework.InterfacesV1.Middleware;
using OwinFramework.InterfacesV1.Upstream;
using OwinFramework.MiddlewareHelpers;

namespace OwinFramework.OutputCache
{
    public class OutputCacheMiddleware:
        IMiddleware<IOutputCache>,
        IUpstreamCommunicator<InterfacesV1.Upstream.IUpstreamOutputCache>,
        InterfacesV1.Capability.IConfigurable,
        InterfacesV1.Capability.ISelfDocumenting
    {
        private readonly InterfacesV1.Facilities.ICache _cache;
        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        IList<IDependency> IMiddleware.Dependencies { get { return _dependencies; } }

        string IMiddleware.Name { get; set; }

        private IDisposable _configurationRegistration;
        private OutputCacheConfiguration _configuration;

        public OutputCacheMiddleware(InterfacesV1.Facilities.ICache cache)
        {
            _cache = cache;
            ConfigurationChanged(new OutputCacheConfiguration());
        }

        public Task RouteRequest(IOwinContext context, Func<Task> next)
        {
            var trace = (TextWriter)context.Environment["host.TraceOutput"];
            if (trace != null) trace.WriteLine(GetType().Name + " RouteRequest() starting " + context.Request.Uri);

            var upstreamOutputCache = new OutputCacheContext(_cache, context, _configuration);
            context.SetFeature<IUpstreamOutputCache>(upstreamOutputCache);

            var result = next();

            if (trace != null) trace.WriteLine(GetType().Name + " RouteRequest() finished");
            return result;
        }

        public Task Invoke(IOwinContext context, Func<Task> next)
        {
            var trace = (TextWriter)context.Environment["host.TraceOutput"];
            if (trace != null) trace.WriteLine(GetType().Name + " Invoke() starting  " + context.Request.Uri);
            
            if (!string.IsNullOrEmpty(_configuration.DocumentationRootUrl) &&
                context.Request.Path.Value.Equals(_configuration.DocumentationRootUrl, StringComparison.OrdinalIgnoreCase))
            {
                if (trace != null) trace.WriteLine(GetType().Name + " returning configuration documentation");
                return DocumentConfiguration(context);
            }
            
            var outputCache = context.GetFeature<IUpstreamOutputCache>() as OutputCacheContext;
            if (outputCache == null)
            {
                if (trace != null) trace.WriteLine(GetType().Name + " has no context, chaining next middleware");
                return next();
            }

            if (outputCache.CachedContentIsAvailable
                && outputCache.UseCachedContent)
            {
                if (trace != null) trace.WriteLine(GetType().Name + " returning cached response");
                return outputCache.SendCachedResponse();
            }

            outputCache.CaptureResponse();
            context.SetFeature<IOutputCache>(outputCache);

            var result = next().ContinueWith(t =>
            {
                if (trace != null) trace.WriteLine(GetType().Name + " flushing captured response to actual response stream");
                outputCache.SendCapturedOutput();

                if (trace != null) trace.WriteLine(GetType().Name + " saving response to cache");
                outputCache.SaveToCache();
            });

            if (trace != null) trace.WriteLine(GetType().Name + " Invoke() finished");
            return result;
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
            document = document.Replace("{maximumCacheTime}", formatRules(_configuration.Rules));

            var defaultConfiguration = new OutputCacheConfiguration();
            document = document.Replace("{maximumCacheTime.default}", formatRules(defaultConfiguration.Rules));

            context.Response.ContentType = "text/html";
            return context.Response.WriteAsync(document);
        }

        public Uri GetDocumentation(InterfacesV1.Capability.DocumentationTypes documentationType)
        {
            switch (documentationType)
            {
                case InterfacesV1.Capability.DocumentationTypes.Configuration:
                    return new Uri(_configuration.DocumentationRootUrl, UriKind.Relative);
                case InterfacesV1.Capability.DocumentationTypes.Overview:
                    return new Uri("https://github.com/Bikeman868/OwinFramework.Middleware", UriKind.Absolute);
            }
            return null;
        }

        public string LongDescription
        {
            get { return "Captures output from downstream middleware and caches it only if the downstream middleware indicates that the response can be cached and reused."; }
        }

        public string ShortDescription
        {
            get { return "Caches output from downstream middleware"; }
        }

        public IList<InterfacesV1.Capability.IEndpointDocumentation> Endpoints 
        { 
            get
            {
                return new List<InterfacesV1.Capability.IEndpointDocumentation>();
            } 
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

        #region Capturing the response

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
            private readonly InterfacesV1.Facilities.ICache _cache;
            private readonly IOwinContext _context;
            private readonly OutputCacheConfiguration _configuration;

            private OutputCacheRule _rule;
            private ResponseCapture _responseCapture;
            private CachedResponse _cachedResponse;


            public OutputCacheContext(
                InterfacesV1.Facilities.ICache cache, 
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

            public void SaveToCache()
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
                        }
                    }
                }
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
    }
}
