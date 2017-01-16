using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Owin;
using OwinFramework.Builder;
using OwinFramework.Interfaces.Builder;
using OwinFramework.OutputCache;

namespace OwinFramework.Versioning
{
    public class VersioningMiddleware:
        IMiddleware<InterfacesV1.Middleware.IOutputCache>,
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

        public VersioningMiddleware(InterfacesV1.Facilities.ICache cache)
        {
            _cache = cache;
            ConfigurationChanged(new OutputCacheConfiguration());
        }

        public Task Invoke(IOwinContext context, Func<Task> next)
        {
            if (!string.IsNullOrEmpty(_configuration.DocumentationRootUrl) &&
                context.Request.Path.Value.Equals(_configuration.DocumentationRootUrl, StringComparison.OrdinalIgnoreCase))
            {
                return DocumentConfiguration(context);
            }
            
            var outputCache = context.GetFeature<InterfacesV1.Upstream.IUpstreamOutputCache>() as OutputCache;
            if (outputCache == null)
            {
                return next();
            }

            if (outputCache.CachedContentIsAvailable
                && outputCache.UseCachedContent)
            {
                return outputCache.Response.Send();
            }

            outputCache.CaptureResponse();
            context.SetFeature<InterfacesV1.Middleware.IOutputCache>(outputCache);

            var result = next();

            result.ContinueWith(t => outputCache.Cache());

            return result;
        }

        #region Request routing

        public Task RouteRequest(IOwinContext context, Func<Task> next)
        {
            var upstreamOutputCache = new OutputCache(_cache, context, _configuration);
            context.SetFeature<InterfacesV1.Upstream.IUpstreamOutputCache>(upstreamOutputCache);

            return next();
        }

        #endregion

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
            var document = GetEmbeddedResource("configuration.html");
            //document = document.Replace("{maximumCacheTime}", formatRules(_configuration.Rules));

            //var defaultConfiguration = new OutputCacheConfiguration();
            //document = document.Replace("{maximumCacheTime.default}", formatRules(defaultConfiguration.Rules));

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
            public DateTime WhenCached { get; set; }
            public byte[] Content { get; set; }
            public string ContentType { get; set; }

            private IOwinContext _context;
            private CapturingStream _capturingStream;

            public CachedResponse Initialize(IOwinContext context)
            {
                _context = context;
                return this;
            }

            public void StartCaptureResponse(Action<IOwinContext> onSendingHeaders)
            {
                _capturingStream = new CapturingStream(_context.Response.Body);
                _context.Response.Body = _capturingStream;

                _context.Response.OnSendingHeaders(
                    state =>
                    {
                        onSendingHeaders(_context);
                        var cachedResponse = (state as CachedResponse);
                        if (cachedResponse != null)
                            cachedResponse.CaptureHeaders();
                    },  
                    this);
            }

            public void EndCaptureResponse()
            {
                if (_capturingStream != null)
                    Content = _capturingStream.GetCapturedData();
            }

            public Task Send()
            {
                _context.Response.ContentType = ContentType;
                _context.Response.ContentLength = Content.LongLength;
                return _context.Response.WriteAsync(Content);
            }

            private void CaptureHeaders()
            {
                ContentType = _context.Response.ContentType;
                // TODO: Capture all headers
            }

            private class CapturingStream: Stream
            {
                private readonly Stream _originalStream;
                private readonly MemoryStream _memoryStream;

                public CapturingStream(Stream originalStream)
                {
                    _originalStream = originalStream;
                    _memoryStream = new MemoryStream();
                }

                public byte[] GetCapturedData()
                {
                    return _memoryStream.ToArray();
                }

                public override bool CanRead
                {
                    get { return _originalStream.CanRead; }
                }

                public override bool CanSeek
                {
                    get { return _originalStream.CanSeek; }
                }

                public override bool CanWrite
                {
                    get { return _originalStream.CanWrite; }
                }

                public override void Flush()
                {
                    _originalStream.Flush();
                }

                public override long Length
                {
                    get { return _originalStream.Length; }
                }

                public override long Position
                {
                    get { return _originalStream.Position; }
                    set { _memoryStream.Position = _originalStream.Position = value; }
                }

                public override int Read(byte[] buffer, int offset, int count)
                {
                    return _originalStream.Read(buffer, offset, count);
                }

                public override long Seek(long offset, SeekOrigin origin)
                {
                    _memoryStream.Seek(offset, origin);
                    return _originalStream.Seek(offset, origin);
                }

                public override void SetLength(long value)
                {
                    _memoryStream.SetLength(value);
                    _originalStream.SetLength(value);
                }

                public override void Write(byte[] buffer, int offset, int count)
                {
                    _memoryStream.Write(buffer, offset, count);
                    _originalStream.Write(buffer, offset, count);
                }
            }
         
        }

        #endregion

        private class OutputCache : 
            InterfacesV1.Upstream.IUpstreamOutputCache,
            InterfacesV1.Middleware.IOutputCache
        {
            // IUpstreamOutputCache
            public bool CachedContentIsAvailable { get { return Response != null; } }
            public TimeSpan? TimeInCache { get; private set; }
            public bool UseCachedContent { get; set; }

            // IOutputCache
            public string Category { get; set; }
            public TimeSpan MaximumCacheTime { get; set; }
            public InterfacesV1.Middleware.CachePriority Priority { get; set; }

            public string CacheKey { get; private set; }
            public CachedResponse Response { get; private set; }

            private readonly InterfacesV1.Facilities.ICache _cache;
            private readonly IOwinContext _context;
            private readonly OutputCacheConfiguration _configuration;

            public OutputCache(
                InterfacesV1.Facilities.ICache cache, 
                IOwinContext context,
                OutputCacheConfiguration configuration)
            {
                _cache = cache;
                _context = context;
                _configuration = configuration;

                CacheKey = GetCacheKey(context);
                Response = cache.Get<CachedResponse>(CacheKey);
                if (Response != null)
                {
                    Response.Initialize(context);
                    TimeInCache = DateTime.UtcNow - Response.WhenCached;
                    UseCachedContent = true;
                }

                MaximumCacheTime = TimeSpan.FromHours(1);
                Category = "None";
                Priority = InterfacesV1.Middleware.CachePriority.Never;
            }

            public void CaptureResponse()
            {
                //if (Response == null)
                //{
                //    Response = new CachedResponse
                //    {
                //        WhenCached = DateTime.UtcNow
                //    }.Initialize(_context);
                //}

                //Response.StartCaptureResponse(c =>
                //{ 
                //    _rule = GetMatchingRule();
                //    if (_rule.BrowserCacheTime.HasValue)
                //    {
                //        c.Response.Expires = DateTime.UtcNow + _rule.BrowserCacheTime;
                //        c.Response.Headers.Set(
                //            "Cache-Control",
                //            "public, max-age=" + (int)_rule.BrowserCacheTime.Value.TotalSeconds);
                //    }
                //    else
                //    {
                //        c.Response.Headers.Set("Cache-Control", "no-cache");
                //    }
                //});
            }

            public void Cache()
            {
                //if (Response != null)
                //{
                //    Response.EndCaptureResponse();

                //    if (_rule != null && _rule.ServerCacheTime.HasValue && _rule.ServerCacheTime.Value > TimeSpan.Zero)
                //    {
                //        _cache.Put(CacheKey, Response, _rule.ServerCacheTime.Value, _rule.CacheCategory);
                //    }
                //}
            }

            public void Clear(string urlRegex)
            {
            }

            public void Clear()
            {
                _cache.Delete(CacheKey);
            }

            private string GetCacheKey(IOwinContext context)
            {
                var uri = context.Request.Uri;
                return "OutputCache:" + uri.PathAndQuery.ToLower();
            }
        }
    }
}
