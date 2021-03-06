﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Owin;
using Newtonsoft.Json.Linq;
using OwinFramework.Builder;
using OwinFramework.Interfaces.Builder;
using OwinFramework.Interfaces.Routing;
using OwinFramework.InterfacesV1.Capability;
using OwinFramework.InterfacesV1.Middleware;
using OwinFramework.MiddlewareHelpers.SelfDocumenting;
using OwinFramework.MiddlewareHelpers.Traceable;

namespace OwinFramework.AnalysisReporter
{
    public class AnalysisReporterMiddleware:
        IMiddleware<IResponseProducer>, 
        IConfigurable, 
        ISelfDocumenting,
        ITraceable
    {
        private const string _configDocsPath = "/docs/configuration";

        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        public IList<IDependency> Dependencies { get { return _dependencies; } }

        public string Name { get; set; }
        public Action<IOwinContext, Func<string>> Trace { get; set; }

        private readonly TraceFilter _traceFilter;
        private DateTime _nextUpdate;

        public AnalysisReporterMiddleware()
        {
            this.RunAfter<IAuthorization>(null, false);
            this.RunAfter<IRequestRewriter>(null, false);
            this.RunAfter<IResponseRewriter>(null, false);

            _traceFilter = new TraceFilter(null, this);
        }

        public Task Invoke(IOwinContext context, Func<Task> next)
        {
            string path;
            if (!IsForThisMiddleware(context, out path))
                return next();

            if (context.Request.Path.Value.Equals(path, StringComparison.OrdinalIgnoreCase))
            {
                _traceFilter.Trace(context, TraceLevel.Debug, () => GetType().Name + " returning analysis report");
                return ReportAnalysis(context);
            }

            if (context.Request.Path.Value.Equals(path + _configDocsPath, StringComparison.OrdinalIgnoreCase))
            {
                _traceFilter.Trace(context, TraceLevel.Debug, () => GetType().Name + " returning configuration documentation");
                return DocumentConfiguration(context);
            }

            throw new Exception("This request looked like it was for the analysis reporter middleware, but the middleware did not know how to handle it.");
        }

        // Note that these two lists must be 1-1 and in the same order
        private enum ReportFormat 
        { 
            Html = 0, 
            Text = 1, 
            Markdown = 2, 
            Json = 3, 
            Xml = 4 
        }
        private readonly List<string> _supportedFormats = new List<string>()
        { 
            "text/html", 
            "text/plain", 
            "text/markdown", 
            "application/json", 
            "application/xml" 
        };
     
        private Task ReportAnalysis(IOwinContext context)
        {
            string mimeType = null;
            if (string.IsNullOrEmpty(context.Request.Accept))
            {
                mimeType = _configuration.DefaultFormat;
            }
            else
            {
                var acceptFormats = context.Request.Accept
                    .Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s));
                foreach (var acceptFormat in acceptFormats)
                {
                    if (acceptFormat == "*/*")
                    {
                        mimeType = _configuration.DefaultFormat;
                        break;
                    }
                    if (_supportedFormats.Contains(acceptFormat))
                    {
                        mimeType = acceptFormat;
                        break;
                    }
                }
            }

            if (mimeType == null)
            {
                context.Response.StatusCode = 406;
                context.Response.ReasonPhrase = "Not Acceptable";
                return context.Response.WriteAsync(
                    "The analysis reporter supports the following MIME types: " + 
                    string.Join(",", _supportedFormats));
            }
            context.Response.ContentType = mimeType;
            var reportFormat = (ReportFormat)_supportedFormats.IndexOf(mimeType);

            var analysis = GetAnalysisData(context);

            switch (reportFormat)
            {
                case ReportFormat.Html:
                    return RenderHtml(context, analysis);
                case ReportFormat.Text:
                    return RenderText(context, analysis);
                case ReportFormat.Markdown:
                    return RenderMarkdown(context, analysis);
                case ReportFormat.Json:
                    return RenderJson(context, analysis);
                case ReportFormat.Xml:
                    return RenderXml(context, analysis);
            }

            context.Response.StatusCode = 406;
            context.Response.ReasonPhrase = "Not Acceptable";
            return context.Response.WriteAsync("The " + reportFormat + " format is currently under development.");
        }

        private Task RenderText(IOwinContext context, IEnumerable<AnalysableInfo> analysisData)
        {
            var pageTemplate = GetResource("pageTemplate.txt");
            var analysableTemplate = GetResource("analysableTemplate.txt");
            var statisticTemplate = GetResource("statisticTemplate.txt");
            var linkTemplate = "";

            return RenderTemplates(context, analysisData, pageTemplate, analysableTemplate, statisticTemplate, linkTemplate);
        }

        private Task RenderMarkdown(IOwinContext context, IEnumerable<AnalysableInfo> analysisData)
        {
            var pageTemplate = GetResource("pageTemplate.md");
            var analysableTemplate = GetResource("analysableTemplate.md");
            var statisticTemplate = GetResource("statisticTemplate.md");
            var linkTemplate = "";

            return RenderTemplates(context, analysisData, pageTemplate, analysableTemplate, statisticTemplate, linkTemplate);
        }

        private Task RenderHtml(IOwinContext context, IEnumerable<AnalysableInfo> analysisData)
        {
            var pageTemplate = GetResource("pageTemplate.html");
            var analysableTemplate = GetResource("analysableTemplate.html");
            var statisticTemplate = GetResource("statisticTemplate.html");
            var linkTemplate = GetResource("linkTemplate.html");

            return RenderTemplates(context, analysisData, pageTemplate, analysableTemplate, statisticTemplate, linkTemplate);
        }

        private Task RenderTemplates(
            IOwinContext context, 
            IEnumerable<AnalysableInfo> analysisData,
            string pageTemplate,
            string analysableTemplate,
            string statisticTemplate,
            string linkTemplate)
        {
            var analysablesContent = new StringBuilder();
            var statisticsContent = new StringBuilder();
            var linksContent = new StringBuilder();

            foreach (var analysable in analysisData)
            {
                statisticsContent.Clear();
                foreach (var statistic in analysable.Statistics)
                {
                    statistic.Statistic.Refresh();
                    var statisticHtml = statisticTemplate
                        .Replace("{name}", statistic.Name)
                        .Replace("{units}", statistic.Units)
                        .Replace("{description}", statistic.Description)
                        .Replace("{value}", statistic.Statistic.Formatted);
                    statisticsContent.AppendLine(statisticHtml);
                }

                linksContent.Clear();
                if (analysable.Links != null && analysable.Links.Count > 0)
                {
                    foreach (var link in analysable.Links)
                    {
                        var linkHtml = linkTemplate
                            .Replace("{name}", link.Name)
                            .Replace("{url}", link.Url);
                        linksContent.Append(linkHtml);
                    }
                }

                var analysableHtml = analysableTemplate
                    .Replace("{name}", analysable.Name)
                    .Replace("{type}", analysable.Type)
                    .Replace("{description}", analysable.Description)
                    .Replace("{statistics}", statisticsContent.ToString())
                    .Replace("{links}", linksContent.ToString());
                analysablesContent.AppendLine(analysableHtml);
            }

            return context.Response.WriteAsync(pageTemplate.Replace("{analysables}", analysablesContent.ToString()));
        }

        private Task RenderJson(IOwinContext context, IEnumerable<AnalysableInfo> analysisData)
        {
            var json = new JObject();
            var analysablesArray = new JArray();
            json.Add("middleware", analysablesArray);

            foreach (var analysable in analysisData)
            {
                var analysableJson = new JObject();
                analysableJson.Add("name", analysable.Name);
                analysableJson.Add("type", analysable.Type);
                analysableJson.Add("description", analysable.Description);
                analysablesArray.Add(analysableJson);

                var statisticsArray = new JArray();
                analysableJson.Add("statistics", statisticsArray);

                foreach (var statistic in analysable.Statistics)
                {
                    statistic.Statistic.Refresh();
                    var statisticJson = new JObject();
                    statisticJson.Add("name", statistic.Name);
                    statisticJson.Add("units", statistic.Units);
                    statisticJson.Add("description", statistic.Description);
                    statisticJson.Add("value", statistic.Statistic.Value);
                    statisticJson.Add("denominator", statistic.Statistic.Denominator);
                    statisticJson.Add("formatted", statistic.Statistic.Formatted);
                    statisticsArray.Add(statisticJson);
                }
            }

            return context.Response.WriteAsync(json.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        private Task RenderXml(IOwinContext context, IEnumerable<AnalysableInfo> analysisData)
        {
            var document = new XDocument(new XDeclaration("1.0", "utf-8", "true"));
            var rootElement = new XElement("Analytics");
            document.Add(rootElement);

            foreach (var analysable in analysisData)
            {
                var middlewareElement = new XElement(
                    "Middleware",
                    new XAttribute("name", analysable.Name),
                    new XAttribute("type", analysable.Type),
                    new XElement("Description", analysable.Description)
                );
                rootElement.Add(middlewareElement);

                var statisticsElement = new XElement("Statistics");
                middlewareElement.Add(statisticsElement);

                foreach (var statistic in analysable.Statistics)
                {
                    statistic.Statistic.Refresh();

                    statisticsElement.Add(
                        new XElement(
                            "Statistic",
                            new XAttribute("name", statistic.Name),
                            new XElement("units", statistic.Units ?? ""),
                            new XElement("value", statistic.Statistic.Value),
                            new XElement("denominator", statistic.Statistic.Denominator),
                            new XElement("formatted", statistic.Statistic.Formatted),
                            new XElement("description", statistic.Description)));
                }
            }

            return context.Response.WriteAsync(document.ToString(SaveOptions.None));
        }

        #region Gathering analysis information

        private IList<AnalysableInfo> _stats;

        private IList<AnalysableInfo> GetAnalysisData(IOwinContext context)
        {
            if (_stats != null &&  DateTime.UtcNow < _nextUpdate) 
                return _stats;

            _nextUpdate = DateTime.UtcNow.AddMinutes(1);

            var router = context.Get<IRouter>("OwinFramework.Router");
            if (router == null)
                throw new Exception("The analysis reporter can only be used if you used OwinFramework to build your OWIN pipeline.");

            var stats = new List<AnalysableInfo>();
            var analysedMiddleware = new List<IMiddleware>();
            AddStats(stats, router, analysedMiddleware);

            _stats = stats;
            return stats;
        }

        private void AddStats(IList<AnalysableInfo> stats, IRouter router, IList<IMiddleware> analysedMiddleware)
        {
            if (router.Segments != null)
            {
                foreach (var segment in router.Segments)
                {
                    if (segment.Middleware != null)
                    {
                        foreach (var middleware in segment.Middleware)
                        {
                            if (!analysedMiddleware.Contains(middleware))
                            {
                                analysedMiddleware.Add(middleware);
                                AddStats(stats, middleware, analysedMiddleware);
                            }
                        }
                    }
                }
            }
        }

        private void AddStats(IList<AnalysableInfo> stats, IMiddleware middleware, IList<IMiddleware> analysedMiddleware)
        {
            var analysable = middleware as IAnalysable;
            if (analysable != null)
            {
                var analysableInfo = new AnalysableInfo
                {
                    Name = middleware.Name,
                    Type = middleware.GetType().FullName,
                    Statistics = new List<StatisticInfo>()
                };

                if (string.IsNullOrEmpty(analysableInfo.Name))
                    analysableInfo.Name = middleware.GetType().Name;

                var selfDocumenting = middleware as ISelfDocumenting;
                if (selfDocumenting != null)
                {
                    analysableInfo.Description = selfDocumenting.ShortDescription;
                    analysableInfo.Links = new List<DocumentationLink>();
                    foreach (var documentTypeValue in Enum.GetValues(typeof(DocumentationTypes)))
                    {
                        var documentType = (DocumentationTypes)documentTypeValue;
                        var url = selfDocumenting.GetDocumentation(documentType);
                        if (url != null)
                        {
                            analysableInfo.Links.Add(new DocumentationLink 
                            { 
                                Name = documentType.ToString(),
                                Url = url.ToString()
                            });
                        }
                    }
                }

                foreach (var availableStatistic in analysable.AvailableStatistics)
                {
                    var statisticInfo = new StatisticInfo
                    {
                        Name = availableStatistic.Name,
                        Description = availableStatistic.Description,
                        Units = availableStatistic.Units,
                        Statistic = analysable.GetStatistic(availableStatistic.Id)
                    };
                    analysableInfo.Statistics.Add(statisticInfo);
                }

                if (analysableInfo.Statistics.Count > 0)
                    stats.Add(analysableInfo);
            }

            var router = middleware as IRouter;
            if (router != null) AddStats(stats, router, analysedMiddleware);
        }

        private class AnalysableInfo
        {
            public string Name;
            public string Description;
            public string Type;
            public List<StatisticInfo> Statistics;
            public List<DocumentationLink> Links;
        }

        private class StatisticInfo
        {
            public string Name;
            public string Units;
            public string Description;
            public IStatistic Statistic;
        }

        private class DocumentationLink
        {
            public string Name;
            public string Url;
        }

        #endregion

        #region IConfigurable

        private IDisposable _configurationRegistration;
        private AnalysisReporterConfiguration _configuration = new AnalysisReporterConfiguration();

        private bool IsForThisMiddleware(IOwinContext context, out string path)
        {
            // Note that the configuration can be changed at any time by another thread
            path = _configuration.Path;

            if (!context.Request.Path.HasValue)
            {
                _traceFilter.Trace(context, TraceLevel.Debug, () => GetType().Name + " will not handle this request because it has no path");
                return false;
            }

            if (string.IsNullOrEmpty(path))
            {
                _traceFilter.Trace(context, TraceLevel.Error, () => GetType().Name + " has no request path configured");
                return false;
            }

            if (!context.Request.Path.Value.StartsWith(path, StringComparison.OrdinalIgnoreCase))
            {
                _traceFilter.Trace(context, TraceLevel.Debug, () => GetType().Name + " this request is not for this middleware");
                return false;
            }

            if (!_configuration.Enabled)
            {
                _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name+ " will not handle this request because it is disabled");
                return false;
            }

            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " is handling this request");
            return true;
        }

        void IConfigurable.Configure(IConfiguration configuration, string path)
        {
            _traceFilter.ConfigureWith(configuration);
            _configurationRegistration = configuration.Register(
                path, cfg => _configuration = cfg, new AnalysisReporterConfiguration());
        }


        #endregion

        #region ISelfDocumenting

        private Task DocumentConfiguration(IOwinContext context)
        {
            var document = GetResource("configuration.html");

            document = document.Replace("{path}", _configuration.Path);
            document = document.Replace("{enabled}", _configuration.Enabled.ToString());
            document = document.Replace("{requiredPermission}", _configuration.RequiredPermission ?? "<none>");
            document = document.Replace("{defaultFormat}", _configuration.DefaultFormat);

            var defaultConfiguration = new AnalysisReporterConfiguration();
            document = document.Replace("{path.default}", defaultConfiguration.Path);
            document = document.Replace("{enabled.default}", defaultConfiguration.Enabled.ToString());
            document = document.Replace("{requiredPermission.default}", defaultConfiguration.RequiredPermission ?? "<none>");
            document = document.Replace("{defaultFormat.default}", defaultConfiguration.DefaultFormat);

            context.Response.ContentType = "text/html";
            return context.Response.WriteAsync(document);
        }

        Uri ISelfDocumenting.GetDocumentation(DocumentationTypes documentationType)
        {
            switch (documentationType)
            {
                case DocumentationTypes.Configuration:
                    return new Uri(_configuration.Path + _configDocsPath, UriKind.Relative);
                case DocumentationTypes.Overview:
                    return new Uri("https://github.com/Bikeman868/OwinFramework.Middleware", UriKind.Absolute);
            }
            return null;
        }

        string ISelfDocumenting.LongDescription
        {
            get { return 
                "<p>Allows you to extract analysis information about your middleware for visual inspection "+
                "or for use in other applications such as health monitoring, trend analysis and system dashboard.</p>"+
                "<p>The results can be returned in different formats. Use the HTML standard <span class='code'>Accept</span> "+
                "header to specify the formats that you can accept in order of preference.</p>"; }
        }

        string ISelfDocumenting.ShortDescription
        {
            get { return "Generates a report with analytic information from middleware"; }
        }

        IList<IEndpointDocumentation> ISelfDocumenting.Endpoints
        {
            get
            {
                var documentation = new List<IEndpointDocumentation>
                {
                    new EndpointDocumentation
                    {
                        RelativePath = _configuration.Path,
                        Description = "Analytic information gathered from all the configured middleware that implements the IAnalysable interface.",
                        Examples = "<div class='code'><pre>GET " + _configuration.Path + "\nAccept: text/html</pre></div>",
                        Attributes = new List<IEndpointAttributeDocumentation>
                        {
                            new EndpointAttributeDocumentation
                            {
                                Type = "Method",
                                Name = "GET",
                                Description = "Returns analytics data for all analysable middleware in this web site"
                            },
                            new EndpointAttributeDocumentation
                            {
                                Type = "Header",
                                Name = "Accept",
                                Description =
                                    "Determines the format of the analytics returned. The following MIME types are supported" +
                                    "<ul><li>" + string.Join("</li><li>", _supportedFormats )+ "</li><li>*/*</li></ul>" +
                                    "When the Accept header contains */* then the default format is returned. You can change " +
                                    "this default via configuration."
                            }
                        }
                    },
                    new EndpointDocumentation
                    {
                        RelativePath = _configuration.Path + _configDocsPath,
                        Description = "Documentation of the configuration options for the analysis reporter middleware",
                        Attributes = new List<IEndpointAttributeDocumentation>
                        {
                            new EndpointAttributeDocumentation
                            {
                                Type = "Method",
                                Name = "GET",
                                Description = "Returns analysis reporter configuration documentation in HTML format"
                            }
                        }
                    },
                };
                return documentation;
            }
        }

        #endregion

        #region Embedded resources

        private string GetResource(string filename)
        {
            var resourceName = Assembly.GetExecutingAssembly()
                .GetManifestResourceNames()
                .FirstOrDefault(n => n.Contains(filename));

            if (resourceName == null)
                throw new Exception("Failed to find embedded resource " + filename);

            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new Exception("Failed to open embedded resource " + resourceName);

                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        #endregion
    }
}
