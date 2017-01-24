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
using OwinFramework.Interfaces.Utility;
using OwinFramework.InterfacesV1.Capability;
using OwinFramework.InterfacesV1.Middleware;
using OwinFramework.MiddlewareHelpers.Analysable;

namespace OwinFramework.Less
{
    public class LessMiddleware:
        IMiddleware<IResponseProducer>, 
        IConfigurable,
        ISelfDocumenting,
        IRoutingProcessor,
        IAnalysable
    {
        private readonly IHostingEnvironment _hostingEnvironment;
        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        IList<IDependency> IMiddleware.Dependencies { get { return _dependencies; } }

        string IMiddleware.Name { get; set; }

        private int _filesServedCount;
        private int _fileModificationCount;
        private int _filesCompiledCount;

        public LessMiddleware(IHostingEnvironment hostingEnvironment)
        {
            _hostingEnvironment = hostingEnvironment;
            this.RunAfter<IOutputCache>(null, false);
            this.RunAfter<IRequestRewriter>(null, false);
            this.RunAfter<IResponseRewriter>(null, false);
        }

        Task IRoutingProcessor.RouteRequest(IOwinContext context, Func<Task> next)
        {
#if DEBUG
            var trace = (TextWriter)context.Environment["host.TraceOutput"];
#endif

            CssFileContext cssFileContext;
            if (!ShouldServeThisFile(context, out cssFileContext))
            {
                return next();
            }

            context.SetFeature(cssFileContext);
            context.SetFeature<IResponseProducer>(cssFileContext);

            var outputCache = context.GetFeature<InterfacesV1.Upstream.IUpstreamOutputCache>();
            if (outputCache != null && outputCache.CachedContentIsAvailable)
            {
                if (outputCache.TimeInCache.HasValue)
                {
                    var timeSinceFileChanged = DateTime.UtcNow - cssFileContext.PhysicalFile.LastWriteTimeUtc;
                    if (outputCache.TimeInCache.Value > timeSinceFileChanged)
                    {
#if DEBUG
                        if (trace != null) trace.WriteLine(GetType().Name + " file has changed since output was cached. Output cache will not be used");
#endif
                        outputCache.UseCachedContent = false;
                        _fileModificationCount++;
                    }
                    else
                    {
#if DEBUG
                        if (trace != null) trace.WriteLine(GetType().Name + " instructing output cache to use cached output");
#endif
                        outputCache.UseCachedContent = true;
                    }
                }
            }

#if DEBUG
            if (trace != null) trace.WriteLine(GetType().Name + " this is a css file request" 
                + (cssFileContext.NeedsCompiling ? " that needs compiling" : ""));
#endif
            return null;
        }
        
        Task IMiddleware.Invoke(IOwinContext context, Func<Task> next)
        {
#if DEBUG
            var trace = (TextWriter)context.Environment["host.TraceOutput"];
#endif

            if (!string.IsNullOrEmpty(_configuration.DocumentationRootUrl) &&
                context.Request.Path.Value.Equals(_configuration.DocumentationRootUrl,
                    StringComparison.OrdinalIgnoreCase))
            {
#if DEBUG
                if (trace != null) trace.WriteLine(GetType().Name + " returning configuration documentation");
#endif
                return DocumentConfiguration(context);
            }

            var cssFileContext = context.GetFeature<CssFileContext>();
            if (cssFileContext == null || cssFileContext.PhysicalFile == null)
            {
                return next();
            }

            var outputCache = context.GetFeature<IOutputCache>();
            if (outputCache != null)
            {
                outputCache.Priority = cssFileContext.NeedsCompiling ? CachePriority.High : CachePriority.Medium;
#if DEBUG
                if (trace != null) trace.WriteLine(GetType().Name + " setting output cache priority " + outputCache.Priority);
#endif
            }

            return Task.Factory.StartNew(() =>
                {

                    string fileContent;
                    using (var streamReader = cssFileContext.PhysicalFile.OpenText())
                    {
                        fileContent = streamReader.ReadToEnd();
                    }
                    _filesServedCount++;

                    context.Response.ContentType = "text/css";
                    if (cssFileContext.NeedsCompiling)
                    {
#if DEBUG
                        if (trace != null) trace.WriteLine(GetType().Name + " compiling Less file to CSS");
#endif
                        var css = dotless.Core.Less.Parse(fileContent);
                        context.Response.Write(css);
                        _filesCompiledCount++;
                    }
                    else
                    {
#if DEBUG
                        if (trace != null) trace.WriteLine(GetType().Name + " returning CSS from file system");
#endif
                        context.Response.Write(fileContent);
                    }
                });
        }

        #region IConfigurable

        private IDisposable _configurationRegistration;
        private LessConfiguration _configuration = new LessConfiguration();

        private string _rootFolder;
        private PathString _rootUrl;

        public void Configure(IConfiguration configuration, string path)
        {
            _configurationRegistration = configuration.Register(
                path, 
                cfg => 
                    {
                        _configuration = cfg;

                        Func<string, string> normalizeFolder = p =>
                        {
                            p = p.Replace("/", "\\");

                            if (p.Length > 0 && !p.EndsWith("\\"))
                                p = p + "\\";

                            return _hostingEnvironment.MapPath(p);
                        };

                        Func<string, PathString> normalizeUrl = u =>
                        {
                            u = u.Replace("\\", "/");

                            if (u.Length > 0 && u.EndsWith("/"))
                                u = u.Substring(0, u.Length - 1);

                            if (!u.StartsWith("/"))
                                u = "/" + u;

                            return new PathString(u);
                        };

                        _rootFolder = normalizeFolder(cfg.RootDirectory ?? "");
                        _rootUrl = normalizeUrl(cfg.RootUrl ?? "");
                    }, 
                    new LessConfiguration());
        }


        #endregion

        #region ISelfDocumenting

        private Task DocumentConfiguration(IOwinContext context)
        {
            var document = GetScriptResource("configuration.html");
            document = document.Replace("{rootUrl}", _configuration.RootUrl);
            document = document.Replace("{documentationRootUrl}", _configuration.DocumentationRootUrl);
            document = document.Replace("{rootDirectory}", _configuration.RootDirectory);
            document = document.Replace("{enabled}", _configuration.Enabled.ToString());
            document = document.Replace("{analyicsEnabled}", _configuration.AnalyticsEnabled.ToString());

            var defaultConfiguration = new LessConfiguration();
            document = document.Replace("{rootUrl.default}", defaultConfiguration.RootUrl);
            document = document.Replace("{documentationRootUrl.default}", defaultConfiguration.DocumentationRootUrl);
            document = document.Replace("{rootDirectory.default}", defaultConfiguration.RootDirectory);
            document = document.Replace("{enabled.default}", defaultConfiguration.Enabled.ToString());
            document = document.Replace("{analyicsEnabled.default}", defaultConfiguration.AnalyticsEnabled.ToString());

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
                case InterfacesV1.Capability.DocumentationTypes.SourceCode:
                    return new Uri("https://github.com/Bikeman868/OwinFramework.Middleware/tree/master/OwinFramework.Less", UriKind.Absolute);
            }
            return null;
        }

        public string LongDescription
        {
            get { return "Serves CSS files by compiling LESS files into CSS using the dotless NuGet package."; }
        }

        public string ShortDescription
        {
            get { return "Serves CSS files by compiling LESS files into CSS."; }
        }

        public IList<IEndpointDocumentation> Endpoints 
        { 
            get 
            {
                var documentation = new List<IEndpointDocumentation>();
                if (_configuration.Enabled)
                {
                    if (!string.IsNullOrEmpty(_configuration.RootUrl))
                    {
                        documentation.Add(
                            new EndpointDocumentation
                            {
                                RelativePath = _configuration.RootUrl + "*/*.css",
                                Description = "Cascading style sheet files rooted at " + _configuration.RootDirectory,
                                Attributes = new List<IEndpointAttributeDocumentation>
                                {
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Method",
                                        Name = "GET",
                                        Description = 
                                            "Returns the contents of the CSS file. If the CSS file does not "+
                                            "exist but there is a LESS file then it will be compiled to CSS "+
                                            "and returned on the fly."
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
                                Description = "Documentation for the configuratrion of this middleware",
                                Attributes = new List<IEndpointAttributeDocumentation>
                                {
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Method",
                                        Name = "GET",
                                        Description = "Returns documentation on the configuration of the Less middleware."
                                    }
                                }
                            });
                    }
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
                if (_configuration.AnalyticsEnabled)
                {
                    stats.Add(
                        new StatisticInformation
                        {
                            Id = "FilesServedCount",
                            Name = "Number of file requests",
                            Description = "The number of requests that resulted in a file being read from disk"
                        });
                    stats.Add(
                        new StatisticInformation
                        {
                            Id = "FilesCompiledCount",
                            Name = "Compiled count",
                            Description = "The number of requests where the Less file was compiled to Css"
                        });
                    stats.Add(
                        new StatisticInformation
                        {
                            Id = "FileModificationCount",
                            Name = "Content modified count",
                            Description = "The number of requests where the file was modified on disk since the content was added to the output cache"
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
                    case "FilesServedCount":
                        return new IntStatistic(() => _filesServedCount);
                    case "FilesCompiledCount":
                        return new IntStatistic(() => _filesCompiledCount);
                    case "FileModificationCount":
                        return new IntStatistic(() => _fileModificationCount);
                }
            }
            return null;
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

        #region Routing

        private bool ShouldServeThisFile(
            IOwinContext context,
            out CssFileContext cssFileContext)
        {
            cssFileContext = new CssFileContext();

            var path = context.Request.Path;

            if (!_configuration.Enabled || 
                !path.HasValue || 
                !_rootUrl.HasValue ||
                !path.Value.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                return false;

            if (_rootUrl.Value != "/" && !path.StartsWithSegments(_rootUrl))
                return false;

            var relativePath = path.Value.Substring(_rootUrl.Value.Length).Replace('/', '\\');
            var cssFileName = Path.Combine(_rootFolder, relativePath);
            var lessFileName = cssFileName.Substring(0, cssFileName.Length - 4) + ".less";

            cssFileContext.PhysicalFile = new FileInfo(cssFileName);
            if (!cssFileContext.PhysicalFile.Exists)
            {
                cssFileContext.PhysicalFile = new FileInfo(lessFileName);
                cssFileContext.NeedsCompiling = true;
            }

            return cssFileContext.PhysicalFile.Exists;
        }

        #endregion

        #region CssFileContext

        private class CssFileContext : IResponseProducer
        {
            public FileInfo PhysicalFile;
            public bool NeedsCompiling;
        }

        #endregion
    }
}
