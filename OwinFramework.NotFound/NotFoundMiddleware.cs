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

namespace OwinFramework.NotFound
{
    public class NotFoundMiddleware:
        IMiddleware<object>,
        InterfacesV1.Capability.IConfigurable
    {
        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        IList<IDependency> IMiddleware.Dependencies { get { return _dependencies; } }

        string IMiddleware.Name { get; set; }

        public NotFoundMiddleware()
        {
            ConfigurationChanged(new NotFoundConfiguration());
            this.RunLast();
        }

        #region IConfigurable

        private IDisposable _configurationRegistration;
        private NotFoundConfiguration _configuration;
        private FileInfo _pageTemplateFile;
        private PathString _configPage;

        public Task Invoke(IOwinContext context, Func<Task> next)
        {
            if (context.Request.Path == _configPage)
                return DocumentConfiguration(context);

            string template;
            if (_pageTemplateFile != null && _pageTemplateFile.Exists)
            {
                using (var stream = _pageTemplateFile.Open(FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var reader = new StreamReader(stream, true);
                    template = reader.ReadToEnd();
                }
            }
            else
            {
                template = GetEmbeddedResource("template.html");
            }

            context.Response.StatusCode = 404;
            context.Response.ReasonPhrase = "Not Found";
            return context.Response.WriteAsync(template);
        }

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
                var fullPath = Path.Combine(AppDomain.CurrentDomain.SetupInformation.ApplicationBase, configuration.Template);
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
            document = document.Replace("{documentationUrl}", defaultConfiguration.DocumentationRootUrl);

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
    }
}
