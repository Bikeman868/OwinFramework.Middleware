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
using OwinFramework.Interfaces.Utility;
using OwinFramework.InterfacesV1.Capability;
using OwinFramework.InterfacesV1.Middleware;
using OwinFramework.MiddlewareHelpers.Analysable;

namespace OwinFramework.NotFound
{
    public class NotFoundMiddleware:
        IMiddleware<IResponseProducer>,
        IConfigurable,
        ISelfDocumenting,
        IAnalysable
    {
        private readonly IHostingEnvironment _hostingEnvironment;
        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        IList<IDependency> IMiddleware.Dependencies { get { return _dependencies; } }

        string IMiddleware.Name { get; set; }

        private int _notFoundCount;

        private IDisposable _configurationRegistration;
        private NotFoundConfiguration _configuration;
        private FileInfo _pageTemplateFile;
        private PathString _configPage;

        public NotFoundMiddleware(IHostingEnvironment hostingEnvironment)
        {
            _hostingEnvironment = hostingEnvironment;
            ConfigurationChanged(new NotFoundConfiguration());
            this.RunLast();
        }

        public Task Invoke(IOwinContext context, Func<Task> next)
        {
#if DEBUG
            var trace = (TextWriter)context.Environment["host.TraceOutput"];
#endif
            if (context.Request.Path == _configPage)
            {
#if DEBUG
                if (trace != null) trace.WriteLine(GetType().Name + " returning configuration documentation");
#endif
                return DocumentConfiguration(context);
            }

            string template;
            if (_pageTemplateFile != null && _pageTemplateFile.Exists)
            {
#if DEBUG
                if (trace != null) trace.WriteLine(GetType().Name + " returning template file");
#endif
                using (var stream = _pageTemplateFile.Open(FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var reader = new StreamReader(stream, true);
                    template = reader.ReadToEnd();
                }

                var outputCache = context.GetFeature<IOutputCache>();
                if (outputCache != null)
                {
                    outputCache.Priority = CachePriority.Low;
#if DEBUG
                    if (trace != null) trace.WriteLine(GetType().Name + " setting output cache priority " + outputCache.Priority);
#endif
                }
            }
            else
            {
#if DEBUG
                if (trace != null) trace.WriteLine(GetType().Name + " returning embedded template response");
#endif
                template = GetEmbeddedResource("template.html");
            }

            _notFoundCount++;
            context.Response.StatusCode = 404;
            context.Response.ReasonPhrase = "Not Found";
            return context.Response.WriteAsync(template);
        }

        #region IConfigurable

        public void Configure(IConfiguration configuration, string path)
        {
            _configurationRegistration = configuration.Register(
                path,
                ConfigurationChanged,
                _configuration);
        }

        private void ConfigurationChanged(NotFoundConfiguration configuration)
        {
            FileInfo pageTemplateFile = null;
            if (!string.IsNullOrEmpty(configuration.Template))
            {
                var fullPath = _hostingEnvironment.MapPath(configuration.Template);
                pageTemplateFile = new FileInfo(fullPath);
            }

            if (string.IsNullOrEmpty(configuration.DocumentationRootUrl))
                configuration.DocumentationRootUrl = null;
            else if (!configuration.DocumentationRootUrl.StartsWith("/"))
                configuration.DocumentationRootUrl = "/" + configuration.DocumentationRootUrl;

            _configuration = configuration;
            _configPage = configuration.DocumentationRootUrl == null ? PathString.Empty : new PathString(configuration.DocumentationRootUrl);
            _pageTemplateFile = pageTemplateFile;
        }

        #endregion

        #region ISelfDocumenting

        private Task DocumentConfiguration(IOwinContext context)
        {
            var document = GetEmbeddedResource("configuration.html");
            document = document.Replace("{template}", _configuration.Template);
            document = document.Replace("{documentationUrl}", _configuration.DocumentationRootUrl);

            var defaultConfiguration = new NotFoundConfiguration();
            document = document.Replace("{template.default}", defaultConfiguration.Template);
            document = document.Replace("{documentationUrl.default}", defaultConfiguration.DocumentationRootUrl);

            context.Response.ContentType = "text/html";
            return context.Response.WriteAsync(document);
        }

        public Uri GetDocumentation(DocumentationTypes documentationType)
        {
            switch (documentationType)
            {
                case DocumentationTypes.Configuration:
                    return string.IsNullOrEmpty(_configuration.DocumentationRootUrl) 
                        ? null 
                        : new Uri(_configuration.DocumentationRootUrl, UriKind.Relative);
                case DocumentationTypes.Overview:
                    return new Uri("https://github.com/Bikeman868/OwinFramework.Middleware", UriKind.Absolute);
                case DocumentationTypes.SourceCode:
                    return new Uri("https://github.com/Bikeman868/OwinFramework.Middleware/tree/master/OwinFramework.NotFound", UriKind.Absolute);
            }
            return null;
        }

        public string LongDescription
        {
            get { return "Returns a templated response with a status code of 404 (not found)"; }
        }

        public string ShortDescription
        {
            get { return "Returns a status 404 response"; }
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
                            Description = "Documentation of the configuration options for the Not Found middleware",
                            Attributes = new List<IEndpointAttributeDocumentation>
                                {
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Method",
                                        Name = "GET",
                                        Description = "Returns configuration documentation for Not Found middleware in HTML format"
                                    }
                                }
                        });
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

        #region IAnalysable

        public IList<IStatisticInformation> AvailableStatistics
        {
            get
            {
                var stats = new List<IStatisticInformation>();
                stats.Add(
                    new StatisticInformation
                    {
                        Id = "NotFoundCount",
                        Name = "Request count",
                        Description = "The number of requests for URLs that were not found",
                        Explanation = "When no other middleware knows how to handle the request, the request passes down to this middleware, which always returns a status 404 response (not found)"
                    });
                return stats;
            }
        }

        public IStatistic GetStatistic(string id)
        {
            switch (id)
            {
                case "NotFoundCount":
                    return new IntStatistic(() => _notFoundCount);
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
    }
}
