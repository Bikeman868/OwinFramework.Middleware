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
using OwinFramework.InterfacesV1.Upstream;

namespace OwinFramework.Middleware
{
    public class StaticFiles:
        IMiddleware<object>, 
        IConfigurable, 
        ISelfDocumenting,
        IRoutingProcessor
    {
        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        IList<IDependency> IMiddleware.Dependencies { get { return _dependencies; } }

        string IMiddleware.Name { get; set; }

        public StaticFiles()
        {
            this.RunAfter<IOutputCache>(null, false);
        }

        Task IMiddleware.Invoke(IOwinContext context, Func<Task> next)
        {
            if (!string.IsNullOrEmpty(_configuration.DocumentationRootUrl) &&
                context.Request.Path.Value.Equals(_configuration.DocumentationRootUrl, StringComparison.OrdinalIgnoreCase))
            {
                return DocumentConfiguration(context);
            }

            StaticFilesConfiguration configuration;
            FileInfo physicalFile;
            if (ShouldServeThisFile(context, out configuration, out physicalFile))
            {
                var outputCache = context.GetFeature<IOutputCache>();
                if (outputCache != null)
                {
                    var largeFile = physicalFile.Length > _configuration.MaximumFileSizeToCache;
                    outputCache.Category = largeFile ? "LargeStaticFile" : "SmallStaticFile";
                    outputCache.MaximumCacheTime = _configuration.MaximumCacheTime;
                    outputCache.Priority = largeFile ? CachePriority.Never : CachePriority.High;
                }

                SetContentType(context, physicalFile);
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
            return next();
        }

        private void SetContentType(IOwinContext context, FileInfo file)
        {
            var fileExtension = file.Extension;
            var extensionConfiguration = _configuration.FileExtensions == null 
                ? null 
                : _configuration.FileExtensions
                    .FirstOrDefault(e => string.Equals(e.Extension, fileExtension, StringComparison.OrdinalIgnoreCase));
            if (extensionConfiguration != null)
                context.Response.ContentType = extensionConfiguration.MimeType;
        }

        #region IConfigurable

        private IDisposable _configurationRegistration;
        private StaticFilesConfiguration _configuration = new StaticFilesConfiguration();

        private bool ShouldServeThisFile(
            IOwinContext context, 
            out StaticFilesConfiguration configuration,
            out FileInfo physicalFile)
        {
            // Note that the configuration can be changed at any time by another thread
            configuration = _configuration;

            physicalFile = null;
            if (!configuration.Enabled || !context.Request.Path.HasValue)
                return false;

            var rootUrl = _rootUrl;

            var requestPath = context.Request.Path.Value;
            if (requestPath.StartsWith("/")) requestPath = requestPath.Substring(1);

            if (rootUrl.Length == 0)
            {
                if (configuration.IncludeSubFolders ||
                    requestPath.IndexOf("/", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    physicalFile = GetFileInfo(requestPath, 0);
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
                        physicalFile = GetFileInfo(requestPath, rootUrl.Length);
                    }
                    else
                    {
                        if (requestPath.IndexOf("/", rootUrl.Length, StringComparison.OrdinalIgnoreCase) == -1)
                            physicalFile = GetFileInfo(requestPath, rootUrl.Length);
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
            return physicalFile.Exists;
        }

        private FileInfo GetFileInfo(string requestPath, int prefixLength)
        {
            var relativeFileName = requestPath.Substring(prefixLength).Replace("/", "\\");
            return new FileInfo(Path.Combine(_rootFolder, relativeFileName));
        }

        private string _rootFolder; // Fully qualified path ending with \
        private string _rootUrl; // Never starts with /. Ends in / unless it is the site root

        void IConfigurable.Configure(IConfiguration configuration, string path)
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

        Uri ISelfDocumenting.GetDocumentation(DocumentationTypes documentationType)
        {
            switch (documentationType)
            {
                case DocumentationTypes.Configuration:
                    return new Uri(_configuration.DocumentationRootUrl, UriKind.Relative);
                case DocumentationTypes.Overview:
                    return new Uri("https://github.com/Bikeman868/OwinFramework.Middleware", UriKind.Absolute);
            }
            return null;
        }

        string ISelfDocumenting.LongDescription
        {
            get { return "Serves static files by mapping a root URL onto a root folder in the file system. Can be restricted to root folder and only certain file extensions. Can require the caller to have permission."; }
        }

        string ISelfDocumenting.ShortDescription
        {
            get { return "Maps URLs onto physical files and returns those files to the requestor"; }
        }

        IList<IEndpointDocumentation> ISelfDocumenting.Endpoints { get { return null; } }

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
            // TODO: Figure out if we are going to serve this request as a static file

            var outputCache = context.GetFeature<IUpstreamOutputCache>();
            if (outputCache == null || !outputCache.CachedContentIsAvailable) 
                return next();

            // TODO: Expire content that was cached for too long or if the file changed on disk
            outputCache.UseCachedContent = true;

            return next();
        }

        #endregion
    }
}
