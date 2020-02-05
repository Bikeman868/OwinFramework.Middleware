using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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
using OwinFramework.MiddlewareHelpers.Analysable;
using Svg;
using Svg.Transforms;

namespace OwinFramework.RouteVisualizer
{
    public class RouteVisualizerMiddleware: 
        IMiddleware<IResponseProducer>, 
        IConfigurable, 
        ISelfDocumenting, 
        IAnalysable, 
        IRoutingProcessor
    {
        private const float TextHeight = 12;
        private const float TextLineSpacing = 15;

        private const float BoxLeftMargin = 5;
        private const float BoxTopMargin = 5;

        private const float ChildHorizontalOffset = 20;
        private const float ChildVericalSpacing = 10;
        private const float SiblingHorizontalSpacing = 15;

        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        public IList<IDependency> Dependencies { get { return _dependencies; } }

        public string Name { get; set; }

        private IDisposable _configurationRegistration;
        private RouteVisualizerConfiguration _configuration = new RouteVisualizerConfiguration();

        private PathString _visualizationPath;
        private PathString _documentationPath;

        public RouteVisualizerMiddleware()
        {
            this.RunAfter<IAuthorization>(null, false);
            this.RunAfter<IRequestRewriter>(null, false);
            this.RunAfter<IResponseRewriter>(null, false);
            this.RunAfter<IOutputCache>(null, false);
        }

        public Task RouteRequest(IOwinContext context, Func<Task> next)
        {
#if DEBUG
            var trace = (TextWriter)context.Environment["host.TraceOutput"];
#endif

            var requiredPermission = _configuration.RequiredPermission;
            if (_configuration.Enabled && !string.IsNullOrEmpty(requiredPermission))
            {
                if (context.Request.Path.Equals(_visualizationPath) || context.Request.Path.Equals(_documentationPath))
                {
                    var authorization = context.GetFeature<IUpstreamAuthorization>();
                    if (authorization != null)
                    {
#if DEBUG
                        if (trace != null) trace.WriteLine(GetType().Name + " requires " + requiredPermission + " permission");
#endif
                        authorization.AddRequiredPermission(requiredPermission);
                    }
                }
            }

            return next();
        }

        public Task Invoke(IOwinContext context, Func<Task> next)
        {
#if DEBUG
            var trace = (TextWriter)context.Environment["host.TraceOutput"];
#endif

            if (!_configuration.Enabled)
                return next();

            if (context.Request.Path.Equals(_visualizationPath))
            {
#if DEBUG
                if (trace != null) trace.WriteLine(GetType().Name + " returning route visualization");
#endif
                _requestCount++;
                return VisualizeRouting(context);
            }

            if (context.Request.Path.Equals(_documentationPath))
            {
#if DEBUG
                if (trace != null) trace.WriteLine(GetType().Name + " returning documentation");
#endif
                _requestCount++;
                return DocumentConfiguration(context);
            }
        
            return next();
        }

        void IConfigurable.Configure(IConfiguration configuration, string path)
        {
            _configurationRegistration = configuration.Register(
                path,
                cfg =>
                {
                    _configuration = cfg;

                    if (cfg.Enabled && !string.IsNullOrEmpty(cfg.Path))
                        _visualizationPath = new PathString(cfg.Path);
                    else
                        _visualizationPath = new PathString();

                    if (cfg.Enabled && !string.IsNullOrEmpty(cfg.DocumentationRootUrl))
                        _documentationPath = new PathString(cfg.DocumentationRootUrl);
                    else
                        _documentationPath = new PathString();
                }, 
                new RouteVisualizerConfiguration());
        }

        private Task VisualizeRouting(IOwinContext context)
        {
            var document = CreateDocument();

            SvgUnit width;
            SvgUnit height;
            DrawRoutes(document, context, 20, 20, out width, out height);

            SetDocumentSize(document, width, height);

            return Svg(context, document);
        }

        private Task DocumentConfiguration(IOwinContext context)
        {
            var document = GetScriptResource("configuration.html");

            document = document.Replace("{path}", _configuration.Path);
            document = document.Replace("{enabled}", _configuration.Enabled.ToString());
            document = document.Replace("{requiredPermission}", _configuration.RequiredPermission ?? "<none>");
            document = document.Replace("{analyticsEnabled}", _configuration.AnalyticsEnabled.ToString());
            document = document.Replace("{documentationRootUrl}", _configuration.DocumentationRootUrl);

            var defaultConfiguration = new RouteVisualizerConfiguration();
            document = document.Replace("{path.default}", defaultConfiguration.Path);
            document = document.Replace("{enabled.default}", defaultConfiguration.Enabled.ToString());
            document = document.Replace("{requiredPermission.default}", defaultConfiguration.RequiredPermission ?? "<none>");
            document = document.Replace("{analyticsEnabled.default}", defaultConfiguration.AnalyticsEnabled.ToString());
            document = document.Replace("{documentationRootUrl.default}", defaultConfiguration.DocumentationRootUrl);

            context.Response.ContentType = "text/html";
            return context.Response.WriteAsync(document);
        }

        private string GetScriptResource(string filename)
        {
            var scriptResourceName = Assembly.GetExecutingAssembly().GetManifestResourceNames().FirstOrDefault(n => n.Contains(filename));
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


        #region Creating SVG drawing

        protected SvgDocument CreateDocument()
        {
            var document = new SvgDocument
            {
                FontFamily = "Arial",
                FontSize = TextHeight
            };

            var styles = GetScriptResource("svg.css");
            if (!string.IsNullOrEmpty(styles))
            {
                var styleElement = new NonSvgElement("style");
                styleElement.Content = "\n" + styles;
                document.Children.Add(styleElement);
            }

            var script = GetScriptResource("svg.js");
            if (!string.IsNullOrEmpty(script))
            {
                document.CustomAttributes.Add("onload", "init(evt)");
                var scriptElement = new NonSvgElement("script");
                scriptElement.CustomAttributes.Add("type", "text/ecmascript");
                scriptElement.Content = "\n" + script;
                document.Children.Add(scriptElement);
            }

            return document;
        }

        private void SetDocumentSize(SvgDocument document, SvgUnit width, SvgUnit height)
        {
            document.Width = width;
            document.Height = height;
            document.ViewBox = new SvgViewBox(0, 0, width, height);
        }

        protected Task Svg(IOwinContext context, SvgDocument document)
        {
            try
            {
                string svg;
                using (var stream = new MemoryStream())
                {
                    document.Write(stream);
                    svg = Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
                }

                if (string.IsNullOrEmpty(svg))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.NoContent;
                    return Task.WhenAll();
                }

                context.Response.ContentType = "image/svg+xml";
                return context.Response.WriteAsync(svg);
            }
            catch (Exception ex)
            {
                context.Response.ContentType = "text/plain";
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                return context.Response.WriteAsync("Exception serializing SVG response: " + ex.Message);
            }
        }

        private void DrawRoutes(
            SvgDocument document, 
            IOwinContext context, 
            SvgUnit x,
            SvgUnit y,
            out SvgUnit width, 
            out SvgUnit height)
        {
            var router = context.Get<IRouter>("OwinFramework.Router");
            if (router == null)
                throw new Exception("The route vizualizer can only be used if you used OwinFramework to build your OWIN pipeline.");

            var root = PositionRouter(router);
            root.X = x;
            root.Y = y;

            Arrange(root);
            Draw(document, root);

            width = root.X + root.TreeWidth + 50;
            height = root.Y + root.TreeHeight + 50;
        }

        void Arrange(Positioned root)
        {
            root.TreeHeight = root.Height;
            root.TreeWidth = root.Width;

            var childX = root.X + ChildHorizontalOffset;
            var childY = root.Y + root.Height + ChildVericalSpacing;

            var siblingX = root.X + root.Width + SiblingHorizontalSpacing;
            var siblingY = root.Y;

            var x = childX;
            var y = childY;
            foreach (var child in root.Children)
            {
                child.X = x;
                child.Y = y;
                Arrange(child);
                y += child.TreeHeight + ChildVericalSpacing;
                if (child.X + child.TreeWidth > root.X + root.TreeWidth)
                    root.TreeWidth = child.TreeWidth + child.X - root.X;
            }
            if (y - root.Y - ChildVericalSpacing > root.TreeHeight)
            root.TreeHeight = y - root.Y - ChildVericalSpacing;

            x = siblingX;
            y = siblingY;
            foreach (var sibling in root.Siblings)
            {
                sibling.X = x;
                sibling.Y = y;
                Arrange(sibling);
                x += sibling.TreeWidth + SiblingHorizontalSpacing;
                if (sibling.Y + sibling.TreeHeight > root.Y + root.TreeHeight)
                    root.TreeHeight = sibling.TreeHeight + sibling.Y - root.Y;
            }
            if (x - root.X - SiblingHorizontalSpacing > root.TreeWidth)
                root.TreeWidth = x - root.X - SiblingHorizontalSpacing;
        }

        private void Draw(SvgDocument document, Positioned root)
        {
            root.DrawAction(document, root);

            foreach (var child in root.Children)
                Draw(document, child);

            foreach (var sibling in root.Siblings)
                Draw(document, sibling);
        }

        private class Positioned
        {
            public SvgUnit X;
            public SvgUnit Y;
            public SvgUnit Width;
            public SvgUnit Height;
            public SvgUnit TreeWidth;
            public SvgUnit TreeHeight;
            public Action<SvgDocument, Positioned> DrawAction;
            public IList<Positioned> Children = new List<Positioned>();
            public IList<Positioned> Siblings = new List<Positioned>();
        }

        private Positioned PositionRouter(IRouter router)
        {
            var lines = new List<string>
            {
                "Router",
                router.Name ?? "<anonymous>"
            };

            var positioned = new Positioned
            {
                Width = 120,
                Height = TextHeight * 4,
                DrawAction = (d, p) =>
                {
                    if (p.Children.Count > 0)
                    {
                        var maxChildY = p.Children.Select(c => c.Y.Value).Max();
                        DrawLine(d, p.X + ChildHorizontalOffset*2, p.Y + TextHeight, p.X + ChildHorizontalOffset*2, maxChildY, "route");
                    }
                    DrawBox(d, p.X, p.Y, p.Width, lines, "router", 2f);
                }
            };

            if (router.Segments != null)
            {
                positioned.Children = router
                    .Segments
                    .Select(PositionSegment)
                    .ToList();
            }

            return positioned;
        }

        private Positioned PositionSegment(IRoutingSegment segment)
        {
            var lines = new List<string>
            {
                "Route: " + (segment.Name ?? "<anonymous>")
            };

            var longestLine = lines.Select(l => l.Length).Max();

            var positioned = new Positioned
            {
                Width = longestLine * 6.5f,
                Height = TextHeight * (lines.Count + 2),
                DrawAction = (d, p) =>
                {
                    if (p.Siblings.Count > 0)
                    {
                        var maxSiblingX = p.Siblings.Select(s => s.X.Value).Max();
                        DrawLine(d, p.X + p.Width, p.Y + TextHeight, maxSiblingX, p.Y + TextHeight, "segment");
                    }
                    DrawBox(d, p.X, p.Y, p.Width, lines, "segment", 2f);
                }
            };

            if (segment.Middleware != null)
            {
                positioned.Siblings = segment
                    .Middleware
                    .Select(PositionMiddleware)
                    .ToList();
            }

            return positioned;
        }

        private Positioned PositionMiddleware(IMiddleware middleware)
        {
            var router = middleware as IRouter;
            if (router != null)
                return PositionRouter(router);

            var lines = new List<string>();

            lines.Add("Middleware " + middleware.GetType().Name);

            if (string.IsNullOrEmpty(middleware.Name))
                lines.Add("<anonymous>");
            else
                lines.Add("\"" + middleware.Name + "\"");

            var selfDocumenting = middleware as ISelfDocumenting;
            if (selfDocumenting != null)
            {
                lines.Add(selfDocumenting.ShortDescription);
            }

            var configurable = middleware as IConfigurable;
            if (configurable != null)
            {
                var line = "Configurable";
                if (selfDocumenting != null)
                {
                    var configurationUrl = selfDocumenting.GetDocumentation(DocumentationTypes.Configuration);
                    if (configurationUrl != null)
                        line += " (see " + configurationUrl + ")";
                }
                lines.Add(line);
            }

            var analysable = middleware as IAnalysable;
            if (analysable != null)
            {
                foreach (var stat in analysable.AvailableStatistics)
                {
                    var value  = analysable.GetStatistic(stat.Id);
                    if (value != null)
                    {
                        value.Refresh();
                        lines.Add(stat.Name + " " + value.Formatted);
                    }
                }
            }

            var longestLine = lines.Select(l => l.Length).Max();

            return new Positioned
            {
                Width = longestLine * 6.5f,
                Height = TextHeight * (lines.Count + 2),
                DrawAction = (d, p) =>
                    {
                        DrawBox(d, p.X, p.Y, p.Width, lines,"middleware", 2f);
                    }
            };
        }

        private void DrawLine(
            SvgDocument document,
            SvgUnit x1,
            SvgUnit y1,
            SvgUnit x2,
            SvgUnit y2,
            string cssClass)
        {
            var line = new SvgLine
            {
                StartX = x1,
                StartY = y1,
                EndX = x2,
                EndY = y2
            };
            if (!string.IsNullOrEmpty(cssClass))
                line.CustomAttributes.Add("class", cssClass);
            document.Children.Add(line);
        }

        private float DrawBox(
            SvgDocument document,
            SvgUnit x,
            SvgUnit y,
            SvgUnit width,
            IList<string> lines,
            string cssClass,
            SvgUnit cornerRadius)
        {
            var group = new SvgGroup();
            group.Transforms.Add(new SvgTranslate(x, y));

            if (!string.IsNullOrEmpty(cssClass))
                group.CustomAttributes.Add("class", cssClass);

            document.Children.Add(group);

            var height = TextLineSpacing * lines.Count + BoxTopMargin * 2;

            var rectangle = new SvgRectangle
            {
                Height = height,
                Width = width,
                CornerRadiusX = cornerRadius,
                CornerRadiusY = cornerRadius
            };
            group.Children.Add(rectangle);

            for (var lineNumber = 0; lineNumber < lines.Count; lineNumber++)
            {
                var text = new SvgText(lines[lineNumber]);
                text.Transforms.Add(new SvgTranslate(BoxLeftMargin, TextHeight + TextLineSpacing * lineNumber + BoxTopMargin));
                text.Children.Add(new SvgTextSpan());
                group.Children.Add(text);
            }

            return height;
        }

        #endregion

        #region ISelfDocumenting

        Uri ISelfDocumenting.GetDocumentation(DocumentationTypes documentationType)
        {
            switch (documentationType)
            {
                case DocumentationTypes.Configuration:
                    return new Uri(_documentationPath.Value, UriKind.Relative);
                case DocumentationTypes.Overview:
                    return new Uri("https://github.com/Bikeman868/OwinFramework.Middleware", UriKind.Absolute);
                case DocumentationTypes.TechnicalDetails:
                case DocumentationTypes.SourceCode:
                    return new Uri("https://github.com/Bikeman868/OwinFramework.Middleware/tree/master/OwinFramework.RouteVisualizer", UriKind.Absolute);
            }
            return null;
        }

        string ISelfDocumenting.LongDescription
        {
            get { return 
                "<p>Allows you to visually inspect your OWIN pipeline configuration to ensure that "+
                "your application will work as expected.</p>"+
                "<p>This is especially important for things like ensuring identificationa and "+
                "authorization middleware is running in front of middleware that needs to be secure.</p>"; }
        }

        string ISelfDocumenting.ShortDescription
        {
            get { return "Produces an SVG visualization of the OWIN pipeline"; }
        }

        IList<IEndpointDocumentation> ISelfDocumenting.Endpoints
        {
            get
            {
                var documentation = new List<IEndpointDocumentation>();

                if (_visualizationPath.HasValue)
                {
                    documentation.Add(
                        new EndpointDocumentation
                        {
                            RelativePath = _visualizationPath.Value,
                            Description = "An SVG drawing of the OWIN pipeline. Useful for diagnosing issues with your web application.",
                            Attributes = new List<IEndpointAttributeDocumentation>
                            {
                                new EndpointAttributeDocumentation
                                {
                                    Type = "Method",
                                    Name = "GET",
                                    Description = "Returns a drawing of the OWIN pipeline in SVG format"
                                }
                            }
                        });
                }

                if (_documentationPath.HasValue)
                {
                    documentation.Add(
                        new EndpointDocumentation
                        {
                            RelativePath = _documentationPath.Value,
                            Description = "Documentation of the configuration options for the route visualizer middleware",
                            Attributes = new List<IEndpointAttributeDocumentation>
                            {
                                new EndpointAttributeDocumentation
                                {
                                    Type = "Method",
                                    Name = "GET",
                                    Description = "Returns route visualizer configuration documentation in HTML format"
                                }
                            }
                        });
                }
                return documentation;
            }
        }

        private class EndpointDocumentation: IEndpointDocumentation
        {
            public string RelativePath { get; set; }
            public string Description { get; set; }
            public string Examples { get; set; }
            public IList<IEndpointAttributeDocumentation> Attributes { get; set; }
        }

        private class EndpointAttributeDocumentation: IEndpointAttributeDocumentation
        {
            public string Type { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
        }

        #endregion

        #region IAnalysable

        private volatile int _requestCount;

        IList<IStatisticInformation> IAnalysable.AvailableStatistics
        {
            get
            {
                if (_configuration.AnalyticsEnabled)
                {
                    return new List<IStatisticInformation>
                    {
                        new StatisticInformation
                        {
                            Id = "RequestCount",
                            Name = "Total requests",
                            Description =
                                "The total number of requests processed by the visualazer midldeware since the application was restarted",
                        }
                    };
                }
                return new List<IStatisticInformation>();
            }
        }

        IStatistic IAnalysable.GetStatistic(string id)
        {
            if (_configuration.AnalyticsEnabled)
            {
                switch (id)
                {
                    case "RequestCount":
                        return new IntStatistic(() => _requestCount).Refresh();
                }
            }
            return null;
        }

        #endregion
    }
}
