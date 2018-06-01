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
using OwinFramework.Interfaces.Routing;
using OwinFramework.InterfacesV1.Capability;
using OwinFramework.InterfacesV1.Middleware;
using OwinFramework.MiddlewareHelpers.Analysable;
using OwinFramework.MiddlewareHelpers.SelfDocumenting;
using OwinFramework.MiddlewareHelpers.Traceable;

namespace OwinFramework.Dart
{
    public class DartMiddleware:
        IMiddleware<IRequestRewriter>, 
        IConfigurable,
        ISelfDocumenting,
        IAnalysable,
        IRoutingProcessor,
        ITraceable
    {
        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        IList<IDependency> IMiddleware.Dependencies { get { return _dependencies; } }

        string IMiddleware.Name { get; set; }
        public Action<IOwinContext, Func<string>> Trace { get; set; }

        private readonly TraceFilter _traceFilter;

        private int _supportedBrowserRequestCount;
        private int _unsupportedBrowserRequestCount;

        public DartMiddleware(IConfiguration configuration)
        {
            _traceFilter = new TraceFilter(configuration, this);
        }

        Task IRoutingProcessor.RouteRequest(IOwinContext context, Func<Task> next)
        {

            PathString relativePath;
            if (_rootUrl.HasValue && context.Request.Path.StartsWithSegments(_rootUrl, out relativePath))
            {
                if (!relativePath.HasValue || relativePath.Value == "/")
                {
                    _traceFilter.Trace(context, TraceLevel.Debug, () => GetType().Name + " selecting the default document " + _defaultDocument);
                    relativePath = _defaultDocument;
                }

                var dartContext = new DartContext();
                context.SetFeature<IRequestRewriter>(dartContext);
                context.SetFeature<IDart>(dartContext);

                var userAgent = context.Request.Headers["user-agent"];
                dartContext.IsDartSupported = 
                    userAgent != null &&
                    userAgent.IndexOf("(Dart)", StringComparison.OrdinalIgnoreCase) > -1;

                if (dartContext.IsDartSupported)
                {
                    _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " the browser supports Dart");
                    context.Request.Path = _rootDartFolder + relativePath;
                    _supportedBrowserRequestCount++;
                }
                else
                {
                    _traceFilter.Trace(context, TraceLevel.Debug, () => GetType().Name + " the browser does not support Dart");
                    context.Request.Path = _rootBuildFolder + relativePath;
                    _unsupportedBrowserRequestCount++;
                }

                var outputCache = context.GetFeature<InterfacesV1.Upstream.IUpstreamOutputCache>();
                if (outputCache != null)
                {
                    _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " disabling output caching");
                    outputCache.UseCachedContent = false;
                }
            }

            return next();
        }
        
        Task IMiddleware.Invoke(IOwinContext context, Func<Task> next)
        {
            if (!string.IsNullOrEmpty(_configuration.DocumentationRootUrl) &&
                context.Request.Path.Value.Equals(_configuration.DocumentationRootUrl,
                    StringComparison.OrdinalIgnoreCase))
            {
                _traceFilter.Trace(context, TraceLevel.Debug, () => GetType().Name + " returning configuration documentation");
                return DocumentConfiguration(context);
            }

            return next();
        }

        #region IConfigurable

        private IDisposable _configurationRegistration;
        private DartConfiguration _configuration = new DartConfiguration();

        private PathString _rootDartFolder;
        private PathString _rootBuildFolder;
        private PathString _rootUrl;
        private PathString _defaultDocument;

        public void Configure(IConfiguration configuration, string path)
        {
            _configurationRegistration = configuration.Register(
                path, 
                cfg => 
                    {
                        _configuration = cfg;

                        Func<string, PathString> normalizeUrl = u =>
                        {
                            if (u == null)
                                return new PathString();

                            u = u.Replace("\\", "/");

                            if (u.Length > 0 && u.EndsWith("/"))
                                u = u.Substring(0, u.Length - 1);

                            if (!u.StartsWith("/"))
                                u = "/" + u;

                            return new PathString(u);
                        };

                        _rootDartFolder = normalizeUrl(cfg.DartUiRootUrl);
                        _rootBuildFolder = normalizeUrl(cfg.CompiledUiRootUrl);
                        _rootUrl = normalizeUrl(cfg.UiRootUrl);
                        _defaultDocument = normalizeUrl(cfg.DefaultDocument);
                    }, 
                    new DartConfiguration());
        }

        #endregion

        #region ISelfDocumenting

        private Task DocumentConfiguration(IOwinContext context)
        {
            var document = GetScriptResource("configuration.html");
            document = document.Replace("{dartUiRootUrl}", _configuration.DartUiRootUrl);
            document = document.Replace("{defaultDocument}", _configuration.DefaultDocument);
            document = document.Replace("{documentationRootUrl}", _configuration.DocumentationRootUrl);
            document = document.Replace("{uiRootUrl}", _configuration.UiRootUrl);
            document = document.Replace("{compiledUiRootUrl}", _configuration.CompiledUiRootUrl);
            document = document.Replace("{analyticsEnabled}", _configuration.AnalyticsEnabled.ToString());

            var defaultConfiguration = new DartConfiguration();
            document = document.Replace("{dartUiRootUrl.default}", defaultConfiguration.DartUiRootUrl);
            document = document.Replace("{defaultDocument.default}", defaultConfiguration.DefaultDocument);
            document = document.Replace("{documentationRootUrl.default}", defaultConfiguration.DocumentationRootUrl);
            document = document.Replace("{uiRootUrl.default}", defaultConfiguration.UiRootUrl);
            document = document.Replace("{compiledUiRootUrl.default}", defaultConfiguration.CompiledUiRootUrl);
            document = document.Replace("{analyticsEnabled.default}", defaultConfiguration.AnalyticsEnabled.ToString());

            context.Response.ContentType = "text/html";
            return context.Response.WriteAsync(document);
        }

        public Uri GetDocumentation(InterfacesV1.Capability.DocumentationTypes documentationType)
        {
            switch (documentationType)
            {
                case DocumentationTypes.Configuration:
                    return new Uri(_configuration.DocumentationRootUrl, UriKind.Relative);
                case DocumentationTypes.Overview:
                    return new Uri("https://github.com/Bikeman868/OwinFramework.Middleware", UriKind.Absolute);
                case DocumentationTypes.SourceCode:
                    return new Uri("https://github.com/Bikeman868/OwinFramework.Middleware/tree/master/OwinFramework.Dart", UriKind.Absolute);
            }
            return null;
        }

        public string LongDescription
        {
            get { return null; }
        }

        public string ShortDescription
        {
            get { return "Rerwites requests for browsers that support Dart. Serves Dart source files instead of compiled JavaScript"; }
        }

        public IList<IEndpointDocumentation> Endpoints 
        { 
            get
            {
                var documentation = new List<IEndpointDocumentation>();
                if (!string.IsNullOrEmpty(_configuration.UiRootUrl))
                {
                    documentation.Add(
                        new EndpointDocumentation
                        {
                            RelativePath = _configuration.UiRootUrl,
                            Description = "A user interface written in the Dart programming language",
                            Attributes = new List<IEndpointAttributeDocumentation>
                            {
                                new EndpointAttributeDocumentation
                                {
                                    Type = "Method",
                                    Name = "GET",
                                    Description = "Returns static files needed to run a Dart application"
                                }
                            }
                        });
                }
                if (!string.IsNullOrEmpty(_configuration.DocumentationRootUrl))
                {
                    documentation.Add(
                        new EndpointDocumentation
                        {
                            RelativePath = _configuration.DocumentationRootUrl,
                            Description = "Documentation of the configuration options for the Dart middleware",
                            Attributes = new List<IEndpointAttributeDocumentation>
                            {
                                new EndpointAttributeDocumentation
                                {
                                    Type = "Method",
                                    Name = "GET",
                                    Description = "Returns configuration documentation for Dart middleware in HTML format"
                                }
                            }
                        });
                };
                return documentation;
            } 
        }

        #endregion

        #region Embedded resources

        private string GetScriptResource(string filename)
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

        #region IAnalysable

        public IList<IStatisticInformation> AvailableStatistics
        {
            get 
            { 
                var stats = new List<IStatisticInformation>();
                if (_configuration.AnalyticsEnabled)
                {
                    stats.Add(new StatisticInformation
                    {
                        Id = "SupportedBrowserRequestCount",
                        Name = "Supported browser requests",
                        Description = "The number of requests served to browsers that have support for the Dart programming language",
                        Units = ""
                    });
                    stats.Add(new StatisticInformation
                    {
                        Id = "UnsupportedBrowserRequestCount",
                        Name = "Unsupported browser requests",
                        Description = "The number of requests served to browsers that do not support for the Dart programming language",
                        Units = ""
                    });
                }
                return stats;
            }
        }

        public IStatistic GetStatistic(string id)
        {
            if (_configuration.AnalyticsEnabled)
            {
                switch (id)
                {
                    case "SupportedBrowserRequestCount":
                        return new LongStatistic(() => _supportedBrowserRequestCount);
                    case "UnsupportedBrowserRequestCount":
                        return new LongStatistic(() => _unsupportedBrowserRequestCount);
                }
            }
            return null;
        }

        #endregion

        private class DartContext : IDart
        {
            public bool IsDartSupported { get; set; }
        }

    }
}
