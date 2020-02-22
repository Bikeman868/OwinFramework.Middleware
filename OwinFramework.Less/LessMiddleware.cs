using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using dotless.Core.configuration;
using dotless.Core.Loggers;
using Microsoft.Owin;
using OwinFramework.Builder;
using OwinFramework.Interfaces.Builder;
using OwinFramework.Interfaces.Routing;
using OwinFramework.Interfaces.Utility;
using OwinFramework.InterfacesV1.Capability;
using OwinFramework.InterfacesV1.Middleware;
using OwinFramework.MiddlewareHelpers.Analysable;
using OwinFramework.MiddlewareHelpers.SelfDocumenting;
using OwinFramework.MiddlewareHelpers.Traceable;

namespace OwinFramework.Less
{
    public class LessMiddleware:
        IMiddleware<IResponseProducer>, 
        IConfigurable,
        ISelfDocumenting,
        IRoutingProcessor,
        IAnalysable,
        ITraceable
    {
        private readonly IHostingEnvironment _hostingEnvironment;
        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        IList<IDependency> IMiddleware.Dependencies { get { return _dependencies; } }

        string IMiddleware.Name { get; set; }
        public Action<IOwinContext, Func<string>> Trace { get; set; }
        private readonly TraceFilter _traceFilter;

        private int _filesServedCount;
        private int _fileModificationCount;
        private int _filesCompiledCount;

        private Func<FileInfo, string, string> _preprocessor;

        public LessMiddleware(
            IHostingEnvironment hostingEnvironment)
        {
            _hostingEnvironment = hostingEnvironment;

            this.RunAfter<IOutputCache>(null, false);
            this.RunAfter<IRequestRewriter>(null, false);
            this.RunAfter<IResponseRewriter>(null, false);

            _traceFilter = new TraceFilter(null, this);
        }

        /// <summary>
        /// Call this method to install a hook that gets a chance to process
        /// Less files after loading from disk and before compiling. This allows
        /// you to perform search/replace on your less files, prepend common
        /// variable defintions etc.
        /// </summary>
        public LessMiddleware ApplyPreProcessing(Func<FileInfo, string, string> preprocessor)
        {
            _preprocessor = preprocessor;
            return this;
        }

        Task IRoutingProcessor.RouteRequest(IOwinContext context, Func<Task> next)
        {
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
                        _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " file has changed since output was cached. Output cache will not be used");
                        outputCache.UseCachedContent = false;
                        _fileModificationCount++;
                    }
                    else
                    {
                        _traceFilter.Trace(context, TraceLevel.Debug, () => GetType().Name + " instructing output cache to use cached output");
                        outputCache.UseCachedContent = true;
                    }
                }
            }

            _traceFilter.Trace(context, TraceLevel.Information, () => 
                GetType().Name + " this is a css file request" +
                (cssFileContext.NeedsCompiling ? " that needs compiling" : ""));

            return null;
        }
        
        Task IMiddleware.Invoke(IOwinContext context, Func<Task> next)
        {
            if (!string.IsNullOrEmpty(_configuration.DocumentationRootUrl) &&
                context.Request.Path.Value.Equals(_configuration.DocumentationRootUrl,
                    StringComparison.OrdinalIgnoreCase))
            {
                _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " returning configuration documentation");
                return DocumentConfiguration(context);
            }

            var cssFileContext = context.GetFeature<CssFileContext>();
            if (cssFileContext == null || cssFileContext.PhysicalFile == null)
                return next();

            var outputCache = context.GetFeature<IOutputCache>();
            if (outputCache != null)
            {
                outputCache.Category = "Less";
                outputCache.Priority = cssFileContext.NeedsCompiling ? CachePriority.High : CachePriority.Medium;
                _traceFilter.Trace(context, TraceLevel.Debug, () => GetType().Name + " setting output cache category='Less' and priority='" + outputCache.Priority + "'");
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
                        if (_preprocessor != null)
                        {
                            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " applying application defined pre-processing of Less file");
                            fileContent = _preprocessor(cssFileContext.PhysicalFile, fileContent);
                        }

                        _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " compiling Less file to CSS");
                        try
                        {
                            var css = dotless.Core.Less.Parse(
                                fileContent, 
                                new DotlessConfiguration 
                                {
                                    Logger = _configuration.TraceLog ? typeof(DotLessCustomLogger) : typeof(dotless.Core.Loggers.NullLogger),
                                    MinifyOutput = _configuration.Minify,
                                    
                                });
                            context.Response.Write(css);
                            _filesCompiledCount++;
                        }
                        catch (Exception ex)
                        {
                            _traceFilter.Trace(context, TraceLevel.Error, () => GetType().Name + " compilation error in LESS file, see response for details");
                            context.Response.Write("/* Compilation error in LESS file " + cssFileContext.PhysicalFile + Environment.NewLine);
                            while (ex != null)
                            {
                                context.Response.Write(ex.GetType().FullName + " " + ex.Message + Environment.NewLine);
                                ex = ex.InnerException;
                            }
                            context.Response.Write("*/" + Environment.NewLine);
                        }
                    }
                    else
                    {
                        _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " returning CSS from file system");
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
            _traceFilter.ConfigureWith(configuration);

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

        public Uri GetDocumentation(DocumentationTypes documentationType)
        {
            switch (documentationType)
            {
                case DocumentationTypes.Configuration:
                    return new Uri(_configuration.DocumentationRootUrl, UriKind.Relative);
                case DocumentationTypes.Overview:
                    return new Uri("https://github.com/Bikeman868/OwinFramework.Middleware", UriKind.Absolute);
                case DocumentationTypes.SourceCode:
                    return new Uri("https://github.com/Bikeman868/OwinFramework.Middleware/tree/master/OwinFramework.Less", UriKind.Absolute);
            }
            return null;
        }

        public string LongDescription
        {
            get 
            { 
                return 
                "<p>Compiles LESS to CSS on demand using the dotless NuGet package. Note "+
                "that the URL should end with .css even though a .less file is served "+
                "because as far as the browser is concerned it is downloading a CSS file.</p>"+
                "<p>If output caching is configured then it will be instructed to cache "+
                "the compiled CSS. If the underlying LESS file changes them the output "+
                "cache is invalidated so that the LESS will be recompiled the next time "+
                "it is requested.</p>"+
                "<p>If a CSS file exists on disk then this middleware will serve it as a "+
                "static file.</p>" +
                "<p>If there are compillation errors in the LESS file then the output will" +
                "be a diagnostic message enclosed in comment delimiters.</p></p>";
            }
        }

        public string ShortDescription
        {
            get { return "Serves CSS files by compiling LESS files into CSS"; }
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

            if (!string.Equals(context.Request.Method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                _traceFilter.Trace(context, TraceLevel.Debug, () => GetType().Name + " only supports GET requests");
                return false;
            }

            if (!_configuration.Enabled)
            {
                _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " is turned off in configuration");
                return false;
            }

            if (!_rootUrl.HasValue)
            {
                _traceFilter.Trace(context, TraceLevel.Error, () => GetType().Name + " has no root URL configured");
                return false;
            }

            var path = context.Request.Path;

            if (!path.HasValue ||
                !path.Value.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            {
                _traceFilter.Trace(context, TraceLevel.Debug, () => GetType().Name + " requested path does not end with '.css'");
                return false;
            }

            if (_rootUrl.Value != "/" && !path.StartsWithSegments(_rootUrl))
            {
                _traceFilter.Trace(context, TraceLevel.Debug, () => GetType().Name + " requested path is not below the configured root path '" + _rootUrl + "'");
                return false;
            }

            var relativePath = path.Value.Substring(_rootUrl.Value.Length).Replace('/', '\\');
            if (relativePath.StartsWith("\\")) relativePath = relativePath.Substring(1);
            var cssFileName = Path.Combine(_rootFolder, relativePath);
            var lessFileName = cssFileName.Substring(0, cssFileName.Length - 4) + ".less";

            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " path to CSS file is '" + cssFileName + "'");

            cssFileContext.PhysicalFile = new FileInfo(cssFileName);
            if (!cssFileContext.PhysicalFile.Exists)
            {
                _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " CSS file does not exist, LESS file will be compiled");
                cssFileContext.PhysicalFile = new FileInfo(lessFileName);
                cssFileContext.NeedsCompiling = true;
            }

            var exists = cssFileContext.PhysicalFile.Exists;

            var filePath = cssFileContext.PhysicalFile.FullName;
            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + (exists ? " file exists on disk" : filePath + " does not exist on disk"));

            return exists;
        }

        #endregion

        #region CssFileContext

        private class CssFileContext : IResponseProducer
        {
            public FileInfo PhysicalFile;
            public bool NeedsCompiling;
        }

        #endregion

        private class DotLessCustomLogger: dotless.Core.Loggers.ILogger
        {
            private readonly Action<TraceLevel, Func<string>> _traceAction;

            public DotLessCustomLogger(Action<TraceLevel, Func<string>> traceAction)
            {
                _traceAction = traceAction;
            }

            public void Debug(string message, params object[] args)
            {
                WriteLine(message, args);
                _traceAction(TraceLevel.Debug, () => string.Format(message, args));
            }

            public void Debug(string message)
            {
                WriteLine(message);
                _traceAction(TraceLevel.Debug, () => message);
            }

            public void Error(string message, params object[] args)
            {
                WriteLine(message, args);
                _traceAction(TraceLevel.Error, () => string.Format(message, args));
            }

            public void Error(string message)
            {
                WriteLine(message);
                _traceAction(TraceLevel.Error, () => message);
            }

            public void Info(string message, params object[] args)
            {
                WriteLine(message, args);
                _traceAction(TraceLevel.Information, () => string.Format(message, args));
            }

            public void Info(string message)
            {
                WriteLine(message);
                _traceAction(TraceLevel.Information, () => message);
            }

            public void Log(dotless.Core.Loggers.LogLevel level, string message)
            {
                WriteLine(message);
                switch (level)
                {
                    case LogLevel.Debug:
                        _traceAction(TraceLevel.Debug, () => message);
                        break;
                    case LogLevel.Error:
                    case LogLevel.Warn:
                        _traceAction(TraceLevel.Error, () => message);
                        break;
                    case LogLevel.Info:
                        _traceAction(TraceLevel.Information, () => message);
                        break;
                }
            }

            public void Warn(string message, params object[] args)
            {
                WriteLine(message, args);
                _traceAction(TraceLevel.Error, () => string.Format(message, args));
            }

            public void Warn(string message)
            {
                WriteLine(message);
                _traceAction(TraceLevel.Error, () => message);
            }

            private void WriteLine(string message, params object[] args)
            {
                message = message.Replace("\r", "").Replace("\n", "\nLESS: ");
                if (args == null || args.Length == 0)
                    System.Diagnostics.Trace.WriteLine("LESS: " + message);
                else
                    System.Diagnostics.Trace.WriteLine(string.Format("LESS: " + message, args));
            }
        }

    }
}
