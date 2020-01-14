using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Owin;
using Newtonsoft.Json;
using OwinFramework.Builder;
using OwinFramework.Interfaces.Builder;
using OwinFramework.Interfaces.Routing;
using OwinFramework.InterfacesV1.Capability;
using OwinFramework.InterfacesV1.Middleware;
using OwinFramework.MiddlewareHelpers.SelfDocumenting;
using OwinFramework.MiddlewareHelpers.Traceable;

namespace OwinFramework.DefaultDocument
{
    public class DefaultDocumentMiddleware:
        IMiddleware<IRequestRewriter>,
        IRoutingProcessor,
        ISelfDocumenting,
        IConfigurable,
        ITraceable
    {
        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        IList<IDependency> IMiddleware.Dependencies { get { return _dependencies; } }

        string IMiddleware.Name { get; set; }
        public Action<IOwinContext, Func<string>> Trace { get; set; }

        private readonly TraceFilter _traceFilter;

        public DefaultDocumentMiddleware()
        {
            _traceFilter = new TraceFilter(null, this);

            ConfigurationChanged(new DefaultDocumentConfiguration());
            this.RunFirst();
        }

        public Task RouteRequest(IOwinContext context, Func<Task> next)
        {
            var configuration = _configuration;
            if (!configuration.Enabled) return next();

            var requestRewriter = context.GetFeature<IRequestRewriter>() ?? new DefaultDocumentContext(context);

            if (!context.Request.Path.HasValue || context.Request.Path.Value == "/" || context.Request.Path.Value == "")
            {
                _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " identified a request for the default document");
                if (configuration.DefaultPageString.HasValue)
                {
                    _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " modifying request path to '" + configuration.DefaultPageString.Value + "'");
                    context.Request.Path = configuration.DefaultPageString;
                    context.SetFeature(requestRewriter);
                    return next();
                }
            }

            if (configuration.DefaultFolderPaths != null)
            {
                foreach(var path in configuration.DefaultFolderPaths)
                {
                    if (context.Request.Path.Equals(path.FolderPathString))
                    {
                        var p = path;
                        _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " modifying '" + p.FolderPathString + "' path to '" + p.DefaultPageString.Value + "'");
                        context.Request.Path = path.DefaultPageString;
                        context.SetFeature(requestRewriter);
                        return next();
                    }
                }
            }

            return next();
        }

        public Task Invoke(IOwinContext context, Func<Task> next)
        {
            var configuration = _configuration;
            if (!configuration.Enabled) return next();

            if (configuration.DocumentationRootUrlString.HasValue && context.Request.Path.Equals(configuration.DocumentationRootUrlString))
            {
                _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " returning configuration documentation");
                return DocumentConfiguration(context);
            }

            return next();
        }

        #region IConfigurable

        private IDisposable _configurationRegistration;
        private DefaultDocumentConfiguration _configuration;

        public void Configure(IConfiguration configuration, string path)
        {
            _traceFilter.ConfigureWith(configuration);

            _configurationRegistration = configuration.Register(
                path,
                ConfigurationChanged,
                new DefaultDocumentConfiguration());
        }

        private void ConfigurationChanged(DefaultDocumentConfiguration configuration)
        {
            _configuration = configuration.Sanitize();
        }

        #endregion

        #region ISelfDocumenting

        private Task DocumentConfiguration(IOwinContext context)
        {
            var document = GetEmbeddedResource("configuration.html");
            document = document.Replace("{enabled}", _configuration.Enabled.ToString());
            document = document.Replace("{defaultPage}", _configuration.DefaultPageString.Value);
            document = document.Replace("{configUrl}", _configuration.DocumentationRootUrlString.Value);
            document = document.Replace("{paths}", _configuration.DefaultFolderPaths == null ? "" : JsonConvert.SerializeObject(_configuration.DefaultFolderPaths));

            var defaultConfiguration = new DefaultDocumentConfiguration();
            document = document.Replace("{enabled.default}", defaultConfiguration.Enabled.ToString());
            document = document.Replace("{defaultPage.default}", defaultConfiguration.DefaultPageString.Value);
            document = document.Replace("{configUrl.default}", defaultConfiguration.DocumentationRootUrlString.Value);
            document = document.Replace("{paths.default}", defaultConfiguration.DefaultFolderPaths == null ? "" : JsonConvert.SerializeObject(defaultConfiguration.DefaultFolderPaths));

            context.Response.ContentType = "text/html";
            return context.Response.WriteAsync(document);
        }

        public IList<IEndpointDocumentation> Endpoints
        {
            get
            {
                var endpoints = new List<IEndpointDocumentation>();

                if (_configuration.Enabled)
                {
                    if (_configuration.DefaultPageString.HasValue)
                    {
                        endpoints.Add(new EndpointDocumentation
                            {
                                RelativePath = "/",
                                Description =
                                    "Requests for the root URL of the website " +
                                    "with no document specified will be rewritten to " + _configuration.DefaultPageString.Value
                            });
                    }

                    if (_configuration.DefaultFolderPaths != null)
                    {
                        endpoints.AddRange(_configuration.DefaultFolderPaths.Select(fp =>
                            new EndpointDocumentation
                            {
                                RelativePath = fp.FolderPathString.Value,
                                Description = "Rewrite the request path to " + fp.DefaultPageString.Value
                            }));
                    }
                }
                return endpoints;
            }
        }

        public Uri GetDocumentation(DocumentationTypes documentationType)
        {
            if (_configuration.Enabled && 
                _configuration.DocumentationRootUrlString.HasValue &&
                documentationType == DocumentationTypes.Configuration)
                return new Uri(_configuration.DocumentationRootUrlString.Value, UriKind.Relative);

            return null;
        }

        public string LongDescription
        {
            get { return string.Empty; }
        }

        public string ShortDescription
        {
            get { return "Redirects requests with no page specified to a specific page within the website"; }
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
    
        private class DefaultDocumentContext: IRequestRewriter
        {
            public Uri OriginalUrl { get; set; }
            public PathString OriginalPath { get; set; }

            public DefaultDocumentContext(IOwinContext context)
            {
                OriginalUrl = new Uri(context.Request.Uri.ToString());
                OriginalPath = new PathString(context.Request.Path.Value);
            }
        }
    }
}
