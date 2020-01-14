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
using OwinFramework.InterfacesV1.Middleware;
using OwinFramework.MiddlewareHelpers.EmbeddedResources;
using OwinFramework.MiddlewareHelpers.SelfDocumenting;
using OwinFramework.MiddlewareHelpers.Traceable;

namespace OwinFramework.Documenter
{
    public class DocumenterMiddleware:
        IMiddleware<IResponseProducer>, 
        IConfigurable, 
        ISelfDocumenting,
        ITraceable
    {
        private readonly ResourceManager _resourceManager;
        private const string ConfigDocsPath = "/docs/configuration";

        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        public IList<IDependency> Dependencies { get { return _dependencies; } }

        public string Name { get; set; }
        public Action<IOwinContext, Func<string>> Trace { get; set; }

        private readonly TraceFilter _traceFilter;

        public DocumenterMiddleware(
            IHostingEnvironment hostingEnvironment)
        {
            this.RunAfter<IAuthorization>(null, false);
            this.RunAfter<IRequestRewriter>(null, false);
            this.RunAfter<IResponseRewriter>(null, false);

            _traceFilter = new TraceFilter(null, this);
            _resourceManager = new ResourceManager(hostingEnvironment, new MimeTypeEvaluator());
        }

        public Task Invoke(IOwinContext context, Func<Task> next)
        {
            string path;
            if (!IsForThisMiddleware(context, out path))
                return next();

            if (context.Request.Path.Value.Equals(path, StringComparison.OrdinalIgnoreCase))
            {
                _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " returning middleware documentation");
                return GenerateDocumentation(context);
            }

            if (context.Request.Path.Value.Equals(path + ConfigDocsPath, StringComparison.OrdinalIgnoreCase))
            {
                _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " returning configuration documentation");
                return DocumentConfiguration(context);
            }

            throw new Exception("This request looked like it was for the documenter middleware, but the middleware did not know how to handle it.");
        }

        private Task GenerateDocumentation(IOwinContext context)
        {
            var documentation = GetDocumentation(context);

            context.Response.ContentType = "text/html";

            var pageTemplate = GetResource("pageTemplate.html");
            var middlewareTemplate = GetResource("middlewareTemplate.html");
            var endpointTemplate = GetResource("endpointTemplate.html");
            var attributeListTemplate = GetResource("attributeListTemplate.html");
            var attributeTemplate = GetResource("attributeTemplate.html");
            var examplesTemplate = GetResource("examplesTemplate.html");

            var indexTemplate = GetResource("indexTemplate.html");
            var endpointIndexEntryTemplate = GetResource("endpointIndexEntryTemplate.html");
            var middlewareIndexEntryTemplate = GetResource("middlewareIndexEntryTemplate.html");

            var middlwareContent = new StringBuilder();
            var endpointsContent = new StringBuilder();
            var attributesContent = new StringBuilder();
            var indexContent = new StringBuilder();

            foreach (var middleware in documentation)
            {
                var type = middleware.SelfDocumenting.GetType();
                var middlewareHtml = middlewareIndexEntryTemplate
                        .Replace("{id}", type.FullName)
                        .Replace("{name}", type.Name)
                        .Replace("{description}", middleware.Name);
                indexContent.AppendLine(middlewareHtml);

                if (middleware.Endpoints != null)
                {
                    foreach (var endpoint in middleware.Endpoints)
                    {
                        var methods = endpoint.Attributes == null 
                            ? string.Empty 
                            : string.Join(", ", endpoint.Attributes
                                .Where(a => string.Equals("method", a.Type, StringComparison.OrdinalIgnoreCase))
                                .Select(a => a.Name.ToUpper()));

                        var endpointHtml = endpointIndexEntryTemplate
                            .Replace("{id}", endpoint.RelativePath.Replace("/", "_"))
                            .Replace("{methods}", methods)
                            .Replace("{path}", endpoint.RelativePath);
                        indexContent.AppendLine(endpointHtml);
                    }
                }
            }

            foreach (var middleware in documentation)
            {
                var type = middleware.SelfDocumenting.GetType();
                var middlewareHtml = middlewareTemplate
                        .Replace("{id}", type.FullName)
                        .Replace("{type}", type.FullName)
                        .Replace("{name}", middleware.Name)
                        .Replace("{description}", middleware.Description);
                middlwareContent.AppendLine(middlewareHtml);
            }

            var endpoints = ExtractEndpointDocumentation(documentation);

            foreach (var endpoint in endpoints)
            {
                var attributeListHtml = "";
                if (endpoint.Attributes != null && endpoint.Attributes.Count > 0)
                {
                    attributesContent.Clear();
                    foreach (var attribute in endpoint.Attributes)
                    {
                        var attributeHtml = attributeTemplate
                            .Replace("{type}", attribute.Type)
                            .Replace("{name}", attribute.Name)
                            .Replace("{description}", attribute.Description);
                        attributesContent.Append(attributeHtml);
                    }
                    attributeListHtml = attributeListTemplate.Replace("{attributes}", attributesContent.ToString());
                }

                var examplesHtml = "";
                if (!string.IsNullOrEmpty(endpoint.Examples))
                {
                    examplesHtml = examplesTemplate.Replace("{examples}", endpoint.Examples);
                }

                var endpointHtml = endpointTemplate
                    .Replace("{id}", endpoint.RelativePath.Replace("/", "_"))
                    .Replace("{path}", endpoint.RelativePath)
                    .Replace("{description}", endpoint.Description)
                    .Replace("{examples}", examplesHtml)
                    .Replace("{attributes}", attributeListHtml);
                endpointsContent.AppendLine(endpointHtml);
            }

            var pageHtml = pageTemplate
                .Replace("{index}", indexTemplate.Replace("{index}", indexContent.ToString()))
                .Replace("{middleware}", middlwareContent.ToString())
                .Replace("{endpoints}", endpointsContent.ToString());
            return context.Response.WriteAsync(pageHtml);
        }

        private string GetResource(string fileName)
        {
            var resource = _resourceManager.GetResource(Assembly.GetExecutingAssembly(), fileName.ToLower());
            if (resource == null || resource.Content == null)
                return string.Empty;

            return Encoding.UTF8.GetString(resource.Content);
        }

        #region Gathering documentation

        private IList<MiddlewareDocumentation> GetDocumentation(IOwinContext context)
        {
            var router = context.Get<IRouter>("OwinFramework.Router");
            if (router == null)
                throw new Exception("The documenter can only be used if you used OwinFramework to build your OWIN pipeline.");

            var documentation = new List<MiddlewareDocumentation>();
            AddDocumentation(documentation, router);

            return documentation.OrderBy(m => m.ClassName).ToList();
        }

        private IList<EndpointDocumentation> ExtractEndpointDocumentation(IEnumerable<MiddlewareDocumentation> documentation)
        {
            var endpoints = new List<EndpointDocumentation>();

            foreach (var middleware in documentation.Where(d => d.Endpoints != null))
            {
                foreach (var endpointDocumentation in middleware.Endpoints)
                {
                    var newEndpoint = endpointDocumentation;
                    var existingEndpoint = endpoints.FirstOrDefault(
                            e => string.Equals(e.RelativePath, newEndpoint.RelativePath, StringComparison.InvariantCultureIgnoreCase));

                    if (existingEndpoint == null)
                    {
                        endpoints.Add(new EndpointDocumentation 
                        {
                            RelativePath = newEndpoint.RelativePath,
                            Description = newEndpoint.Description,
                            Examples = newEndpoint.Examples,
                            Attributes = newEndpoint.Attributes == null ? null : newEndpoint.Attributes.ToList()
                        });
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(existingEndpoint.Description))
                            existingEndpoint.Description = newEndpoint.Description;

                        if (string.IsNullOrEmpty(existingEndpoint.Examples))
                            existingEndpoint.Examples = newEndpoint.Examples;
                        else if (!string.IsNullOrEmpty(newEndpoint.Examples))
                            existingEndpoint.Examples += newEndpoint.Examples;

                        if (existingEndpoint.Attributes == null || existingEndpoint.Attributes.Count == 0)
                            existingEndpoint.Attributes = newEndpoint.Attributes;
                        else if (newEndpoint.Attributes != null && newEndpoint.Attributes.Count > 0)
                            ((List<IEndpointAttributeDocumentation>)existingEndpoint.Attributes).AddRange(newEndpoint.Attributes);
                    }
                }
            }

            return endpoints.OrderBy(e => e.RelativePath).ToList();
        }

        private void AddDocumentation(
            IList<MiddlewareDocumentation> documentation, 
            IRouter router)
        {
            if (router.Segments != null)
            {
                foreach (var segment in router.Segments)
                {
                    if (segment.Middleware != null)
                    {
                        foreach (var middleware in segment.Middleware)
                        {
                            AddDocumentation(documentation, middleware);
                        }
                    }
                }
            }
        }

        private void AddDocumentation(
            IList<MiddlewareDocumentation> documentation, 
            IMiddleware middleware)
        {
            var selfDocumenting = middleware as ISelfDocumenting;
            if (selfDocumenting != null)
            {
                if (documentation.All(d => d.SelfDocumenting.GetType() != selfDocumenting.GetType()))
                {
                    var middlewareDocumentation = new MiddlewareDocumentation
                    {
                        SelfDocumenting = selfDocumenting,
                        ClassName = selfDocumenting.GetType().FullName,
                        Name = selfDocumenting.ShortDescription,
                        Description = selfDocumenting.LongDescription,
                        Endpoints = selfDocumenting.Endpoints == null 
                            ? null 
                            : selfDocumenting.Endpoints.OrderBy(e => e.RelativePath).ToList()
                    };
                    documentation.Add(middlewareDocumentation);
                }
            }

            var router = middleware as IRouter;
            if (router != null) AddDocumentation(documentation, router);
        }

        private class MiddlewareDocumentation
        {
            public ISelfDocumenting SelfDocumenting { get; set; }
            public string ClassName{ get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public List<IEndpointDocumentation> Endpoints { get; set; }
        }

        #endregion

        #region IConfigurable

        private IDisposable _configurationRegistration;
        private DocumenterConfiguration _configuration = new DocumenterConfiguration();

        private bool IsForThisMiddleware(IOwinContext context, out string path)
        {
            // Note that the configuration can be changed at any time by another thread
            path = _configuration.Path;

            return _configuration.Enabled
                   && !string.IsNullOrEmpty(path)
                   && context.Request.Path.HasValue
                   && context.Request.Path.Value.StartsWith(path, StringComparison.OrdinalIgnoreCase);
        }

        void IConfigurable.Configure(IConfiguration configuration, string path)
        {
            _traceFilter.ConfigureWith(configuration);

            _configurationRegistration = configuration.Register(
                path, 
                cfg => 
                    {
                        _configuration = cfg;
                        _resourceManager.LocalResourcePath = cfg.LocalFilePath;
                    }, 
                new DocumenterConfiguration());
        }


        #endregion

        #region ISelfDocumenting

        private Task DocumentConfiguration(IOwinContext context)
        {
            var document = GetResource("configuration.html");

            document = document.Replace("{path}", _configuration.Path);
            document = document.Replace("{enabled}", _configuration.Enabled.ToString());
            document = document.Replace("{requiredPermission}", _configuration.RequiredPermission ?? "<none>");

            var defaultConfiguration = new DocumenterConfiguration();
            document = document.Replace("{path.default}", defaultConfiguration.Path);
            document = document.Replace("{enabled.default}", defaultConfiguration.Enabled.ToString());
            document = document.Replace("{requiredPermission.default}", defaultConfiguration.RequiredPermission ?? "<none>");

            context.Response.ContentType = "text/html";
            return context.Response.WriteAsync(document);
        }

        Uri ISelfDocumenting.GetDocumentation(DocumentationTypes documentationType)
        {
            switch (documentationType)
            {
                case DocumentationTypes.Configuration:
                    return new Uri(_configuration.Path + ConfigDocsPath, UriKind.Relative);
                case DocumentationTypes.Overview:
                    return new Uri("https://github.com/Bikeman868/OwinFramework.Middleware", UriKind.Absolute);
                case DocumentationTypes.SourceCode:
                    return new Uri("https://github.com/Bikeman868/OwinFramework.Middleware/tree/master/OwinFramework.Documenter", UriKind.Absolute);
            }
            return null;
        }

        string ISelfDocumenting.LongDescription
        {
            get { return GetResource("middlewareDescription.html"); }
        }

        string ISelfDocumenting.ShortDescription
        {
            get { return "Middleware documentation generator"; }
        }

        IList<IEndpointDocumentation> ISelfDocumenting.Endpoints
        {
            get
            {
                var documentation = new List<IEndpointDocumentation>();
                if (_configuration.Enabled)
                {
                    documentation.Add(
                        new EndpointDocumentation
                        {
                            RelativePath = _configuration.Path,
                            Description = "Website endpoint documentation.",
                            Attributes = new List<IEndpointAttributeDocumentation>
                            {
                                new EndpointAttributeDocumentation
                                {
                                    Type = "Method",
                                    Name = "GET",
                                    Description = "Returns documentation for the endpoints in the application"
                                }
                            }
                        });
                    documentation.Add(

                        new EndpointDocumentation
                        {
                            RelativePath = _configuration.Path + ConfigDocsPath,
                            Description = "Documentation of the configuration options for the documenter middleware",
                            Attributes = new List<IEndpointAttributeDocumentation>
                            {
                                new EndpointAttributeDocumentation
                                {
                                    Type = "Method",
                                    Name = "GET",
                                    Description = "Returns documenter configuration documentation in HTML format"
                                }
                            }
                        });
                }
                return documentation;
            }
        }

        #endregion
    }
}
