﻿using System;
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
using OwinFramework.MiddlewareHelpers.Traceable;

namespace OwinFramework.StaticFiles
{
    public class StaticFilesMiddleware:
        IMiddleware<IResponseProducer>, 
        IConfigurable,
        ISelfDocumenting,
        IRoutingProcessor,
        IAnalysable,
        ITraceable
    {
        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        IList<IDependency> IMiddleware.Dependencies { get { return _dependencies; } }

        string IMiddleware.Name { get; set; }
        public Action<IOwinContext, Func<string>> Trace { get; set; }
        private readonly TraceFilter _traceFilter;

        private readonly IHostingEnvironment _hostingEnvironment;

        private int _textFilesServedCount;
        private int _binaryFilesServedCount;
        private int _cachedContentExpiredCount;
        private int _fileModificationCount;

        public StaticFilesMiddleware(IHostingEnvironment hostingEnvironment)
        {
            _hostingEnvironment = hostingEnvironment;
            _traceFilter = new TraceFilter(null, this);

            this.RunAfter<IOutputCache>(null, false);
            this.RunAfter<IRequestRewriter>(null, false);
            this.RunAfter<IResponseRewriter>(null, false);
            this.RunAfter<IAuthorization>(null, false);
        }

        Task IRoutingProcessor.RouteRequest(IOwinContext context, Func<Task> next)
        {
            if (!ShouldServeThisFile(context, out StaticFileContext fileContext))
                return next();

            context.SetFeature(fileContext);

            var outputCache = context.GetFeature<InterfacesV1.Upstream.IUpstreamOutputCache>();
            if (outputCache != null && outputCache.CachedContentIsAvailable)
            {
                _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " output cache has cached content available");
                if (outputCache.TimeInCache.HasValue)
                {
                    if (outputCache.TimeInCache > fileContext.Configuration.MaximumCacheTime)
                    {
                        _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " cached output is too old and will not be used");
                        _cachedContentExpiredCount++;
                        outputCache.UseCachedContent = false;
                    }
                    else
                    {
                        var timeSinceLastUpdate = DateTime.UtcNow - fileContext.PhysicalFile.LastWriteTimeUtc;
                        if (outputCache.TimeInCache > timeSinceLastUpdate)
                        {
                            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " file was modified since it was added to the output cache");
                            _fileModificationCount++;
                            outputCache.UseCachedContent = false;
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(fileContext.Configuration.RequiredPermission))
            {
                var authorization = context.GetFeature<InterfacesV1.Upstream.IUpstreamAuthorization>();
                if (authorization == null)
                {
                    _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " required permission will not be enforced because there is no upstream authorization middleware");
                }
                else
                {
                    _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " file access requires " + fileContext.Configuration.RequiredPermission + " permission");
                    authorization.AddRequiredPermission(fileContext.Configuration.RequiredPermission);
                }
            }

            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " ending the routing phase, this is a static file");
            return null;
        }

        Task IMiddleware.Invoke(IOwinContext context, Func<Task> next)
        {
            if (!string.IsNullOrEmpty(_configuration.DocumentationRootUrl) &&
                context.Request.Path.Value.Equals(_configuration.DocumentationRootUrl, StringComparison.OrdinalIgnoreCase))
            {
                _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " returning configuration documentation");
                return DocumentConfiguration(context);
            }

            var staticFileContext = context.GetFeature<StaticFileContext>();
            if (staticFileContext == null || !staticFileContext.FileExists)
            {
                _traceFilter.Trace(context, TraceLevel.Debug, () => GetType().Name + " this is not a request for a static file");
                return next();
            }

            var configuration = staticFileContext.Configuration;
            var physicalFile = staticFileContext.PhysicalFile;
            var extentionConfiguration = staticFileContext.ExtensionConfiguration;

            if (configuration == null || physicalFile == null || extentionConfiguration == null)
            {
                _traceFilter.Trace(context, TraceLevel.Error, () => GetType().Name + " required data is missing, file can not be served");
                return next();
            }

            var outputCache = context.GetFeature<IOutputCache>();
            if (outputCache != null)
            {
                var largeFile = physicalFile.Length > configuration.MaximumFileSizeToCache;
                outputCache.Category = largeFile ? "LargeStaticFile" : "SmallStaticFile";
                outputCache.MaximumCacheTime = configuration.MaximumCacheTime;
                outputCache.Priority = largeFile ? CachePriority.Never : CachePriority.High;
                _traceFilter.Trace(context, TraceLevel.Debug, () => GetType().Name + " configured output cache " + outputCache.Category + " " + outputCache.Priority + " " + outputCache.MaximumCacheTime);
            }

            context.Response.ContentType = extentionConfiguration.MimeType;

            if (extentionConfiguration.IsText)
            {
                _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " responding with a text file");
                _textFilesServedCount++;

                string text;
                using (var streamReader = physicalFile.OpenText())
                {
                    text = streamReader.ReadToEnd();
                }
                return context.Response.WriteAsync(text);
            }

            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " responding with a binary file");
            _binaryFilesServedCount++;

            var buffer = new byte[physicalFile.Length];
            using (var stream = physicalFile.Open(FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var offset = 0;
                while (true)
                {
                    var bytesRead = stream.Read(buffer, offset, buffer.Length - offset);
                    if (bytesRead == 0) return context.Response.WriteAsync(buffer);
                    offset += bytesRead;
                }
            }
        }

        #region IConfigurable

        private IDisposable _configurationRegistration;
        private StaticFilesConfiguration _configuration = new StaticFilesConfiguration();

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
                        _rootUrl = normalizeUrl(cfg.StaticFilesRootUrl ?? "");
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
            document = document.Replace("{requiredPermission}", _configuration.RequiredPermission);
            document = document.Replace("{analyticsEnabled}", _configuration.AnalyticsEnabled.ToString());

            var defaultConfiguration = new StaticFilesConfiguration();
            document = document.Replace("{staticFilesRootUrl.default}", defaultConfiguration.StaticFilesRootUrl);
            document = document.Replace("{documentationRootUrl.default}", defaultConfiguration.DocumentationRootUrl);
            document = document.Replace("{rootDirectory.default}", defaultConfiguration.RootDirectory);
            document = document.Replace("{enabled.default}", defaultConfiguration.Enabled.ToString());
            document = document.Replace("{includeSubFolders.default}", defaultConfiguration.IncludeSubFolders.ToString());
            document = document.Replace("{fileExtensions.default}", formatExtensions(defaultConfiguration.FileExtensions));
            document = document.Replace("{maximumFileSizeToCache.default}", defaultConfiguration.MaximumFileSizeToCache.ToString());
            document = document.Replace("{maximumCacheTime.default}", defaultConfiguration.MaximumCacheTime.ToString());
            document = document.Replace("{requiredPermission.default}", defaultConfiguration.RequiredPermission);
            document = document.Replace("{analyticsEnabled.default}", defaultConfiguration.AnalyticsEnabled.ToString());

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
                    return new Uri("https://github.com/Bikeman868/OwinFramework.Middleware/tree/master/OwinFramework.StaticFiles", UriKind.Absolute);
            }
            return null;
        }

        public string LongDescription
        {
            get { return 
                "<p>Serves static files by mapping a root URL onto a root folder in the file system. "+
                "Can be restricted to root folder and only certain file extensions. Can require the caller to have permission.</p>"; }
        }

        public string ShortDescription
        {
            get { return "Maps URLs onto physical files and returns those files to the requestor"; }
        }

        public IList<IEndpointDocumentation> Endpoints 
        { 
            get 
            {
                var documentation = new List<IEndpointDocumentation>();
                    if (!string.IsNullOrEmpty(_configuration.DocumentationRootUrl))
                        documentation.Add(
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
                            });
                foreach (var extension in _configuration.FileExtensions)
                {
                    documentation.Add(
                        new EndpointDocumentation
                        {
                            RelativePath = _configuration.StaticFilesRootUrl + (_configuration.IncludeSubFolders ? "*/*" : "*") + extension.Extension,
                            Description = "Maps the URL path to a file path rooted at " + _configuration.RootDirectory,
                            Attributes = new List<IEndpointAttributeDocumentation>
                            {
                                new EndpointAttributeDocumentation
                                {
                                    Type = "Method",
                                    Name = "GET",
                                    Description = "Returns the contents of a file with a content type of " + extension.MimeType
                                }
                            }
                        });
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
                    stats.Add(
                        new StatisticInformation
                        {
                            Id = "TextFilesServedCount",
                            Name = "Number of text files",
                            Description = "The number of requests that resulted in a text file being read from disk",
                            Explanation = "A text file is where the mime type for the content is text/* (for example text/html). Text files are handled differently because the file can contain preamble bytes that define the encoding"
                        });
                    stats.Add(
                        new StatisticInformation
                        {
                            Id = "BinaryFilesServedCount",
                            Name = "Number of binary files",
                            Description = "The number of requests that resulted in a binary file being read from disk",
                            Explanation = "A binary file is any file that is not a text file. Binary files are sent to the response stream byte-for-byte"
                        });
                    stats.Add(
                        new StatisticInformation
                        {
                            Id = "CachedContentExpiredCount",
                            Name = "Content expired count",
                            Description = "The number of requests where the output cache had a cached copy of the file, but it had been cached for too long"
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
                    case "TextFilesServedCount":
                        return new IntStatistic(() => _textFilesServedCount);
                    case "BinaryFilesServedCount":
                        return new IntStatistic(() => _binaryFilesServedCount);
                    case "CachedContentExpiredCount":
                        return new IntStatistic(() => _cachedContentExpiredCount);
                    case "FileModificationCount":
                        return new IntStatistic(() => _fileModificationCount);
                }
            }
            return null;
        }

        #endregion

        #region Routing helper functions

        private bool ShouldServeThisFile(
            IOwinContext context,
            out StaticFileContext fileContext)
        {
            // Capture the configuration because it can change at any time
            fileContext = new StaticFileContext
            {
                Configuration = _configuration,
                RootFolder = _rootFolder,
                RootUrl = _rootUrl
            };

            if (!string.Equals(context.Request.Method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                _traceFilter.Trace(context, TraceLevel.Debug, () => GetType().Name + " only handles GET requests");
                return false;
            }

            var request = context.Request;

            if (!_configuration.Enabled)
            {
                _traceFilter.Trace(context, TraceLevel.Error, () => GetType().Name + " is disabled and will not serve this request");
                return false;
            }

            if (!request.Path.HasValue)
            {
                _traceFilter.Trace(context, TraceLevel.Error, () => GetType().Name + " can not serve this request because the request path is empty");
                return false;
            }

            if (!fileContext.RootUrl.HasValue)
            {
                _traceFilter.Trace(context, TraceLevel.Error, () => GetType().Name + " will not handle this request because the configured root URL is empty");
                return false;
            }

            if (!(fileContext.RootUrl.Value == "/" || request.Path.StartsWithSegments(fileContext.RootUrl))) 
            {
                _traceFilter.Trace(context, TraceLevel.Debug, () => GetType().Name + " will not handle this request because the file is not in a sub-directory of the configured root");
                return false;
            }

            // Extract the path relative to the root UI
            var relativePath = request.Path.Value.Substring(fileContext.RootUrl.Value.Length);
            var fileName = relativePath.Replace('/', '\\');

            // No filename case can't be handled by this middleware
            if (string.IsNullOrWhiteSpace(fileName) || fileName == "\\")
            {
                _traceFilter.Trace(context, TraceLevel.Error, () => GetType().Name + " will not handle this request because the file name is blank");
                return false;
            }

            if (fileName.StartsWith("\\"))
                fileName = fileName.Substring(1);

            if (!fileContext.Configuration.IncludeSubFolders && fileName.Contains("\\"))
            {
                _traceFilter.Trace(context, TraceLevel.Error, () => GetType().Name + " will not handle this request because it is configured to not serve files from sub-folders");
                return false;
            }

            // Parse out pieces of the file name
            var lastPeriodIndex = fileName.LastIndexOf('.');
            var extension = lastPeriodIndex < 0 ? "" : fileName.Substring(lastPeriodIndex);

            // Get the configuration appropriate to this file extension
            fileContext.ExtensionConfiguration = fileContext.Configuration.FileExtensions
                .FirstOrDefault(c => string.Equals(c.Extension, extension, StringComparison.OrdinalIgnoreCase));

            // Only serve files that have their extensions confgured
            if (fileContext.ExtensionConfiguration == null)
            {
                _traceFilter.Trace(context, TraceLevel.Error, () => GetType().Name + " will not handle this request because " + extension + " file extensions are not configured");
                return false;
            }

            fileContext.PhysicalFile = new FileInfo(Path.Combine(fileContext.RootFolder, fileName));

            var file = fileContext.PhysicalFile;
            fileContext.FileExists = file.Exists;

            if (fileContext.FileExists)
                _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " this is a request for static file " + file);
            else
                _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " static file '" + file + "' does not exist on disk");

            return fileContext.FileExists;
        }

        #endregion

        #region StaticFileContext

        private class StaticFileContext
        {
            public FileInfo PhysicalFile;
            public bool FileExists;
            public StaticFilesConfiguration Configuration;
            public ExtensionConfiguration ExtensionConfiguration;
            public string RootFolder;
            public PathString RootUrl;
        }

        #endregion

    }
}
