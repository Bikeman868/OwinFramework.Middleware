﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Owin;
using OwinFramework.Builder;
using OwinFramework.Interfaces.Builder;
using OwinFramework.Interfaces.Routing;
using OwinFramework.InterfacesV1.Capability;
using OwinFramework.InterfacesV1.Middleware;
using System.Web;
using OwinFramework.MiddlewareHelpers.Traceable;
using OwinFramework.Routing;

namespace OwinFramework.ExceptionReporter
{
    public class ExceptionReporterMiddleware:
        IMiddleware<object>, 
        IConfigurable, 
        ISelfDocumenting,
        IRoutingProcessor,
        ITraceable
    {
        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        public IList<IDependency> Dependencies { get { return _dependencies; } }

        public string Name { get; set; }
        public Action<IOwinContext, Func<string>> Trace { get; set; }
        private readonly TraceFilter _traceFilter;

        public ExceptionReporterMiddleware()
        {
            _traceFilter = new TraceFilter(null, this);
        }

        Task IMiddleware.Invoke(IOwinContext context, Func<Task> next)
        {
            return next();
        }

        Task IRoutingProcessor.RouteRequest(IOwinContext context, Func<Task> next)
        {
            try
            {
                return next();
            }
            catch (HttpException httpException)
            {
                return HandleHttpException(context, httpException);
            }
            catch (RoutingException routingException)
            {
                var innerException = routingException.InnerException;
                while (innerException != null)
                {
                    var httpException = innerException as HttpException;
                    if (httpException != null)
                        return HandleHttpException(context, httpException);

                    routingException = innerException as RoutingException;
                    if (routingException == null)
                        return HandleException(context, innerException);

                    innerException = innerException.InnerException;
                }
                return HandleException(context, routingException);
            }
            catch (Exception ex)
            {
                return HandleException(context, ex);
            }
        }

        private Task HandleException(IOwinContext context, Exception ex)
        {
            _traceFilter.Trace(context, TraceLevel.Error, () => GetType().Name + " Exception caught: " + ex.Message);
            var isPrivate = false;
            try
            {
                context.Response.StatusCode = 500;
                context.Response.ReasonPhrase = "Server Error";

                try
                {
                    SendEmail(context, ex);
                }
                catch (Exception mailException)
                {
                    _traceFilter.Trace(context, TraceLevel.Error, () => GetType().Name + " failed to send email: " + mailException.Message);
                    System.Diagnostics.Trace.WriteLine(GetType().Name + " failed to send email: " + mailException.Message);
                }

                isPrivate = IsPrivate(context);
                if (isPrivate) return PrivateResponse(context, ex);

                return PublicResponse(context);
            }
            catch (Exception ex2)
            {
                string message;
                if (isPrivate)
                {
                    message = 
                        "An exception occurred and a report was being generated, but then a further exception " +
                        "was thrown during the generation of that report. \nThe original exception that was throws was " +
                        ex.Message + ". During report generation the exception thrown was " + ex2.Message + ".";
                }
                else
                {
                    message =
                        "An exception occurred and a report was being generated, but then a further exception " +
                        "was thrown during the generation of that report. Please contact support and provide " +
                        "details of what you were doing at the time so that we can resolve this problem as soon " +
                        "as possible. Thank you.";
                }
                if (message.Length < 514) message += new string(' ', 514 - message.Length);

                context.Response.ContentType = "text/plain";
                context.Response.StatusCode = (int) HttpStatusCode.InternalServerError;
                return context.Response.WriteAsync(message);
            }
        }

        private Task HandleHttpException(IOwinContext context, HttpException httpException)
        {
            context.Response.StatusCode = httpException.GetHttpCode();
            context.Response.ReasonPhrase = httpException.GetHtmlErrorMessage();
            _traceFilter.Trace(context, TraceLevel.Error, () => GetType().Name + " HttpException caught with status code " + context.Response.StatusCode);
            return Task.Factory.StartNew(() => { });
        }

        private bool IsPrivate(IOwinContext context)
        {
            if (_configuration.Localhost)
            {
                if (string.Equals(context.Request.Uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (context.Request.RemoteIpAddress == "127.0.0.1")
                    return true;
                if (context.Request.RemoteIpAddress == "::1")
                    return true;
            }

            var permission = _configuration.RequiredPermission;
            if(!string.IsNullOrEmpty(permission))
            {
                var authorization = context.GetFeature<IAuthorization>();
                if (authorization != null && authorization.HasPermission(permission, null))
                    return true;
            }
            return false;
        }

        private string MapPath(string relativePath)
        {
            var applicationBase = String.IsNullOrEmpty(AppDomain.CurrentDomain.RelativeSearchPath)
                ? AppDomain.CurrentDomain.BaseDirectory
                : AppDomain.CurrentDomain.RelativeSearchPath;
            return Path.GetFullPath(Path.Combine(applicationBase, relativePath));
        }

        private Task PublicResponse(IOwinContext context)
        {
            var pageTemplate = GetScriptResource("publicTemplate.html");

            var customTemplate = _configuration.Template; // Note: can change in background thread
            if (!string.IsNullOrEmpty(customTemplate))
            {
                var fullFileName = MapPath(customTemplate);
                var fileInfo = new FileInfo(fullFileName);
                if (fileInfo.Exists)
                {
                    using (var fileStream = fileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        var reader = new StreamReader(fileStream, true);
                        pageTemplate = reader.ReadToEnd();
                    }
                }
            }

            context.Response.ContentType = "text/html";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            return context.Response.WriteAsync(pageTemplate.Replace("{message}", _configuration.Message));
        }

        private Task PrivateResponse(IOwinContext context, Exception ex)
        {
            var pageTemplate = GetScriptResource("privateTemplate.html");
            var requestTemplate = GetScriptResource("requestTemplate.html");
            var exceptionTemplate = GetScriptResource("exceptionTemplate.html");
            var headerTemplate = GetScriptResource("headerTemplate.html");

            var requestContent = new StringBuilder();
            var request = context.Request;
            var requestHtml = requestTemplate
                .Replace("{url}", request.Uri.ToString())
                .Replace("{method}", request.Method)
                .Replace("{scheme}", request.Scheme)
                .Replace("{host}", request.Host.ToString())
                .Replace("{path}", request.Path.ToString())
                .Replace("{queryString}", request.QueryString.ToString())
                .Replace("{ipaddress}", request.RemoteIpAddress)
                .Replace("{port}", request.RemotePort.ToString());

            var headerContent = new StringBuilder();
            foreach (var key in request.Headers.Keys)
            {
                var headerHtml = headerTemplate
                    .Replace("{key}", key)
                    .Replace("{value}", request.Headers[key]);
                headerContent.Append(headerHtml);
            }
            requestHtml = requestHtml.Replace("{headers}", headerContent.ToString());

            requestContent.Append(requestHtml);

            var exceptionsContent = new StringBuilder();

            Action<Exception> addException = e =>
            {
                var exceptionHtml = exceptionTemplate.Replace("{message}", e.Message);

                var typeLoadException = e as TypeLoadException;
                if (typeLoadException != null)
                {
                    exceptionHtml += "<p>Type name: " + typeLoadException.TypeName + "</p>";
                }

                var argumentNullException = e as ArgumentNullException;
                if (argumentNullException != null)
                {
                    exceptionHtml += "<p>Parameter name: " + argumentNullException.ParamName + "</p>";
                }

                if (ex.StackTrace == null)
                    exceptionHtml = exceptionHtml.Replace("{stackTrace}", "[no stack trace available]");
                else
                {
                    var stackTraceLines = e.StackTrace
                        .Split('\n')
                        .Where(l => !string.IsNullOrEmpty(l))
                        .Where(l => l.IndexOf(typeof (Routing.Router).FullName, StringComparison.OrdinalIgnoreCase) < 0)
                        .Where(l => l.IndexOf(typeof (Builder.Builder).FullName, StringComparison.OrdinalIgnoreCase) < 0);
                    exceptionHtml = exceptionHtml.Replace("{stackTrace}", string.Join("<br/>", stackTraceLines));
                }

                exceptionsContent.Append(exceptionHtml);
            };

            while (ex != null)
            {
                var aggregateException = ex as AggregateException;

                if (aggregateException != null)
                {
                    foreach (var inner in aggregateException.InnerExceptions)
                    {
                        addException(inner);
                    }
                }
                else addException(ex);
                ex = ex.InnerException;
            }

            var pageHtml = DocumentConfiguration(pageTemplate)
                .Replace("{request}", requestContent.ToString())
                .Replace("{exceptions}", exceptionsContent.ToString());
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            return context.Response.WriteAsync(pageHtml);
        }

        private void SendEmail(IOwinContext context, Exception ex)
        {
            var email = _configuration.EmailAddress;
            var subject = _configuration.EmailSubject;
            if (string.IsNullOrEmpty(email)) return;

            if (string.IsNullOrEmpty(subject)) subject = "Exception in " + context.Request.Host;

            var message = new StringBuilder();
            message.AppendLine("The Owin exception reporter caught an exception in the following request:");
            message.AppendLine("Url: " + context.Request.Uri);
            message.AppendLine("IP: " + context.Request.RemoteIpAddress);
            message.AppendLine("Headers:");
            foreach (var header in context.Request.Headers)
                message.AppendLine("   " + header.Key + " = " + header.Value);
            while (ex != null)
            {
                message.AppendLine();
                message.AppendLine(ex.Message);
                message.AppendLine(ex.StackTrace);
                ex = ex.InnerException;
            }

            var mailMessage = new MailMessage(email, email, subject, message.ToString());
            var smtp = new SmtpClient();
            smtp.SendAsync(mailMessage, null);
        }

        #region IConfigurable

        private IDisposable _configurationRegistration;
        private ExceptionReporterConfiguration _configuration = new ExceptionReporterConfiguration();

        void IConfigurable.Configure(IConfiguration configuration, string path)
        {
            _traceFilter.ConfigureWith(configuration);

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
            pageTemplate = pageTemplate.Replace("{message.default}", defaultConfiguration.Message ?? "<default>")
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
