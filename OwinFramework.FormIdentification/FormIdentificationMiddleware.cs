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
using OwinFramework.NotFound;

namespace OwinFramework.FormIdentification
{
    public class FormIdentificationMiddleware:
        IMiddleware<IResponseProducer>,
        IConfigurable,
        ISelfDocumenting,
        IAnalysable
    {
        private readonly IHostingEnvironment _hostingEnvironment;
        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        IList<IDependency> IMiddleware.Dependencies { get { return _dependencies; } }

        string IMiddleware.Name { get; set; }

        private int _signinSuccessCount;
        private int _signinFailCount;

        private IDisposable _configurationRegistration;
        private FormIdentificationConfiguration _configuration;

        private string _secureDomain;
        private string _cookieName;
        private string _sessionName;

        private PathString _configPage;
        private PathString _signupPage;
        private PathString _signinPage;
        private PathString _signoutPage;
        private PathString _signupSuccessPage;
        private PathString _signupFailPage;
        private PathString _signinSuccessPage;
        private PathString _signinFailPage;
        private PathString _signoutSuccessPage;
        private PathString _sendPasswordResetPage;
        private PathString _resetPasswordPage;
        private PathString _renewSessionPage;

        public FormIdentificationMiddleware(IHostingEnvironment hostingEnvironment)
        {
            _hostingEnvironment = hostingEnvironment;
            ConfigurationChanged(new FormIdentificationConfiguration());
            this.RunAfter<ISession>();
        }

        public Task Invoke(IOwinContext context, Func<Task> next)
        {
            return next();
        }

        #region IConfigurable

        public void Configure(IConfiguration configuration, string path)
        {
            _configurationRegistration = configuration.Register(path, ConfigurationChanged, _configuration);
        }

        private void ConfigurationChanged(FormIdentificationConfiguration configuration)
        {
            Func<string, PathString> cleanUrl = url =>
            {
                if (string.IsNullOrWhiteSpace(url)) url = null;
                else if (!url.StartsWith("/")) url = "/" + url;
                return url == null ? PathString.Empty : new PathString(url);
            };

            _configuration = configuration;

            _configPage = cleanUrl(configuration.DocumentationPage);
            _signupPage = cleanUrl(configuration.SignupPage);
            _signinPage = cleanUrl(configuration.SignupPage);
            _signoutPage = cleanUrl(configuration.SignupPage);
            _signupSuccessPage = cleanUrl(configuration.SignupPage);
            _signupFailPage = cleanUrl(configuration.SignupPage);
            _signinSuccessPage = cleanUrl(configuration.SignupPage);
            _signinFailPage = cleanUrl(configuration.SignupPage);
            _signoutSuccessPage = cleanUrl(configuration.SignupPage);
            _sendPasswordResetPage = cleanUrl(configuration.SignupPage);
            _resetPasswordPage = cleanUrl(configuration.SignupPage);
            _renewSessionPage = cleanUrl(configuration.SignupPage);

            _secureDomain = configuration.SecureDomain;
            _cookieName = configuration.CookieName;
            _sessionName = configuration.SessionName;
        }

        #endregion

        #region ISelfDocumenting

        private Task DocumentConfiguration(IOwinContext context)
        {
            var document = GetEmbeddedResource("configuration.html");
            document = document.Replace("{documentationPage}", _configuration.DocumentationPage);
            document = document.Replace("{signupPage}", _configuration.SignupPage);

            var defaultConfiguration = new FormIdentificationConfiguration();
            document = document.Replace("{documentationPage.default}", defaultConfiguration.DocumentationPage);
            document = document.Replace("{signupPage.default}", defaultConfiguration.SignupPage);

            context.Response.ContentType = "text/html";
            return context.Response.WriteAsync(document);
        }

        public Uri GetDocumentation(DocumentationTypes documentationType)
        {
            switch (documentationType)
            {
                case DocumentationTypes.Configuration:
                    return string.IsNullOrEmpty(_configuration.DocumentationPage) 
                        ? null
                        : new Uri(_configuration.DocumentationPage, UriKind.Relative);
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

                if (_configPage.HasValue)
                    documentation.Add(
                        new EndpointDocumentation
                        {
                            RelativePath = _configPage.Value,
                            Description = "Documentation of the configuration options for the Form Identification middleware",
                            Attributes = new List<IEndpointAttributeDocumentation>
                                {
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Method",
                                        Name = "GET",
                                        Description = "Returns configuration documentation for Form Identification middleware in HTML format"
                                    }
                                }
                        });

                if (_signupPage.HasValue)
                    documentation.Add(
                        new EndpointDocumentation
                        {
                            RelativePath = _signupPage.Value,
                            Description = "User account creation via email and password",
                            Attributes = new List<IEndpointAttributeDocumentation>
                                {
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Method",
                                        Name = "POST",
                                        Description = "Tries to create a new account then redirects the browser to a success or fail page"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Form Variable",
                                        Name = "email",
                                        Description = "The email address of the user wanting to create an account"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Form Variable",
                                        Name = "password",
                                        Description = "The password of the user trying to create an account"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Form Variable",
                                        Name = "rememberMe",
                                        Description = "A boolean flag indicating if a cookie should be stored on the browser to keep the user logged in"
                                    }
                                }
                        });

                if (_signinPage.HasValue)
                    documentation.Add(
                        new EndpointDocumentation
                        {
                            RelativePath = _signinPage.Value,
                            Description = "User login via email and password",
                            Attributes = new List<IEndpointAttributeDocumentation>
                                {
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Method",
                                        Name = "POST",
                                        Description = "Tries to log the user in and then redirects the browser to a success or fail page"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Form Variable",
                                        Name = "email",
                                        Description = "The email address of the user trying to login"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Form Variable",
                                        Name = "password",
                                        Description = "The password of the user trying to login"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Form Variable",
                                        Name = "rememberMe",
                                        Description = "A boolean flag indicating if a cookie should be stored on the browser to keep the user logged in"
                                    }
                                }
                        });

                if (_signoutPage.HasValue)
                    documentation.Add(
                        new EndpointDocumentation
                        {
                            RelativePath = _signinPage.Value,
                            Description = "User logout",
                            Attributes = new List<IEndpointAttributeDocumentation>
                                {
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Method",
                                        Name = "POST",
                                        Description = "Logs out the current user and clears any cookie stored on the browser. Optionally Redirects the user"
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
                        Id = "SigninSuccessCount",
                        Name = "Signin success count",
                        Description = "The number of successful sign in requests since startup",
                        Explanation = ""
                    });
                return stats;
            }
        }

        public IStatistic GetStatistic(string id)
        {
            switch (id)
            {
                case "SigninSuccessCount":
                    return new IntStatistic(() => _signinSuccessCount);
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
