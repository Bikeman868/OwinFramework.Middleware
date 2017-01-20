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

namespace OwinFramework.Versioning
{
    public class VersioningMiddleware:
        IMiddleware<object>,
        IRoutingProcessor,
        InterfacesV1.Capability.IConfigurable,
        InterfacesV1.Capability.ISelfDocumenting
    {
        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        IList<IDependency> IMiddleware.Dependencies { get { return _dependencies; } }

        string IMiddleware.Name { get; set; }

        private IDisposable _configurationRegistration;
        private VersioningConfiguration _configuration;

        public VersioningMiddleware()
        {
            ConfigurationChanged(new VersioningConfiguration());
            this.RunAfter<InterfacesV1.Middleware.IOutputCache>(null, false);
        }

        public Task RouteRequest(IOwinContext context, Func<Task> next)
        {
            var versionContext = new VersionContext(_configuration);
            context.SetFeature(versionContext);

            versionContext.RemoveVersionNumber(context);

            return next();
        }

        public Task Invoke(IOwinContext context, Func<Task> next)
        {
            if (!string.IsNullOrEmpty(_configuration.DocumentationRootUrl) &&
                context.Request.Path.Value.Equals(_configuration.DocumentationRootUrl, StringComparison.OrdinalIgnoreCase))
            {
                return DocumentConfiguration(context);
            }
            
            var versionContext = context.GetFeature<VersionContext>();
            if (versionContext == null)
                return next();

            versionContext.CaptureResponse(context);

            return next().ContinueWith(t => versionContext.Send(context).Wait());
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
            get { return "Adds version numbers to static assets so that the browser can safely cache them for a long time. This makes your website more responsive and reduces the number of requests to your wibsite."; }
        }

        public string ShortDescription
        {
            get { return "Adds version numbers to static assets"; }
        }

        public IList<InterfacesV1.Capability.IEndpointDocumentation> Endpoints 
        { 
            get
            {
                return new List<InterfacesV1.Capability.IEndpointDocumentation>();
            } 
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

        #region Capturing the response

        private class CapturedResponse
        {
            private MemoryStream _capturingStream;
            private Stream _responseStream;

            public void StartCapture(IOwinContext context, Action<IOwinContext, CapturedResponse> onSendingHeaders)
            {
                _responseStream = context.Response.Body;
                _capturingStream = new MemoryStream();
                context.Response.Body = _capturingStream;

                context.Response.OnSendingHeaders(
                    state =>
                    {
                        onSendingHeaders(context, state as CapturedResponse);
                    },  
                    this);
            }

            public byte[] EndCapture()
            {
                return _capturingStream.ToArray();
            }

            public Task Send(byte[] content)
            {
                return Task.Factory.FromAsync(
                    _responseStream.BeginWrite(content, 0, content.Length, null, null), 
                    ar => 
                    {
                        // Nothing to do when the write completes
                    });
            }
        }

        #endregion

        #region Request specific context

        private class VersionContext
        {
            private bool _isVersioned;

            private readonly VersioningConfiguration _configuration;
            private readonly CapturedResponse _response;

            private const string _versionPrefix = "_v";
            private const string _versionMarker = "{_v_}";

            public VersionContext(
                VersioningConfiguration configuration)
            {
                _configuration = configuration;
                _response = new CapturedResponse();
            }

            public void RemoveVersionNumber(IOwinContext context)
            {
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
                var allExtensions = _configuration.FileExtensions == null || _configuration.FileExtensions.Length == 0;
                if (extension.Length > 0)
                {
                    if (!allExtensions &&
                        !_configuration.FileExtensions.Any(e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase)))
                        return;
                }
                else
                {
                    if (!allExtensions) return;
                }

                _isVersioned = true;

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
                _response.StartCapture(context, (c, r) =>
                {
                    if (_isVersioned)
                    {
                        if (_configuration.BrowserCacheTime.HasValue)
                        {
                            c.Response.Expires = DateTime.UtcNow + _configuration.BrowserCacheTime.Value;
                            c.Response.Headers.Set(
                                "Cache-Control",
                                "public, max-age=" + (int)_configuration.BrowserCacheTime.Value.TotalSeconds);
                        }
                        else
                        {
                            c.Response.Headers.Set("Cache-Control", "no-cache");
                        }
                    }
                });
            }

            public Task Send(IOwinContext context)
            {
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

                if (_isVersioned)
                {
                    if (_configuration.BrowserCacheTime.HasValue)
                    {
                        context.Response.Expires = DateTime.UtcNow + _configuration.BrowserCacheTime.Value;
                        context.Response.Headers.Set(
                            "Cache-Control",
                            "public, max-age=" + (int)_configuration.BrowserCacheTime.Value.TotalSeconds);
                    }
                    else
                    {
                        context.Response.Headers.Set("Cache-Control", "no-cache");
                    }
                }

                var content = _response.EndCapture();

                if (_configuration.MimeTypes != null && 
                    _configuration.MimeTypes.Length > 0 &&
                    _configuration.MimeTypes.Any(m => string.Equals(m, mimeType, StringComparison.OrdinalIgnoreCase)))
                {
                    var text = encoding.GetString(content);
                    if (_configuration.Version.HasValue)
                        text = text.Replace(_versionMarker, _versionPrefix + _configuration.Version.Value);
                    else
                        text = text.Replace(_versionMarker, string.Empty);
                    content = encoding.GetBytes(text);
                }

                return _response.Send(content);
            }
        }

        #endregion

    }
}
