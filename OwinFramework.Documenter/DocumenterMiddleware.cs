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

namespace OwinFramework.Documenter
{
    public class DocumenterMiddleware:
        IMiddleware<IResponseProducer>, 
        IConfigurable, 
        ISelfDocumenting
    {
        private const string ConfigDocsPath = "/docs/configuration";

        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        public IList<IDependency> Dependencies { get { return _dependencies; } }

        public string Name { get; set; }

        public DocumenterMiddleware()
        {
            this.RunAfter<IAuthorization>(null, false);
            this.RunAfter<IRequestRewriter>(null, false);
            this.RunAfter<IResponseRewriter>(null, false);
        }

        public Task Invoke(IOwinContext context, Func<Task> next)
        {
            string path;
            if (!IsForThisMiddleware(context, out path))
            {
                return next();
            }

#if DEBUG
            var trace = (TextWriter)context.Environment["host.TraceOutput"];
#endif
            if (context.Request.Path.Value.Equals(path, StringComparison.OrdinalIgnoreCase))
            {
#if DEBUG
                if (trace != null) trace.WriteLine(GetType().Name + " returning middleware documentation");
#endif
                return GenerateDocumentation(context);
            }

            if (context.Request.Path.Value.Equals(path + ConfigDocsPath, StringComparison.OrdinalIgnoreCase))
            {
#if DEBUG
                if (trace != null) trace.WriteLine(GetType().Name + " returning configuration documentation");
#endif
                return DocumentConfiguration(context);
            }

            throw new Exception("This request looked like it was for the documenter middleware, but the middleware did not know how to handle it.");
        }

        private Task GenerateDocumentation(IOwinContext context)
        {
            var documentation = GetDocumentation(context);

            context.Response.ContentType = "text/html";

            var pageTemplate = GetScriptResource("pageTemplate.html");
            var endpointTemplate = GetScriptResource("endpointTemplate.html");
            var attributeListTemplate = GetScriptResource("attributeListTemplate.html");
            var attributeTemplate = GetScriptResource("attributeTemplate.html");
            var examplesTemplate = GetScriptResource("examplesTemplate.html");

            var endpointsContent = new StringBuilder();
            var attributesContent = new StringBuilder();

            foreach (var endpoint in documentation)
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
                    .Replace("{path}", endpoint.RelativePath)
                    .Replace("{description}", endpoint.Description)
                    .Replace("{examples}", examplesHtml)
                    .Replace("{attributes}", attributeListHtml);
                endpointsContent.AppendLine(endpointHtml);
            }

            return context.Response.WriteAsync(pageTemplate.Replace("{endpoints}", endpointsContent.ToString()));
        }

        #region Gathering documentation

        private IList<IEndpointDocumentation> GetDocumentation(IOwinContext context)
        {
            var router = context.Get<IRouter>("OwinFramework.Router");
            if (router == null)
                throw new Exception("The documenter can only be used if you used OwinFramework to build your OWIN pipeline.");

            var documentation = new List<IEndpointDocumentation>();
            var analysedMiddleware = new List<Type>();
            AddDocumentation(documentation, router, analysedMiddleware);

            return documentation.OrderBy(e => e.RelativePath).ToList();
        }

        private void AddDocumentation(
            IList<IEndpointDocumentation> documentation, 
            IRouter router, 
            IList<Type> analysedMiddleware)
        {
            if (router.Segments != null)
            {
                foreach (var segment in router.Segments)
                {
                    if (segment.Middleware != null)
                    {
                        foreach (var middleware in segment.Middleware)
                        {
                            AddDocumentation(documentation, middleware, analysedMiddleware);
                        }
                    }
                }
            }
        }

        private void AddDocumentation(
            IList<IEndpointDocumentation> documentation, 
            IMiddleware middleware, 
            IList<Type> analysedMiddleware)
        {
            var selfDocumenting = middleware as ISelfDocumenting;
            if (selfDocumenting != null)
            {
                if (!analysedMiddleware.Contains(middleware.GetType()))
                {
                    analysedMiddleware.Add(middleware.GetType());
                    if (selfDocumenting.Endpoints != null)
                    {
                        foreach (var endpoint in selfDocumenting.Endpoints)
                            documentation.Add(endpoint);
                    }
                }
            }

            var router = middleware as IRouter;
            if (router != null) AddDocumentation(documentation, router, analysedMiddleware);
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
            _configurationRegistration = configuration.Register(
                path, cfg => _configuration = cfg, new DocumenterConfiguration());
        }


        #endregion

        #region ISelfDocumenting

        private Task DocumentConfiguration(IOwinContext context)
        {
            var document = GetScriptResource("configuration.html");

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
            get { return "Produces documentation for the application developer about all of the installed middleware"; }
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
    }
}
