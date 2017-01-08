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

namespace OwinFramework.OutputCache
{
    public class OutputCacheMiddleware:
        IMiddleware<InterfacesV1.Middleware.IOutputCache>,
        IUpstreamCommunicator<InterfacesV1.Upstream.IUpstreamOutputCache>,
        InterfacesV1.Capability.IConfigurable
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

        public Task Invoke(IOwinContext context, Func<Task> next)
        {
            var cacheTime = _configuration.MaximumCacheTime;
            if (!cacheTime.HasValue)
            {
                return next();
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


            switch (outputCache.Priority)
            {
                case InterfacesV1.Middleware.CachePriority.Always:
                case InterfacesV1.Middleware.CachePriority.High:
                case InterfacesV1.Middleware.CachePriority.Medium:
                    result.ContinueWith(t => outputCache.CacheFor(cacheTime.Value));
                    break;
            }

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
            document = document.Replace("{maximumCacheTime}", _configuration.MaximumCacheTime.ToString());

            var defaultConfiguration = new OutputCacheConfiguration();
            document = document.Replace("{maximumCacheTime.default}", defaultConfiguration.MaximumCacheTime.ToString());

            context.Response.ContentType = "text/html";
            return context.Response.WriteAsync(document);
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

            public void StartCaptureResponse()
            {
                _capturingStream = new CapturingStream(_context.Response.Body);
                _context.Response.Body = _capturingStream;

                _context.Response.OnSendingHeaders(state =>
                {
                    var cachedResponse = (state as CachedResponse);
                    if (cachedResponse != null)
                        cachedResponse.CaptureHeaders();
                },  this);
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

            public OutputCache(
                InterfacesV1.Facilities.ICache cache, 
                IOwinContext context,
                OutputCacheConfiguration configuration)
            {
                _cache = cache;
                _context = context;

                CacheKey = GetCacheKey(context);
                Response = cache.Get<CachedResponse>(CacheKey);
                if (Response != null)
                {
                    Response.Initialize(context);
                    TimeInCache = DateTime.UtcNow - Response.WhenCached;
                    UseCachedContent = true;
                }

                // TODO: Examine middleware configuration and compare with the request to decide the caching strategy
                MaximumCacheTime = configuration.MaximumCacheTime.HasValue ? configuration.MaximumCacheTime.Value : TimeSpan.FromMinutes(10);
                Category = "Unknown";
                Priority = InterfacesV1.Middleware.CachePriority.Never;
            }

            public void CaptureResponse()
            {
                if (Response == null)
                    Response = new CachedResponse
                    {
                        WhenCached = DateTime.UtcNow
                    }.Initialize(_context);

                Response.StartCaptureResponse();
            }

            public void CacheFor(TimeSpan duration)
            {
                if (Response != null)
                {
                    Response.EndCaptureResponse();
                    _cache.Put(CacheKey, Response, duration);
                }
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
                return uri.PathAndQuery.ToLower();
            }
        }
    }
}
