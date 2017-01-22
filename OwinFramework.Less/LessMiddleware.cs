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
using OwinFramework.InterfacesV1.Middleware;

namespace OwinFramework.Less
{
    public class LessMiddleware:
        IMiddleware<IResponseProducer>, 
        InterfacesV1.Capability.IConfigurable,
        InterfacesV1.Capability.ISelfDocumenting,
        IRoutingProcessor
    {
        private readonly IHostingEnvironment _hostingEnvironment;
        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        IList<IDependency> IMiddleware.Dependencies { get { return _dependencies; } }

        string IMiddleware.Name { get; set; }

        public LessMiddleware(IHostingEnvironment hostingEnvironment)
        {
            _hostingEnvironment = hostingEnvironment;
            this.RunAfter<IOutputCache>(null, false);
            this.RunAfter<IRequestRewriter>(null, false);
            this.RunAfter<IResponseRewriter>(null, false);
        }

        Task IRoutingProcessor.RouteRequest(IOwinContext context, Func<Task> next)
        {
            var trace = (TextWriter)context.Environment["host.TraceOutput"];
            if (trace != null) trace.WriteLine(GetType().Name + " RouteRequest() starting " + context.Request.Uri);

            CssFileContext cssFileContext;
            if (!ShouldServeThisFile(context, out cssFileContext))
            {
                if (trace != null) trace.WriteLine(GetType().Name + " this is not a request for a Less or CSS file");
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
                        if (trace != null) trace.WriteLine(GetType().Name + " file has changed since output was cached. Output cache will not be used");
                        outputCache.UseCachedContent = false;
                    }
                    else
                    {
                        if (trace != null) trace.WriteLine(GetType().Name + " instructing output cache to use cached output");
                        outputCache.UseCachedContent = true;
                    }
                }
            }

            if (trace != null) trace.WriteLine(GetType().Name + " RouteRequest() finished. Short-circuiting any further routing");
            return null;
        }
        
        Task IMiddleware.Invoke(IOwinContext context, Func<Task> next)
        {
            var trace = (TextWriter)context.Environment["host.TraceOutput"];
            if (trace != null) trace.WriteLine(GetType().Name + " Invoke() starting " + context.Request.Uri);

            if (!string.IsNullOrEmpty(_configuration.DocumentationRootUrl) &&
                context.Request.Path.Value.Equals(_configuration.DocumentationRootUrl,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (trace != null) trace.WriteLine(GetType().Name + " returning configuration documentation");
                return DocumentConfiguration(context);
            }

            var cssFileContext = context.GetFeature<CssFileContext>();
            if (cssFileContext == null || cssFileContext.PhysicalFile == null)
            {
                if (trace != null) trace.WriteLine(GetType().Name + " no less context, probably not a less file request");
                return next();
            }

            var outputCache = context.GetFeature<IOutputCache>();
            if (outputCache != null)
            {
                outputCache.Priority = cssFileContext.NeedsCompiling ? CachePriority.High : CachePriority.Medium;
                if (trace != null) trace.WriteLine(GetType().Name + " setting output cache priority " + outputCache.Priority);
            }

            var result = Task.Factory.StartNew(() =>
                {

                    string fileContent;
                    using (var streamReader = cssFileContext.PhysicalFile.OpenText())
                    {
                        fileContent = streamReader.ReadToEnd();
                    }

                    context.Response.ContentType = "text/css";
                    if (cssFileContext.NeedsCompiling)
                    {
                        if (trace != null) trace.WriteLine(GetType().Name + " compiling Less file to CSS");
                        var css = dotless.Core.Less.Parse(fileContent);
                        context.Response.Write(css);
                    }
                    else
                    {
                        if (trace != null) trace.WriteLine(GetType().Name + " returning CSS from file system");
                        context.Response.Write(fileContent);
                    }
                });

            if (trace != null) trace.WriteLine(GetType().Name + " Invoke() finished ");
            return result;
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

            var defaultConfiguration = new LessConfiguration();
            document = document.Replace("{rootUrl.default}", defaultConfiguration.RootUrl);
            document = document.Replace("{documentationRootUrl.default}", defaultConfiguration.DocumentationRootUrl);
            document = document.Replace("{rootDirectory.default}", defaultConfiguration.RootDirectory);
            document = document.Replace("{enabled.default}", defaultConfiguration.Enabled.ToString());

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
            get { return "Serves CSS files by compiling LESS files into CSS using the dotless NuGet package."; }
        }

        public string ShortDescription
        {
            get { return "Serves CSS files by compiling LESS files into CSS."; }
        }

        public IList<InterfacesV1.Capability.IEndpointDocumentation> Endpoints { get { return null; } }

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
