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

namespace OwinFramework.Dart
{
    public class DartMiddleware:
        IMiddleware<object>, 
        InterfacesV1.Capability.IConfigurable,
        InterfacesV1.Capability.ISelfDocumenting,
        IRoutingProcessor
    {
        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        IList<IDependency> IMiddleware.Dependencies { get { return _dependencies; } }

        string IMiddleware.Name { get; set; }

        private readonly string _contextKey;

        public DartMiddleware()
        {
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

            if (fileContext.Configuration != null)
            {
                context.Response.ContentType = fileContext.Configuration.MimeType;
                if (fileContext.Configuration.Expiry.HasValue)
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

            return Task.Factory.StartNew(() =>
                {
                    var buffer = new byte[32 * 1024];
                    using (var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        while (true)
                        {
                            var length = stream.Read(buffer, 0, buffer.Length);
                            if (length == 0) break;
                            context.Response.Write(buffer, 0, length);
                            if (length < buffer.Length) break;
                        }
                    }
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
            var path = request.Path.Value.Substring(_rootUrl.Value.Length + 1);
            var fileName = path.Replace('/', '\\');

            // Serve the default document for requests to the root folder
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = _configuration.DefaultDocument;

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
            var versionSuffix = "_v" + _configuration.Version;
            fileContext.IsVersioned = baseFileName.EndsWith(versionSuffix);
            if (fileContext.IsVersioned)
            {
                var fileNameWithoutVersion = baseFileName.Substring(0, baseFileName.Length - versionSuffix.Length);
                fileName = fileNameWithoutVersion + fullExtension;
            }

            // Get the configuration appropriate to this file extension
            fileContext.Configuration = _configuration.FileExtensions
                .FirstOrDefault(c => string.Equals(c.Extension, fullExtension, StringComparison.OrdinalIgnoreCase));
            if (fileContext.Configuration == null)
                return false;

            var requestPath = request.Path.Value;
            if (requestPath.StartsWith("/")) requestPath = requestPath.Substring(1);

            if (_rootUrl.Value.Length == 0)
            {
                if (requestPath.IndexOf("/", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    fileContext.NativeFile = GetPhysicalFile(_configuration.RootDartDirectory, requestPath, 0);
                    fileContext.CompiledFile = GetPhysicalFile(_configuration.RootBuildDirectory, requestPath, 0);
                }
            }
            else
            {
                if (requestPath.Length <= _rootUrl.Value.Length)
                    return false;

                if (requestPath.StartsWith(_rootUrl.Value, StringComparison.OrdinalIgnoreCase))
                {
                    fileContext.NativeFile = GetPhysicalFile(_configuration.RootDartDirectory, requestPath, _rootUrl.Value.Length);
                    fileContext.CompiledFile = GetPhysicalFile(_configuration.RootBuildDirectory, requestPath, _rootUrl.Value.Length);
                }
            }

            return true;
        }

        private FileInfo GetPhysicalFile(string rootPath, string requestPath, int prefixLength)
        {
            var relativeFileName = requestPath.Substring(prefixLength).Replace("/", "\\");
            return new FileInfo(Path.Combine(_configuration.RootBuildDirectory, relativeFileName));
        }

        private string _rootDartFolder; // Fully qualified path ending with \
        private string _rootBuildFolder; // Fully qualified path ending with \
        private PathString _rootUrl; // Never starts with /. Ends in / unless it is the site root

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

                            if (p.StartsWith("~\\"))
                                p = p.Substring(2);

                            return Path.IsPathRooted(p) ? p : Path.Combine(AppDomain.CurrentDomain.SetupInformation.ApplicationBase, p);
                        };

                        Func<string, PathString> normalizeUrl = u =>
                        {
                            u = u.Replace("\\", "/");

                            if (u.StartsWith("/"))
                                u = u.Substring(1);

                            if (u.Length > 0 && !u.EndsWith("/"))
                                u = u + "/";

                            return new PathString(u);
                        };

                        _rootDartFolder = normalizeFolder(cfg.RootDartDirectory ?? "");
                        _rootBuildFolder = normalizeFolder(cfg.RootBuildDirectory ?? "");
                        _rootUrl = normalizeUrl(cfg.DartUiRootUrl ?? "");
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
