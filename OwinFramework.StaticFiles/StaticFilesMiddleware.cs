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

namespace OwinFramework.StaticFiles
{
    public class StaticFilesMiddleware:
        IMiddleware<object>, 
        InterfacesV1.Capability.IConfigurable,
        InterfacesV1.Capability.ISelfDocumenting,
        IRoutingProcessor
    {
        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        IList<IDependency> IMiddleware.Dependencies { get { return _dependencies; } }

        string IMiddleware.Name { get; set; }

        private readonly string _contextKey;

        public StaticFilesMiddleware()
        {
            _contextKey = Guid.NewGuid().ToShortString(false);
            this.RunAfter<InterfacesV1.Middleware.IOutputCache>(null, false);
        }

        Task IMiddleware.Invoke(IOwinContext context, Func<Task> next)
        {
            if (!string.IsNullOrEmpty(_configuration.DocumentationRootUrl) &&
                context.Request.Path.Value.Equals(_configuration.DocumentationRootUrl, StringComparison.OrdinalIgnoreCase))
            {
                return DocumentConfiguration(context);
            }

            var staticFileContext = context.Get<StaticFileContext>(_contextKey);
            if (staticFileContext == null || staticFileContext.PhysicalFile == null)
                return next();

            var configuration = staticFileContext.Configuration;
            var physicalFile = staticFileContext.PhysicalFile;

            var outputCache = context.GetFeature<InterfacesV1.Middleware.IOutputCache>();
            if (outputCache != null)
            {
                var largeFile = physicalFile.Length > configuration.MaximumFileSizeToCache;
                outputCache.Category = largeFile ? "LargeStaticFile" : "SmallStaticFile";
                outputCache.MaximumCacheTime = configuration.MaximumCacheTime;
                outputCache.Priority = largeFile 
                    ? InterfacesV1.Middleware.CachePriority.Never 
                    : InterfacesV1.Middleware.CachePriority.High;
            }

            SetContentType(context, staticFileContext);

            return Task.Factory.StartNew(() =>
                {
                    var buffer = new byte[32 * 1024];
                    using (var stream = physicalFile.Open(FileMode.Open, FileAccess.Read, FileShare.Read))
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

        private void SetContentType(IOwinContext owinContext, StaticFileContext staticFileContext)
        {
            var fileExtension = staticFileContext.PhysicalFile.Extension;
            var extensionConfiguration = staticFileContext.Configuration.FileExtensions == null 
                ? null
                : staticFileContext.Configuration.FileExtensions
                    .FirstOrDefault(e => string.Equals(e.Extension, fileExtension, StringComparison.OrdinalIgnoreCase));
            if (extensionConfiguration != null)
                owinContext.Response.ContentType = extensionConfiguration.MimeType;
        }

        #region IConfigurable

        private IDisposable _configurationRegistration;
        private StaticFilesConfiguration _configuration = new StaticFilesConfiguration();

        private bool ShouldServeThisFile(
            IOwinContext context,
            out StaticFileContext staticFileContext)
        {
            staticFileContext = new StaticFileContext
            {
                Configuration = _configuration,
                RootFolder = _rootFolder,
                RootUrl = _rootUrl
            };

            var configuration = staticFileContext.Configuration;
            if (!configuration.Enabled || !context.Request.Path.HasValue)
                return false;

            var rootUrl = staticFileContext.RootUrl;
            FileInfo physicalFile = null;

            var requestPath = context.Request.Path.Value;
            if (requestPath.StartsWith("/")) requestPath = requestPath.Substring(1);

            if (rootUrl.Length == 0)
            {
                if (configuration.IncludeSubFolders ||
                    requestPath.IndexOf("/", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    physicalFile = GetPhysicalFile(staticFileContext, requestPath, 0);
                }
            }
            else
            {
                if (requestPath.Length <= rootUrl.Length)
                    return false;

                if (requestPath.StartsWith(rootUrl, StringComparison.OrdinalIgnoreCase))
                {
                    if (configuration.IncludeSubFolders)
                    {
                        physicalFile = GetPhysicalFile(staticFileContext, requestPath, rootUrl.Length);
                    }
                    else
                    {
                        if (requestPath.IndexOf("/", rootUrl.Length, StringComparison.OrdinalIgnoreCase) == -1)
                            physicalFile = GetPhysicalFile(staticFileContext, requestPath, rootUrl.Length);
                    }
                }
            }

            if (physicalFile == null) return false;

            if (configuration.FileExtensions != null && configuration.FileExtensions.Length > 0)
            {
                var extension = Path.GetExtension(physicalFile.Name);
                if (!configuration.FileExtensions.Any(e => String.Equals(e.Extension, extension, StringComparison.OrdinalIgnoreCase)))
                    return false;
            }

            staticFileContext.PhysicalFile = physicalFile;
            return physicalFile.Exists;
        }

        private FileInfo GetPhysicalFile(StaticFileContext staticFileContext, string requestPath, int prefixLength)
        {
            var relativeFileName = requestPath.Substring(prefixLength).Replace("/", "\\");
            return new FileInfo(Path.Combine(staticFileContext.RootFolder, relativeFileName));
        }

        private string _rootFolder; // Fully qualified path ending with \
        private string _rootUrl; // Never starts with /. Ends in / unless it is the site root

        public void Configure(IConfiguration configuration, string path)
        {
            _configurationRegistration = configuration.Register(
                path, 
                cfg => 
                    {
                        _configuration = cfg;

                        var rootFolder = cfg.RootDirectory ?? "";
                        rootFolder = rootFolder.Replace("/", "\\");

                        if (rootFolder.Length > 0 && !rootFolder.EndsWith("\\")) 
                            rootFolder = rootFolder + "\\";

                        if (rootFolder.StartsWith("~\\")) 
                            rootFolder = rootFolder.Substring(2);

                        if (Path.IsPathRooted(rootFolder))
                            _rootFolder = rootFolder;
                        else
                            _rootFolder = Path.Combine(AppDomain.CurrentDomain.SetupInformation.ApplicationBase, rootFolder);

                        var rootUrl = cfg.StaticFilesRootUrl ?? "";
                        rootUrl = rootUrl.Replace("\\", "/");

                        if (rootUrl.StartsWith("/")) 
                            rootUrl = rootUrl.Substring(1);

                        if (rootUrl.Length > 0 && !rootUrl.EndsWith("/")) 
                            rootUrl = rootUrl + "/";

                        _rootUrl = rootUrl;
                    }, 
                    new StaticFilesConfiguration());
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
                        sb.Append("&nbsp;&nbsp;}");
                    }
                    sb.Append("<br/>]</pre>");
                    return sb.ToString();
                };

            var document = GetScriptResource("configuration.html");
            document = document.Replace("{staticFilesRootUrl}", _configuration.StaticFilesRootUrl);
            document = document.Replace("{documentationRootUrl}", _configuration.DocumentationRootUrl);
            document = document.Replace("{rootDirectory}", _configuration.RootDirectory);
            document = document.Replace("{enabled}", _configuration.Enabled.ToString());
            document = document.Replace("{includeSubFolders}", _configuration.IncludeSubFolders.ToString());
            document = document.Replace("{fileExtensions}", formatExtensions(_configuration.FileExtensions));
            document = document.Replace("{maximumFileSizeToCache}", _configuration.MaximumFileSizeToCache.ToString());
            document = document.Replace("{maximumCacheTime}", _configuration.MaximumCacheTime.ToString());
            document = document.Replace("{totalCacheSize}", _configuration.TotalCacheSize.ToString());
            document = document.Replace("{requiredPermission}", _configuration.RequiredPermission);

            var defaultConfiguration = new StaticFilesConfiguration();
            document = document.Replace("{staticFilesRootUrl.default}", defaultConfiguration.StaticFilesRootUrl);
            document = document.Replace("{documentationRootUrl.default}", defaultConfiguration.DocumentationRootUrl);
            document = document.Replace("{rootDirectory.default}", defaultConfiguration.RootDirectory);
            document = document.Replace("{enabled.default}", defaultConfiguration.Enabled.ToString());
            document = document.Replace("{includeSubFolders.default}", defaultConfiguration.IncludeSubFolders.ToString());
            document = document.Replace("{fileExtensions.default}", formatExtensions(defaultConfiguration.FileExtensions));
            document = document.Replace("{maximumFileSizeToCache.default}", defaultConfiguration.MaximumFileSizeToCache.ToString());
            document = document.Replace("{maximumCacheTime.default}", defaultConfiguration.MaximumCacheTime.ToString());
            document = document.Replace("{totalCacheSize.default}", defaultConfiguration.TotalCacheSize.ToString());
            document = document.Replace("{requiredPermission.default}", defaultConfiguration.RequiredPermission);

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
            get { return "Serves static files by mapping a root URL onto a root folder in the file system. Can be restricted to root folder and only certain file extensions. Can require the caller to have permission."; }
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
            StaticFileContext staticFileContext;
            if (!ShouldServeThisFile(context, out staticFileContext))
                return next();

            context.Set(_contextKey, staticFileContext);

            var outputCache = context.GetFeature<InterfacesV1.Upstream.IUpstreamOutputCache>();
            if (outputCache != null && outputCache.CachedContentIsAvailable)
            {
                // TODO: Expire content that was cached for too long or if the file changed on disk
                outputCache.UseCachedContent = true;
            }

            if (!string.IsNullOrEmpty(staticFileContext.Configuration.RequiredPermission))
            {
                var authorization = context.GetFeature<InterfacesV1.Upstream.IUpstreamAuthorization>();
                if (authorization != null)
                {
                    authorization.AddRequiredPermission(staticFileContext.Configuration.RequiredPermission);
                }
            }

            return next();
        }

        #endregion

        #region StaticFileContext

        private class StaticFileContext
        {
            public FileInfo PhysicalFile;
            public StaticFilesConfiguration Configuration;
            public string RootFolder;
            public string RootUrl;
        }

        #endregion
    }
}
