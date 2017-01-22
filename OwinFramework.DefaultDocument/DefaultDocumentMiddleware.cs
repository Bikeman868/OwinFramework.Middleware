﻿using System;
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

namespace OwinFramework.DefaultDocument
{
    public class DefaultDocumentMiddleware:
        IMiddleware<IRequestRewriter>,
        IRoutingProcessor,
        InterfacesV1.Capability.IConfigurable
    {
        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        IList<IDependency> IMiddleware.Dependencies { get { return _dependencies; } }

        string IMiddleware.Name { get; set; }

        public DefaultDocumentMiddleware()
        {
            ConfigurationChanged(new DefaultDocumentConfiguration());
            this.RunFirst();
        }

        public Task RouteRequest(IOwinContext context, Func<Task> next)
        {
            var trace = (TextWriter)context.Environment["host.TraceOutput"];
            if (trace != null) trace.WriteLine(GetType().Name + " RouteRequest() starting " + context.Request.Uri);

            if (!context.Request.Path.HasValue || context.Request.Path.Value == "/" || context.Request.Path.Value == "")
            {
                if (trace != null) trace.WriteLine(GetType().Name + " modifying request path to " + _defaultPage);
                context.Request.Path = _defaultPage;
                context.SetFeature<IRequestRewriter>(new DefaultDocumentContext());
            }

            var result = next();

            if (trace != null) trace.WriteLine(GetType().Name + " RouteRequest() finished");
            return result;
        }

        public Task Invoke(IOwinContext context, Func<Task> next)
        {
            var trace = (TextWriter)context.Environment["host.TraceOutput"];
            if (trace != null) trace.WriteLine(GetType().Name + " Invoke() starting " + context.Request.Uri);

            if (context.Request.Path == _configPage)
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
        private DefaultDocumentConfiguration _configuration;
        private PathString _defaultPage;
        private PathString _configPage;

        public void Configure(IConfiguration configuration, string path)
        {
            _configurationRegistration = configuration.Register(
                path,
                ConfigurationChanged,
                new DefaultDocumentConfiguration());
        }

        private void ConfigurationChanged(DefaultDocumentConfiguration configuration)
        {
            var defaultConfiguration = new DefaultDocumentConfiguration();

            if (string.IsNullOrEmpty(configuration.DefaultPage))
                configuration.DefaultPage = defaultConfiguration.DefaultPage;
            else if (!configuration.DefaultPage.StartsWith("/"))
                configuration.DefaultPage = "/" + configuration.DefaultPage;

            if (string.IsNullOrEmpty(configuration.DocumentationRootUrl))
                configuration.DocumentationRootUrl = null;
            else if (!configuration.DocumentationRootUrl.StartsWith("/"))
                configuration.DocumentationRootUrl = "/" + configuration.DocumentationRootUrl;

            _configuration = configuration;
            _defaultPage = new PathString(configuration.DefaultPage);
            _configPage = configuration.DocumentationRootUrl == null ? PathString.Empty : new PathString(configuration.DocumentationRootUrl);
        }

        #endregion

        #region ISelfDocumenting

        private Task DocumentConfiguration(IOwinContext context)
        {
            var document = GetEmbeddedResource("configuration.html");
            document = document.Replace("{defaultPage}", _configuration.DefaultPage);
            document = document.Replace("{configUrl}", _configuration.DocumentationRootUrl);

            var defaultConfiguration = new DefaultDocumentConfiguration();
            document = document.Replace("{defaultPage.default}", defaultConfiguration.DefaultPage);
            document = document.Replace("{configUrl.default}", defaultConfiguration.DocumentationRootUrl);

            context.Response.ContentType = "text/html";
            return context.Response.WriteAsync(document);
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
        }
    }
}
