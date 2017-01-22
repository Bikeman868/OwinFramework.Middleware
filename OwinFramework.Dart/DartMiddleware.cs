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
using OwinFramework.InterfacesV1.Middleware;

namespace OwinFramework.Dart
{
    public class DartMiddleware:
        IMiddleware<IRequestRewriter>, 
        InterfacesV1.Capability.IConfigurable,
        InterfacesV1.Capability.ISelfDocumenting,
        IRoutingProcessor
    {
        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        IList<IDependency> IMiddleware.Dependencies { get { return _dependencies; } }

        string IMiddleware.Name { get; set; }

        public DartMiddleware()
        {
            this.RunAfter<IOutputCache>(null, false);
        }

        Task IRoutingProcessor.RouteRequest(IOwinContext context, Func<Task> next)
        {
            var trace = (TextWriter)context.Environment["host.TraceOutput"];
            if (trace != null) trace.WriteLine(GetType().Name + " RouteRequest() starting " + context.Request.Uri);

            PathString relativePath;
            if (_rootUrl.HasValue && context.Request.Path.StartsWithSegments(_rootUrl, out relativePath))
            {
                if (!relativePath.HasValue || relativePath.Value == "/")
                {
                    if (trace != null) trace.WriteLine(GetType().Name + " selecting the default document " + _defaultDocument);
                    relativePath = _defaultDocument;
                }

                var dartContext = new DartContext();
                context.SetFeature<IRequestRewriter>(dartContext);
                context.SetFeature<IDart>(dartContext);

                var userAgent = context.Request.Headers["user-agent"];
                dartContext.IsDartSupported = userAgent != null &&
                                              userAgent.IndexOf("(Dart)", StringComparison.OrdinalIgnoreCase) > -1;

                if (dartContext.IsDartSupported)
                {
                    if (trace != null) trace.WriteLine(GetType().Name + " the browser supports Dart");
                    context.Request.Path = _rootDartFolder + relativePath;
                }
                else
                {
                    if (trace != null) trace.WriteLine(GetType().Name + " the browser does not support Dart");
                    context.Request.Path = _rootBuildFolder + relativePath;
                }

                var outputCache = context.GetFeature<InterfacesV1.Upstream.IUpstreamOutputCache>();
                if (outputCache != null)
                {
                    if (trace != null) trace.WriteLine(GetType().Name + " disabling output caching");
                    outputCache.UseCachedContent = false;
                }
            }

            var result = next();

            if (trace != null) trace.WriteLine(GetType().Name + " RouteRequest() finished");
            return result;
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

            var result = next();

            if (trace != null) trace.WriteLine(GetType().Name + " Invoke() finished");
            return result;
        }

        #region IConfigurable

        private IDisposable _configurationRegistration;
        private DartConfiguration _configuration = new DartConfiguration();

        private PathString _rootDartFolder;
        private PathString _rootBuildFolder;
        private PathString _rootUrl;
        private PathString _defaultDocument;

        public void Configure(IConfiguration configuration, string path)
        {
            _configurationRegistration = configuration.Register(
                path, 
                cfg => 
                    {
                        _configuration = cfg;

                        Func<string, PathString> normalizeUrl = u =>
                        {
                            if (u == null)
                                return new PathString();

                            u = u.Replace("\\", "/");

                            if (u.Length > 0 && u.EndsWith("/"))
                                u = u.Substring(0, u.Length - 1);

                            if (!u.StartsWith("/"))
                                u = "/" + u;

                            return new PathString(u);
                        };

                        _rootDartFolder = normalizeUrl(cfg.DartUiRootUrl);
                        _rootBuildFolder = normalizeUrl(cfg.CompiledUiRootUrl);
                        _rootUrl = normalizeUrl(cfg.UiRootUrl);
                        _defaultDocument = normalizeUrl(cfg.DefaultDocument);
                    }, 
                    new DartConfiguration());
        }

        #endregion

        #region ISelfDocumenting

        private Task DocumentConfiguration(IOwinContext context)
        {
            var document = GetScriptResource("configuration.html");
            document = document.Replace("{dartUiRootUrl}", _configuration.DartUiRootUrl);
            document = document.Replace("{defaultDocument}", _configuration.DefaultDocument);
            document = document.Replace("{documentationRootUrl}", _configuration.DocumentationRootUrl);
            document = document.Replace("{uiRootUrl}", _configuration.UiRootUrl);
            document = document.Replace("{compiledUiRootUrl}", _configuration.CompiledUiRootUrl);

            var defaultConfiguration = new DartConfiguration();
            document = document.Replace("{dartUiRootUrl.default}", defaultConfiguration.DartUiRootUrl);
            document = document.Replace("{defaultDocument.default}", defaultConfiguration.DefaultDocument);
            document = document.Replace("{documentationRootUrl.default}", defaultConfiguration.DocumentationRootUrl);
            document = document.Replace("{uiRootUrl.default}", defaultConfiguration.UiRootUrl);
            document = document.Replace("{compiledUiRootUrl.default}", defaultConfiguration.CompiledUiRootUrl);

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
            get { return "Rerwites requests for browsers that support Dart to Dart source files instead of compiled JavaScript"; }
        }

        public string ShortDescription
        {
            get { return "Rerwites requests for browsers that support Dart to Dart source files instead of compiled JavaScript"; }
        }

        public IList<InterfacesV1.Capability.IEndpointDocumentation> Endpoints 
        { 
            get 
            {
                var documentation = new List<InterfacesV1.Capability.IEndpointDocumentation>
                {
                    new EndpointDocumentation
                    {
                        RelativePath = _configuration.UiRootUrl,
                        Description = "A user interface written in the Dart programming language",
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

        private class DartContext : IDart
        {
            public bool IsDartSupported { get; set; }
        }
    }
}
