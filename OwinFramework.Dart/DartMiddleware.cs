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

namespace OwinFramework.Dart
{
    public class DartMiddleware:
        IMiddleware<object>, 
        InterfacesV1.Capability.IConfigurable,
        InterfacesV1.Capability.ISelfDocumenting,
        IRoutingProcessor
    {
        private readonly IHostingEnvironment _hostingEnvironment;

        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        IList<IDependency> IMiddleware.Dependencies { get { return _dependencies; } }

        string IMiddleware.Name { get; set; }

        private readonly string _contextKey;
        private const string _versionPrefix = "_v";
        private const string _versionMarker = "{_v_}";

        public DartMiddleware(
            IHostingEnvironment hostingEnvironment)
        {
            _hostingEnvironment = hostingEnvironment;

            _contextKey = Guid.NewGuid().ToShortString(false);
            this.RunAfter<InterfacesV1.Middleware.IOutputCache>(null, false);
            this.RunAfter<InterfacesV1.Middleware.IAuthorization>(null, false);
        }

        Task IMiddleware.Invoke(IOwinContext context, Func<Task> next)
        {
            if (!string.IsNullOrEmpty(_configuration.DocumentationRootUrl) &&
                context.Request.Path.Value.Equals(_configuration.DocumentationRootUrl, StringComparison.OrdinalIgnoreCase))
            {
                return DocumentConfiguration(context);
            }

            var fileContext = context.Get<DartFileContext>(_contextKey);
            if (fileContext == null)
                return next();

            var file = fileContext.CompiledFile;

            var request = context.Request;
            var userAgent = request.Headers["user-agent"];
            if (userAgent != null && userAgent.IndexOf("(Dart)", StringComparison.OrdinalIgnoreCase) > -1)
                file = fileContext.NativeFile;

            if (file == null || !file.Exists)
                return next();

            var outputCache = context.GetFeature<InterfacesV1.Middleware.IOutputCache>();
            if (outputCache != null)
            {
                var largeFile = file.Length > _configuration.MaximumFileSizeToCache;
                outputCache.Category = largeFile ? "LargeStaticFile" : "SmallStaticFile";
                outputCache.MaximumCacheTime = _configuration.MaximumCacheTime;
                outputCache.Priority = largeFile 
                    ? InterfacesV1.Middleware.CachePriority.Never 
                    : InterfacesV1.Middleware.CachePriority.High;
            }

            if (fileContext.Configuration != null && fileContext.IsVersioned)
            {
                if (fileContext.Configuration.Expiry.HasValue && _versionSuffix.Length > 0)
                {
                    context.Response.Expires = DateTime.UtcNow + fileContext.Configuration.Expiry;
                    context.Response.Headers.Set(
                        "Cache-Control",
                        "public, max-age=" + (int)fileContext.Configuration.Expiry.Value.TotalSeconds);
                }
                else
                {
                    context.Response.Headers.Set("Cache-Control", "no-cache");
                }
            }

            return ServeFile(context, fileContext, file);
        }

        private Task ServeFile(IOwinContext context, DartFileContext fileContext, FileInfo fileInfo)
        {
            return Task.Factory.StartNew(() =>
            {
                byte[] content;
                using (var stream = fileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    content = new byte[stream.Length];
                    stream.Read(content, 0, content.Length);
                }

                if (fileContext.Configuration.Processing != FileProcessing.None)
                {
                    var encoding = Encoding.UTF8;
                    var text = encoding.GetString(content);
                    switch (fileContext.Configuration.Processing)
                    {
                        case FileProcessing.Html:
                        case FileProcessing.JavaScript:
                            text = text.Replace(_versionMarker, _versionSuffix);
                            break;
                        case FileProcessing.Dart:
                        case FileProcessing.Css:
                            break;
                    }
                    content = encoding.GetBytes(text);
                }
                context.Response.ContentType = fileContext.Configuration.MimeType;
                context.Response.ContentLength = content.Length;
                context.Response.Write(content, 0, content.Length);
            });
        }

        #region IConfigurable

        private IDisposable _configurationRegistration;
        private DartConfiguration _configuration = new DartConfiguration();

        private bool ShouldServeThisFile(
            IOwinContext context,
            out DartFileContext fileContext)
        {
            fileContext = new DartFileContext();

            var request = context.Request;

            // If this is not for us get the hell out
            if (   !_configuration.Enabled 
                || !request.Path.HasValue 
                || !request.Path.StartsWithSegments(_rootUrl))
                return false;

            // Extract the path relative to the Dart UI root
            var relativePath = request.Path.Value.Substring(_rootUrl.Value.Length);
            var fileName = relativePath.Replace('/', '\\');

            // Serve the default document for requests to the root folder
            if (string.IsNullOrWhiteSpace(fileName) || fileName == "\\")
                fileName = _configuration.DefaultDocument;

            if (fileName.StartsWith("\\"))
                fileName = fileName.Substring(1);

            // Parse out pieces of the file name
            var lastDirectorySeparatorIndex = fileName.LastIndexOf('\\');
            var firstPeriodIndex = lastDirectorySeparatorIndex < 0
                ? fileName.IndexOf('.')
                : fileName.IndexOf('.', lastDirectorySeparatorIndex);
            var lastPeriodIndex = fileName.LastIndexOf('.');

            var fullExtension = firstPeriodIndex < 0 ? "" : fileName.Substring(firstPeriodIndex);
            var extension = lastPeriodIndex < 0 ? "" : fileName.Substring(lastPeriodIndex);
            var baseFileName = firstPeriodIndex < 0 ? fileName : fileName.Substring(0, firstPeriodIndex);

            // Check if the requested file includes a version number suffix
            var versionSuffix = _versionPrefix + _configuration.Version;
            fileContext.IsVersioned = baseFileName.EndsWith(versionSuffix);
            if (fileContext.IsVersioned)
            {
                var fileNameWithoutVersion = baseFileName.Substring(0, baseFileName.Length - versionSuffix.Length);
                fileName = fileNameWithoutVersion + fullExtension;
            }

            // Get the configuration appropriate to this file extension
            fileContext.Configuration = _configuration.FileExtensions
                .FirstOrDefault(c => string.Equals(c.Extension, extension, StringComparison.OrdinalIgnoreCase));
            if (fileContext.Configuration == null)
                return false;

            fileContext.NativeFile = new FileInfo(Path.Combine(_rootDartFolder, fileName));
            fileContext.CompiledFile = new FileInfo(Path.Combine(_rootBuildFolder, fileName));

            return true;
        }

        private string _rootDartFolder;
        private string _rootBuildFolder;
        private PathString _rootUrl;
        private string _versionSuffix;

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

                        _rootDartFolder = normalizeFolder(cfg.RootDartDirectory ?? "");
                        _rootBuildFolder = normalizeFolder(cfg.RootBuildDirectory ?? "");
                        _rootUrl = normalizeUrl(cfg.DartUiRootUrl ?? "");
                        _versionSuffix = cfg.Version.HasValue ? _versionPrefix + cfg.Version : string.Empty;
                    }, 
                    new DartConfiguration());
        }

        #endregion

        #region ISelfDocumenting

        private Task DocumentConfiguration(IOwinContext context)
        {
            Func<IEnumerable<ExtensionConfiguration>, string> formatExtensions = (extensions) =>
                {
                    var sb = new StringBuilder();
                    sb.Append("<pre>[<br/>");
                    var first = true;
                    foreach (var extension in extensions)
                    {
                        if (first)
                        {
                            sb.Append("&nbsp;&nbsp;{<br/>");
                            first = false;
                        }
                        else
                            sb.Append(",<br/>&nbsp;&nbsp;{<br/>");
                        sb.Append("&nbsp;&nbsp;&nbsp;&nbsp;\"extension\":\"" + extension.Extension + "\",<br/>");
                        sb.Append("&nbsp;&nbsp;&nbsp;&nbsp;\"mimeType\":\"" + extension.MimeType + "\"<br/>");
                        sb.Append("&nbsp;&nbsp;&nbsp;&nbsp;\"processing\":\"" + extension.Processing + "\"<br/>");
                        sb.Append("&nbsp;&nbsp;&nbsp;&nbsp;\"expiry\":\"" + extension.Expiry + "\"<br/>");
                        sb.Append("&nbsp;&nbsp;}");
                    }
                    sb.Append("<br/>]</pre>");
                    return sb.ToString();
                };

            var document = GetScriptResource("configuration.html");
            document = document.Replace("{dartUiRootUrl}", _configuration.DartUiRootUrl);
            document = document.Replace("{defaultDocument}", _configuration.DefaultDocument);
            document = document.Replace("{documentationRootUrl}", _configuration.DocumentationRootUrl);
            document = document.Replace("{rootDartDirectory}", _configuration.RootDartDirectory);
            document = document.Replace("{rootBuildDirectory}", _configuration.RootBuildDirectory);
            document = document.Replace("{enabled}", _configuration.Enabled.ToString());
            document = document.Replace("{fileExtensions}", formatExtensions(_configuration.FileExtensions));
            document = document.Replace("{maximumFileSizeToCache}", _configuration.MaximumFileSizeToCache.ToString());
            document = document.Replace("{maximumCacheTime}", _configuration.MaximumCacheTime.ToString());
            document = document.Replace("{totalCacheSize}", _configuration.TotalCacheSize.ToString());
            document = document.Replace("{requiredPermission}", _configuration.RequiredPermission);
            document = document.Replace("{version}", _configuration.Version.ToString());

            var defaultConfiguration = new DartConfiguration();
            document = document.Replace("{dartUiRootUrl.default}", defaultConfiguration.DartUiRootUrl);
            document = document.Replace("{defaultDocument.default}", defaultConfiguration.DefaultDocument);
            document = document.Replace("{documentationRootUrl.default}", defaultConfiguration.DocumentationRootUrl);
            document = document.Replace("{rootDartDirectory.default}", defaultConfiguration.RootDartDirectory);
            document = document.Replace("{rootBuildDirectory.default}", defaultConfiguration.RootBuildDirectory);
            document = document.Replace("{enabled.default}", defaultConfiguration.Enabled.ToString());
            document = document.Replace("{fileExtensions.default}", formatExtensions(defaultConfiguration.FileExtensions));
            document = document.Replace("{maximumFileSizeToCache.default}", defaultConfiguration.MaximumFileSizeToCache.ToString());
            document = document.Replace("{maximumCacheTime.default}", defaultConfiguration.MaximumCacheTime.ToString());
            document = document.Replace("{totalCacheSize.default}", defaultConfiguration.TotalCacheSize.ToString());
            document = document.Replace("{requiredPermission.default}", defaultConfiguration.RequiredPermission);
            document = document.Replace("{version.default}", defaultConfiguration.Version.ToString());

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
            get { return "Serves UI written in Dart by serving Dart source files to browsers that support it and compiled JavaScript otherwise."; }
        }

        public string ShortDescription
        {
            get { return "Maps URLs onto physical files and returns those files to the requestor"; }
        }

        public IList<IEndpointDocumentation> Endpoints 
        { 
            get 
            {
                var documentation = new List<IEndpointDocumentation>
                {
                    new EndpointDocumentation
                    {
                        RelativePath = _configuration.DartUiRootUrl,
                        Description = "A UI written in the Dart programming language",
                        Attributes = new List<IEndpointAttributeDocumentation>
                        {
                            new EndpointAttributeDocumentation
                            {
                                Type = "Method",
                                Name = "GET",
                                Description = "Returns static files needed to run a Dart application"
                            }
                        }
                    },
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
                    },
                };
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

        #region IRoutingProcessor

        Task IRoutingProcessor.RouteRequest(IOwinContext context, Func<Task> next)
        {
            DartFileContext dartFileContext;
            if (!ShouldServeThisFile(context, out dartFileContext))
                return next();

            context.Set(_contextKey, dartFileContext);

            var outputCache = context.GetFeature<InterfacesV1.Upstream.IUpstreamOutputCache>();
            if (outputCache != null && outputCache.CachedContentIsAvailable)
            {
                if (outputCache.TimeInCache.HasValue && 
                    outputCache.TimeInCache > _configuration.MaximumCacheTime)
                    outputCache.UseCachedContent = false;
            }

            if (!string.IsNullOrEmpty(_configuration.RequiredPermission))
            {
                var authorization = context.GetFeature<InterfacesV1.Upstream.IUpstreamAuthorization>();
                if (authorization != null)
                {
                    authorization.AddRequiredPermission(_configuration.RequiredPermission);
                }
            }

            return next();
        }

        #endregion

        #region DartFileContext

        private class DartFileContext
        {
            public bool IsVersioned;
            public FileInfo NativeFile;
            public FileInfo CompiledFile;
            public ExtensionConfiguration Configuration;
        }

        #endregion
    }
}
