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

namespace OwinFramework.Less
{
    public class LessMiddleware:
        IMiddleware<object>, 
        InterfacesV1.Capability.IConfigurable,
        InterfacesV1.Capability.ISelfDocumenting,
        IRoutingProcessor
    {
        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        IList<IDependency> IMiddleware.Dependencies { get { return _dependencies; } }

        string IMiddleware.Name { get; set; }

        private readonly string _contextKey;

        public LessMiddleware()
        {
            _contextKey = Guid.NewGuid().ToShortString(false);
            this.RunAfter<InterfacesV1.Middleware.IOutputCache>(null, false);
        }

        Task IMiddleware.Invoke(IOwinContext context, Func<Task> next)
        {
            if (!string.IsNullOrEmpty(_configuration.DocumentationRootUrl) &&
                context.Request.Path.Value.Equals(_configuration.DocumentationRootUrl, StringComparison.OrdinalIgnoreCase))
            {
                return DocumentConfiguration(context);
            }

            var cssFileContext = context.Get<CssFileContext>(_contextKey);
            if (cssFileContext == null || cssFileContext.PhysicalFile == null)
                return next();

            var outputCache = context.GetFeature<InterfacesV1.Middleware.IOutputCache>();
            if (outputCache != null)
            {
                outputCache.Priority = cssFileContext.NeedsCompiling
                    ? InterfacesV1.Middleware.CachePriority.High 
                    : InterfacesV1.Middleware.CachePriority.Medium;
            }

            return Task.Factory.StartNew(() =>
                {
                    string fileContent;
                    using (var streamReader = cssFileContext.PhysicalFile.OpenText())
                    {
                        fileContent = streamReader.ReadToEnd();
                    }

                    context.Response.ContentType = "text/css";
                    if (cssFileContext.NeedsCompiling)
                    {
                        var css = dotless.Core.Less.Parse(fileContent);
                        context.Response.Write(css);
                    }
                    else
                    {
                        context.Response.Write(fileContent);
                    }
                });
        }

        #region IConfigurable

        private IDisposable _configurationRegistration;
        private LessConfiguration _configuration = new LessConfiguration();

        private string _rootFolder; // Fully qualified path ending with \
        private string _rootUrl; // Starts and end with /

        public void Configure(IConfiguration configuration, string path)
        {
            _configurationRegistration = configuration.Register(
                path, 
                cfg => 
                    {
                        _configuration = cfg;

                        var rootFolder = cfg.RootDirectory ?? "";
                        rootFolder = rootFolder.Replace("/", "\\");

                        if (rootFolder.Length > 0 && !rootFolder.EndsWith("\\")) 
                            rootFolder = rootFolder + "\\";

                        if (rootFolder.StartsWith("~\\")) 
                            rootFolder = rootFolder.Substring(2);

                        if (Path.IsPathRooted(rootFolder))
                            _rootFolder = rootFolder;
                        else
                            _rootFolder = Path.Combine(AppDomain.CurrentDomain.SetupInformation.ApplicationBase, rootFolder);

                        var rootUrl = cfg.RootUrl ?? "";
                        rootUrl = rootUrl.Replace("\\", "/");

                        if (!rootUrl.StartsWith("/")) 
                            rootUrl = "/" + rootUrl;

                        if (rootUrl.Length > 0 && !rootUrl.EndsWith("/")) 
                            rootUrl = rootUrl + "/";

                        _rootUrl = rootUrl;
                    }, 
                    new LessConfiguration());
        }


        #endregion

        #region ISelfDocumenting

        private Task DocumentConfiguration(IOwinContext context)
        {
            var document = GetScriptResource("configuration.html");
            document = document.Replace("{rootUrl}", _configuration.RootUrl);
            document = document.Replace("{documentationRootUrl}", _configuration.DocumentationRootUrl);
            document = document.Replace("{rootDirectory}", _configuration.RootDirectory);
            document = document.Replace("{enabled}", _configuration.Enabled.ToString());

            var defaultConfiguration = new LessConfiguration();
            document = document.Replace("{rootUrl.default}", defaultConfiguration.RootUrl);
            document = document.Replace("{documentationRootUrl.default}", defaultConfiguration.DocumentationRootUrl);
            document = document.Replace("{rootDirectory.default}", defaultConfiguration.RootDirectory);
            document = document.Replace("{enabled.default}", defaultConfiguration.Enabled.ToString());

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
            get { return "Serves CSS files by compiling LESS files into CSS using the dotless NuGet package."; }
        }

        public string ShortDescription
        {
            get { return "Serves CSS files by compiling LESS files into CSS."; }
        }

        public IList<InterfacesV1.Capability.IEndpointDocumentation> Endpoints { get { return null; } }

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

        #region IRoutingProcessor

        Task IRoutingProcessor.RouteRequest(IOwinContext context, Func<Task> next)
        {
            CssFileContext cssFileContext;
            if (!ShouldServeThisFile(context, out cssFileContext))
                return next();

            context.Set(_contextKey, cssFileContext);

            var outputCache = context.GetFeature<InterfacesV1.Upstream.IUpstreamOutputCache>();
            if (outputCache != null && outputCache.CachedContentIsAvailable)
            {
                if (outputCache.TimeInCache.HasValue)
                {
                    var timeSinceFileChanged = DateTime.UtcNow - cssFileContext.PhysicalFile.LastWriteTimeUtc;
                    outputCache.UseCachedContent = outputCache.TimeInCache.Value > timeSinceFileChanged;
                }
            }

            return null;
        }

        private bool ShouldServeThisFile(
            IOwinContext context,
            out CssFileContext cssFileContext)
        {
            cssFileContext = new CssFileContext();

            if (!_configuration.Enabled || !context.Request.Path.HasValue)
                return false;

            var path = context.Request.Path.Value;
            if (!path.StartsWith(_rootUrl, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                return false;

            var cssFileName = Path.Combine(_rootFolder, path.Substring(_rootUrl.Length).Replace('/', '\\'));
            var lessFileName = cssFileName.Substring(0, cssFileName.Length - 4) + ".less";

            cssFileContext.PhysicalFile = new FileInfo(cssFileName);
            if (!cssFileContext.PhysicalFile.Exists)
            {
                cssFileContext.PhysicalFile = new FileInfo(lessFileName);
                cssFileContext.NeedsCompiling = true;
            }

            return cssFileContext.PhysicalFile.Exists;
        }

        #endregion

        #region CssFileContext

        private class CssFileContext
        {
            public FileInfo PhysicalFile;
            public bool NeedsCompiling;
        }

        #endregion
    }
}