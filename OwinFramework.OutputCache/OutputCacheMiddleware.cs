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
        private readonly string _contextKey;

        public OutputCacheMiddleware(InterfacesV1.Facilities.ICache cache)
        {
            _cache = cache;
            _contextKey = Guid.NewGuid().ToShortString(false);
            ConfigurationChanged(new OutputCacheConfiguration());
        }

        public Task Invoke(IOwinContext context, Func<Task> next)
        {
            // Return cached output if available

            var outputCache = new OutputCache();
            context.SetFeature<InterfacesV1.Middleware.IOutputCache>(outputCache);

            var result = next();

            // Cache output if cacheable
        }

        #region Request routing

        public Task RouteRequest(IOwinContext context, Func<Task> next)
        {
            TimeSpan? timeInCache = null; // TODO: figure out if cached content is available
            var upstreamOutputCache = new UpstreamOutputCache(timeInCache);

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
        }

        #endregion

        #region ISelfDocumenting

        private Task DocumentConfiguration(IOwinContext context)
        {
            var document = GetEmbeddedResource("configuration.html");
            document = document.Replace("{maximumTotalMemory}", _configuration.MaximumTotalMemory.ToString());
            document = document.Replace("{maximumCacheTime}", _configuration.MaximumCacheTime.ToString());

            var defaultConfiguration = new OutputCacheConfiguration();
            document = document.Replace("{maximumTotalMemory.default}", defaultConfiguration.MaximumTotalMemory.ToString());
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

        private class OutputCache: InterfacesV1.Middleware.IOutputCache
        {
        }

        private class UpstreamOutputCache : InterfacesV1.Upstream.IUpstreamOutputCache
        {
            public bool CachedContentIsAvailable { get; private set; }
            public TimeSpan? TimeInCache { get; private set; }
            public bool UseCachedContent { get; set; }
            public string Category { get; set; }
            public TimeSpan MaximumCacheTime { get; set; }
            public InterfacesV1.Middleware.CachePriority Priority { get; set; }

            public UpstreamOutputCache(TimeSpan? timeInCache)
            {
                CachedContentIsAvailable = timeInCache.HasValue;
                TimeInCache = timeInCache;
            }

            public void Clear(string urlRegex)
            {
            }

            public void Clear()
            {
            }
        }
    }
}
