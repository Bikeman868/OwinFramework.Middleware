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

namespace OwinFramework.Middleware
{
    public class ExceptionReporter:
        IMiddleware<object>, 
        IConfigurable, 
        ISelfDocumenting
    {
        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        public IList<IDependency> Dependencies { get { return _dependencies; } }

        public string Name { get; set; }

        public ExceptionReporter()
        {
            this.RunFirst();
        }

        public Task Invoke(IOwinContext context, Func<Task> next)
        {
            try
            {
                return next();
            }
            catch (Exception ex)
            {
                try
                {
                    SendEmail(context, ex);
                    return IsPrivate(context) ? PrivateResponse(context, ex) : PublicResponse(context);
                }
                catch
                {
                    context.Response.ContentType = "text/plain";
                    return context.Response.WriteAsync(
                        "An exception occurred and a report was being generated, but then a further exception "+
                        "was thrown during the generation of that report. Please contact technical support so that "+
                        "we can resolve this issue as soon as possible. Thank you.");
                }
            }
        }

        private bool IsPrivate(IOwinContext context)
        {
            return true;
        }

        private Task PublicResponse(IOwinContext context)
        {
            var pageTemplate = GetScriptResource("publicTemplate.html");

            var customTemplate = _configuration.Template; // Note: can change in background thread
            if (!string.IsNullOrEmpty(customTemplate))
            {
                // TODO: Load template from file
            }

            context.Response.ContentType = "text/html";
            return context.Response.WriteAsync(pageTemplate.Replace("{message}", _configuration.Message));
        }

        private Task PrivateResponse(IOwinContext context, Exception ex)
        {
            var pageTemplate = GetScriptResource("privateTemplate.html");
            var requestTemplate = GetScriptResource("requestTemplate.html");
            var exceptionTemplate = GetScriptResource("exceptionTemplate.html");

            var requestContent = new StringBuilder();

            var exceptionsContent = new StringBuilder();
            while (ex != null)
            {
                var exceptionHtml = exceptionTemplate
                    .Replace("{message}", ex.Message)
                    .Replace("{stackTrace}", ex.StackTrace);
                exceptionsContent.Append(exceptionHtml);

                ex = ex.InnerException;
            }

            var pageHtml = DocumentConfiguration(pageTemplate)
                .Replace("{request}", requestContent.ToString())
                .Replace("{exceptions}", exceptionsContent.ToString());
            return context.Response.WriteAsync(pageHtml);
        }

        private void SendEmail(IOwinContext context, Exception ex)
        {
            // TODO: format and send email to ops
        }

        #region IConfigurable

        private IDisposable _configurationRegistration;
        private ExceptionReporterConfiguration _configuration = new ExceptionReporterConfiguration();

        void IConfigurable.Configure(IConfiguration configuration, string path)
        {
            _configurationRegistration = configuration.Register(
                path, cfg => _configuration = cfg, new ExceptionReporterConfiguration());
        }


        #endregion

        #region ISelfDocumenting

        private string DocumentConfiguration(string pageTemplate)
        {
            pageTemplate = pageTemplate
                .Replace("{message}", _configuration.Message ?? "<default>")
                .Replace("{template}", _configuration.Template ?? "<default>")
                .Replace("{localhost}", _configuration.Localhost.ToString())
                .Replace("{requiredPermission}", _configuration.RequiredPermission ?? "<none>")
                .Replace("{email}", _configuration.EmailAddress ?? "<none>")
                .Replace("{subject}", _configuration.EmailSubject ?? "<none>");

            var defaultConfiguration = new ExceptionReporterConfiguration();
            pageTemplate.Replace("{message.default}", defaultConfiguration.Message ?? "<default>")
                .Replace("{template.default}", defaultConfiguration.Template ?? "<default>")
                .Replace("{localhost.default}", defaultConfiguration.Localhost.ToString())
                .Replace("{requiredPermission.default}", defaultConfiguration.RequiredPermission ?? "<none>")
                .Replace("{email}", defaultConfiguration.EmailAddress ?? "<none>")
                .Replace("{subject}", defaultConfiguration.EmailSubject ?? "<none>");

            return pageTemplate;
        }

        Uri ISelfDocumenting.GetDocumentation(DocumentationTypes documentationType)
        {
            switch (documentationType)
            {
                case DocumentationTypes.Overview:
                    return new Uri("https://github.com/Bikeman868/OwinFramework.Middleware", UriKind.Absolute);
                case DocumentationTypes.SourceCode:
                    return new Uri("https://github.com/Bikeman868/OwinFramework.Middleware", UriKind.Absolute);
            }
            return null;
        }

        string ISelfDocumenting.LongDescription
        {
            get { return 
                "When middleware throws an unhandled exception during request processing "+
                "this middleware will catch the exception and return either a public apology "+
                "message or detailed technical information. There are multiple ways to define "+
                "who will see the detailed technical information, by default it is only shown "+
                "to localhost clients"; }
        }

        string ISelfDocumenting.ShortDescription
        {
            get { return "Catches all middleware exceptions and returns a templated response"; }
        }

        IList<IEndpointDocumentation> ISelfDocumenting.Endpoints { get { return null; } }

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
