using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Owin;
using OwinFramework.Builder;
using OwinFramework.Interfaces.Builder;
using OwinFramework.Interfaces.Routing;
using OwinFramework.InterfacesV1.Capability;
using OwinFramework.InterfacesV1.Middleware;
using OwinFramework.MiddlewareHelpers;
using OwinFramework.MiddlewareHelpers.Analysable;
using OwinFramework.MiddlewareHelpers.ResponseRewriter;

namespace OwinFramework.Versioning
{
    public class VersioningMiddleware:
        IMiddleware<IRequestRewriter>, 
        IConfigurable, 
        ISelfDocumenting, 
        IRoutingProcessor,
        IAnalysable
    {
        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        IList<IDependency> IMiddleware.Dependencies { get { return _dependencies; } }

        string IMiddleware.Name { get; set; }

        private int _versionedAssetCount;
        private int _versionReplacedCount;

        private IDisposable _configurationRegistration;
        private VersioningConfiguration _configuration;

        public VersioningMiddleware()
        {
            ConfigurationChanged(new VersioningConfiguration());
            this.RunAfter<IOutputCache>(null, false);
        }

        public Task RouteRequest(IOwinContext context, Func<Task> next)
        {
#if DEBUG
            var trace = (TextWriter)context.Environment["host.TraceOutput"];
#endif

            var versionContext = new VersioningContext(_configuration);
            context.SetFeature(versionContext);

            versionContext.RemoveVersionNumber(context);
            if (versionContext.IsVersioned)
                _versionedAssetCount++;

            return next();
        }

        public Task Invoke(IOwinContext context, Func<Task> next)
        {
#if DEBUG
            var trace = (TextWriter)context.Environment["host.TraceOutput"];
#endif

            if (!string.IsNullOrEmpty(_configuration.DocumentationRootUrl) &&
                context.Request.Path.Value.Equals(_configuration.DocumentationRootUrl, StringComparison.OrdinalIgnoreCase))
            {
                return DocumentConfiguration(context);
            }
            
            var versioningContext = context.GetFeature<VersioningContext>();
            if (versioningContext == null)
                return next();

            versioningContext.CaptureResponse(context);

            return next()
                .ContinueWith(t =>
                {
#if DEBUG
                    if (trace != null) trace.WriteLine(GetType().Name + " sending captured output");
#endif
                    versioningContext.Send(context);
                    if (versioningContext.VersionUrlsReplaced)
                        _versionReplacedCount++;
                });
        }

        #region IConfigurable

        public void Configure(IConfiguration configuration, string path)
        {
            _configurationRegistration = configuration.Register(path, ConfigurationChanged, _configuration);
        }

        private void ConfigurationChanged(VersioningConfiguration configuration)
        {
            _configuration = configuration;
        }

        #endregion

        #region ISelfDocumenting

        private Task DocumentConfiguration(IOwinContext context)
        {
            Func<string[], string> f = a =>
                {
                    if (a == null) return "null";
                    return "[<br>\"" + string.Join("\",<br>\"", a) + "\"<br>]";
                };

            var document = GetEmbeddedResource("configuration.html");
            document = document.Replace("{documentationRootUrl}", _configuration.DocumentationRootUrl);
            document = document.Replace("{version}", _configuration.Version.ToString());
            document = document.Replace("{mimeTypes}", f(_configuration.MimeTypes));
            document = document.Replace("{fileExtensions}", f(_configuration.FileExtensions));
            document = document.Replace("{browserCacheTime}", _configuration.BrowserCacheTime.ToString());
            document = document.Replace("{exactVersion}", _configuration.ExactVersion.ToString());

            var defaultConfiguration = new VersioningConfiguration();
            document = document.Replace("{documentationRootUrl.default}", defaultConfiguration.DocumentationRootUrl);
            document = document.Replace("{version.default}", defaultConfiguration.Version.ToString());
            document = document.Replace("{mimeTypes.default}", f(defaultConfiguration.MimeTypes));
            document = document.Replace("{fileExtensions.default}", f(defaultConfiguration.FileExtensions));
            document = document.Replace("{browserCacheTime.default}", defaultConfiguration.BrowserCacheTime.ToString());
            document = document.Replace("{exactVersion.default}", defaultConfiguration.ExactVersion.ToString());

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
                    return new Uri("https://github.com/Bikeman868/OwinFramework.Middleware/tree/master/OwinFramework.Versioning", UriKind.Absolute);
            }
            return null;
        }

        public string LongDescription
        {
            get { return "Adds version numbers to static assets so that the browser can safely cache them for a long time. This makes your website more responsive and reduces the number of requests to your wibsite."; }
        }

        public string ShortDescription
        {
            get { return "Adds version numbers to static assets"; }
        }

        public IList<IEndpointDocumentation> Endpoints 
        {
            get { return new List<IEndpointDocumentation>(); } 
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
                            Id = "VersionedAssetCount",
                            Name = "Versioned asset count",
                            Description = "The number of requests for assets that contained a version number",
                            Explanation = 
                                "When requesst are received for assets that contain version numbers " +
                                "(for example logo_v1.png) this middleware strips off the version number before " +
                                "passing it on for further processing. In this example the rest of the pipeline " +
                                "would see a request for logo.png"
                        });
                    stats.Add(
                        new StatisticInformation
                        {
                            Id = "VersionReplacedCount",
                            Name = "Version numbers replaced",
                            Description = "The number of requests served where version number markers were replaced",
                            Explanation = 
                                "Responses with specific mime types are scanned for version number markers. " + 
                                "These markers are replaced with the current version number before the response " +
                                "is sent back to the browser. This can be used to send versioned URLs to the browser. " +
                                "For example if you put <img src='logo{_v_}.png' /> into your html, this will get modified "+
                                "to <img src='logo_v1.png' />"
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
                    case "VersionedAssetCount":
                        return new IntStatistic(() => _versionedAssetCount);
                    case "VersionReplacedCount":
                        return new IntStatistic(() => _versionReplacedCount);
                }
            }
            return null;
        }

        #endregion

        #region Embedded resources

        private string GetEmbeddedResource(string filename)
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

        #region Request specific context

        private class VersioningContext
        {
            private readonly VersioningConfiguration _configuration;
            public bool IsVersioned;
            public bool VersionUrlsReplaced;

            private ResponseCapture _response;

            private const string _versionPrefix = "_v";
            private const string _versionMarker = "{_v_}";

            public VersioningContext(
                VersioningConfiguration configuration)
            {
                _configuration = configuration;
            }

            public void RemoveVersionNumber(IOwinContext context)
            {
#if DEBUG
                var trace = (TextWriter)context.Environment["host.TraceOutput"];
#endif
                var path = context.Request.Path.Value;

                var fileNameIndex = path.LastIndexOf('/') + 1;
                var firstPeriodIndex = path.IndexOf('.', fileNameIndex);
                var lastPeriodIndex = path.LastIndexOf('.');

                var baseFileName = firstPeriodIndex < 0 ? path.Substring(fileNameIndex) : path.Substring(fileNameIndex, firstPeriodIndex - fileNameIndex);
                var versionIndex = baseFileName.LastIndexOf(_versionPrefix, StringComparison.OrdinalIgnoreCase);

                if (versionIndex < 0)
                    return;

                var extension = lastPeriodIndex < 0 
                    ? string.Empty 
                    : path.Substring(lastPeriodIndex);

#if DEBUG
                if (trace != null) trace.WriteLine(typeof(VersioningMiddleware).Name + " base file name: " + baseFileName + " Ext: " + extension);
#endif
                var allExtensions = _configuration.FileExtensions == null || _configuration.FileExtensions.Length == 0;
                if (extension.Length > 0)
                {
                    if (!allExtensions &&
                        !_configuration.FileExtensions.Any(e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase)))
                    {
#if DEBUG
                        if (trace != null) trace.WriteLine(typeof(VersioningMiddleware).Name + " the " + extension + " extension is not configured for versioning");
#endif
                        return;
                    }
                }
                else
                {
                    if (!allExtensions)
                    {
#if DEBUG
                        if (trace != null) trace.WriteLine(typeof(VersioningMiddleware).Name + " not configured to version extensionless paths");
#endif
                        return;
                    }
                }

#if DEBUG
                if (trace != null) trace.WriteLine(typeof(VersioningMiddleware).Name + " stripping version number from " + baseFileName);
#endif

                IsVersioned = true;

                if (_configuration.ExactVersion && _configuration.Version.HasValue)
                {
                    var version = _versionPrefix + _configuration.Version.Value;
                    var requestedVersion = baseFileName.Substring(versionIndex);
                    if (!string.Equals(requestedVersion, version, StringComparison.OrdinalIgnoreCase))
                        throw new HttpException(404, "This is not the current version of this resource");
                }

                context.Request.Path = new PathString(
                    path.Substring(0, fileNameIndex + versionIndex) +
                    path.Substring(firstPeriodIndex));
            }

            public void CaptureResponse(IOwinContext context)
            {
                _response = new ResponseCapture(context);                
            }

            public void Send(IOwinContext context)
            {
#if DEBUG
                var trace = (TextWriter)context.Environment["host.TraceOutput"];
#endif
                if (_response == null)
                    return;

                var contentType = context.Response.ContentType;

                var mimeType = contentType;
                var encoding = Encoding.UTF8;

                if (!string.IsNullOrEmpty(contentType))
                {
                    foreach (var contentTypeHeader in contentType.Split(';').Select(h => h.Trim()).Where(h => h.Length > 0))
                    {
                        if (contentTypeHeader.Contains('='))
                        {
                            if (contentTypeHeader.StartsWith("charset="))
                                encoding = Encoding.GetEncoding(contentTypeHeader.Substring(8));
                        }
                        else
                        {
                            mimeType = contentTypeHeader;
                        }
                    }
                }

#if DEBUG
                if (trace != null) trace.WriteLine(typeof(VersioningMiddleware).Name + " mime type " + mimeType + " with " + encoding.EncodingName + " encoding");
#endif

                if (IsVersioned)
                {
                    if (_configuration.BrowserCacheTime.HasValue)
                    {
#if DEBUG
                        if (trace != null) trace.WriteLine(typeof(VersioningMiddleware).Name + " adding headers to cache versioned asset for " + _configuration.BrowserCacheTime.Value);
#endif
                        context.Response.Expires = DateTime.UtcNow + _configuration.BrowserCacheTime.Value;
                        context.Response.Headers.Set(
                            "Cache-Control",
                            "public, max-age=" + (int)_configuration.BrowserCacheTime.Value.TotalSeconds);
                    }
                    else
                    {
#if DEBUG
                        if (trace != null) trace.WriteLine(typeof(VersioningMiddleware).Name + " adding headers to disable browser caching");
#endif
                        context.Response.Headers.Set("Cache-Control", "no-cache");
                    }
                }

                if (_configuration.MimeTypes != null && 
                    _configuration.MimeTypes.Length > 0 &&
                    _configuration.MimeTypes.Any(m => string.Equals(m, mimeType, StringComparison.OrdinalIgnoreCase)))
                {
                    var text = encoding.GetString(_response.OutputBuffer);

                    if (_configuration.Version.HasValue)
                    {
#if DEBUG
                        if (trace != null) trace.WriteLine(typeof(VersioningMiddleware).Name + " replacing version markers with " + _versionPrefix + _configuration.Version.Value);
#endif
                        text = text.Replace(_versionMarker, _versionPrefix + _configuration.Version.Value);
                    }
                    else
                    {
#if DEBUG
                        if (trace != null) trace.WriteLine(typeof(VersioningMiddleware).Name + " removing version markers");
#endif
                        text = text.Replace(_versionMarker, string.Empty);
                    }

                    _response.OutputBuffer = encoding.GetBytes(text);
                    VersionUrlsReplaced = true;
                }

#if DEBUG
                if (trace != null) trace.WriteLine(typeof(VersioningMiddleware).Name + " sending buffered response to actual response stream");
#endif
                _response.Send();
            }
        }

        #endregion

    }
}
