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

        private const string _versionPrefix = "_v";
        private const string _versionMarker = "{_v_}";

        public DartMiddleware(
            IHostingEnvironment hostingEnvironment)
        {
            _hostingEnvironment = hostingEnvironment;

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

            var fileContext = context.GetFeature<DartFileContext>();
            if (fileContext == null)
                return next();

            if (fileContext.File == null || !fileContext.File.Exists)
                return next();

            var outputCache = context.GetFeature<InterfacesV1.Middleware.IOutputCache>();
            if (outputCache != null)
            {
                outputCache.Category = "StaticFile";
                outputCache.MaximumCacheTime = _configuration.MaximumCacheTime;
                outputCache.Priority = InterfacesV1.Middleware.CachePriority.High;
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

            return ServeFile(context, fileContext);
        }

        private Task ServeFile(IOwinContext context, DartFileContext fileContext)
        {
            return Task.Factory.StartNew(() =>
            {

                context.Response.ContentType = fileContext.Configuration.MimeType;

                if (fileContext.Configuration.Processing == FileProcessing.None)
                {
                    byte[] content;
                    using (var stream = fileContext.File.Open(FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        content = new byte[stream.Length];
                        stream.Read(content, 0, content.Length);
                    }
                    context.Response.ContentLength = content.Length;
                    context.Response.Write(content, 0, content.Length);
                }
                else
                {
                    string text;
                    using (var streamReader = fileContext.File.OpenText())
                    {
                        text = streamReader.ReadToEnd();
                    }
                    switch (fileContext.Configuration.Processing)
                    {
                        case FileProcessing.Html:
                        case FileProcessing.JavaScript:
                            text = text.Replace(_versionMarker, _versionSuffix);
                            break;
                        case FileProcessing.Dart:
                        case FileProcessing.Css:
                            break;
                        case FileProcessing.Less:
                            text = dotless.Core.Less.Parse(text);
                            break;
                    }
                    context.Response.Write(text);
                }
            });
        }

        #region IConfigurable

        private IDisposable _configurationRegistration;
        private DartConfiguration _configuration = new DartConfiguration();

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
                        sb.Append("&nbsp;&nbsp;&nbsp;&nbsp;\"mimeType\":\"" + extension.MimeType + "\",<br/>");
                        sb.Append("&nbsp;&nbsp;&nbsp;&nbsp;\"processing\":\"" + extension.Processing + "\",<br/>");
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

        public IList<InterfacesV1.Capability.IEndpointDocumentation> Endpoints 
        { 
            get 
            {
                var documentation = new List<InterfacesV1.Capability.IEndpointDocumentation>
                {
                    new EndpointDocumentation
                    {
                        RelativePath = _configuration.DartUiRootUrl,
                        Description = "A UI written in the Dart programming language",
                        Attributes = new List<InterfacesV1.Capability.IEndpointAttributeDocumentation>
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
                        Attributes = new List<InterfacesV1.Capability.IEndpointAttributeDocumentation>
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

        private class EndpointDocumentation : InterfacesV1.Capability.IEndpointDocumentation
        {
            public string RelativePath { get; set; }
            public string Description { get; set; }
            public string Examples { get; set; }
            public IList<InterfacesV1.Capability.IEndpointAttributeDocumentation> Attributes { get; set; }
        }

        private class EndpointAttributeDocumentation : InterfacesV1.Capability.IEndpointAttributeDocumentation
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

            context.SetFeature(dartFileContext);

            var outputCache = context.GetFeature<InterfacesV1.Upstream.IUpstreamOutputCache>();
            if (outputCache != null && outputCache.CachedContentIsAvailable)
            {
                if (outputCache.TimeInCache.HasValue)
                {
                    if (outputCache.TimeInCache > _configuration.MaximumCacheTime)
                    {
                        outputCache.UseCachedContent = false;
                    }
                    else
                    {
                        var timeSinceLastUpdate = DateTime.UtcNow - dartFileContext.File.LastWriteTimeUtc;
                        if (outputCache.TimeInCache > timeSinceLastUpdate)
                            outputCache.UseCachedContent = false;
                    }
                }
            }
            
            if (!string.IsNullOrEmpty(_configuration.RequiredPermission))
            {
                var authorization = context.GetFeature<InterfacesV1.Upstream.IUpstreamAuthorization>();
                if (authorization != null)
                {
                    authorization.AddRequiredPermission(_configuration.RequiredPermission);
                }
            }

            return null;
        }

        private bool ShouldServeThisFile(
            IOwinContext context,
            out DartFileContext fileContext)
        {
            fileContext = new DartFileContext();

            var request = context.Request;

            // If this is turned off get the hell out
            if (!_configuration.Enabled
                || !request.Path.HasValue
                || !_rootUrl.HasValue)
                return false;

            // If this is not for us get the hell out
            if (!(_rootUrl.Value == "/" || request.Path.StartsWithSegments(_rootUrl)))
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

            // Check if the browser supports Dart natively
            var userAgent = request.Headers["user-agent"];
            if (userAgent != null && userAgent.IndexOf("(Dart)", StringComparison.OrdinalIgnoreCase) > -1)
                fileContext.File = new FileInfo(Path.Combine(_rootDartFolder, fileName));
            else
                fileContext.File = new FileInfo(Path.Combine(_rootBuildFolder, fileName));

            if (fileContext.File.Exists)
                return true;

            if (fileContext.Configuration.Processing == FileProcessing.Less)
            {
                fileContext.File = new FileInfo(Path.ChangeExtension(fileContext.File.FullName, ".less"));
                return fileContext.File.Exists;
            }

            return false;
        }

        #endregion

        #region DartFileContext

        private class DartFileContext
        {
            public bool IsVersioned;
            public FileInfo File;
            public ExtensionConfiguration Configuration;
        }

        #endregion
    }
}
