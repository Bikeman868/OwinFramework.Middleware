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
using OwinFramework.InterfacesV1.Capability;
using OwinFramework.InterfacesV1.Middleware;
using OwinFramework.InterfacesV1.Upstream;
using OwinFramework.MiddlewareHelpers.Analysable;
using OwinFramework.NotFound;

namespace OwinFramework.FormIdentification
{
    public class FormIdentificationMiddleware:
        IMiddleware<IResponseProducer>,
        IUpstreamCommunicator<IUpstreamSession>,
        IConfigurable,
        ISelfDocumenting,
        IAnalysable
    {
        private const string _anonymousUserIdentity = "urn:form.identity.anonymous:";

        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        IList<IDependency> IMiddleware.Dependencies { get { return _dependencies; } }

        string IMiddleware.Name { get; set; }

        private int _signupSuccessCount;
        private int _signupFailCount;
        private int _signinSuccessCount;
        private int _signinFailCount;
        private int _signoutCount;
        private int _renewSessionCount;
        private int _clearSessionCount;

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
        private PathString _clearSessionPage;

        public FormIdentificationMiddleware()
        {
            ConfigurationChanged(new FormIdentificationConfiguration());
            this.RunAfter<ISession>();
        }

        public Task RouteRequest(IOwinContext context, Func<Task> next)
        {
            IdentifyUser(context);
            return next();
        }

        public Task Invoke(IOwinContext context, Func<Task> next)
        {
            if (context.Request.Method == "POST")
            {
                if (context.Request.Path == _signinPage)
                    return HandleSignin(context);
                if (context.Request.Path == _signoutPage)
                    return HandleSignout(context);
                if (context.Request.Path == _sendPasswordResetPage)
                    return HandleSendPasswordReset(context);
                if (context.Request.Path == _resetPasswordPage)
                    return HandleResetPassword(context);
                if (context.Request.Path == _signupPage)
                    return HandleSignup(context);
            }
            else if (context.Request.Method == "GET")
            {
                if (context.Request.Path == _renewSessionPage)
                    return HandleRenewSession(context);
                if (context.Request.Path == _clearSessionPage)
                    return HandleClearSession(context);
                if (context.Request.Path == _configPage)
                    return DocumentConfiguration(context);
            }

            var upstreamIdentification = context.GetFeature<IUpstreamIdentification>();
            var identification = context.GetFeature<IIdentification>();

            if (identification != null && identification.IsAnonymous && string.IsNullOrEmpty(identification.Identity))
            {
                return RenewSession(context, upstreamIdentification);
            }

            return next();
        }

        #region User identification

        private void IdentifyUser(IOwinContext context)
        {
            // If identification middleware further up the pipeline already 
            // identified the user then do nothing here
            var identification = context.GetFeature<IIdentification>();
            if (identification != null && !string.IsNullOrEmpty(identification.Identity))
                return;

            var upstreamSession = context.GetFeature<IUpstreamSession>();
            if (upstreamSession == null)
                throw new Exception("Session middleware is required for Form Identification to work");

            if (!upstreamSession.EstablishSession())
                return;

            var session = context.GetFeature<ISession>();
            if (session == null)
                throw new Exception("Session middleware is required for Form Identification to work");

            var identity = session.Get<string>(_sessionName);
            context.SetFeature<IIdentification>(
                new Idenfitication
                {
                    IsAnonymous = string.IsNullOrEmpty(identity) || string.Equals(identity, _anonymousUserIdentity),
                    Identity = identity ?? string.Empty
                });

            // Provide a mechanism for downstream middleware to indicate if 
            // annonymous access is allowed for this page
            if (context.GetFeature<IUpstreamIdentification>() == null)
                context.SetFeature<IUpstreamIdentification>(new UpstreamIdentification { AllowAnonymous = true });
        }

        private Task RenewSession(IOwinContext context, IUpstreamIdentification upstreamIdentification)
        {
            var session = context.GetFeature<ISession>();
            if (session == null)
                throw new Exception("Session middleware is required for Form Identification to work");

            var nonSecurePrefix = string.IsNullOrEmpty(_secureDomain)
                ? string.Empty
                : context.Request.Scheme + "://" + context.Request.Host;

            var securePrefix = string.IsNullOrEmpty(_secureDomain)
                ? string.Empty
                : "https://" + _secureDomain;

            var thisUrl = nonSecurePrefix + context.Request.Path + "?" + context.Request.QueryString;
            var signinUrl = _signinPage.HasValue ? nonSecurePrefix + _signinPage.Value : thisUrl;
            var renewSessionUrl = securePrefix + _renewSessionPage.Value;

            renewSessionUrl += "?sid=" + "session_id"; // TODO: Modify ISession to include session ID
            renewSessionUrl += "&success=" + thisUrl;

            if (upstreamIdentification.AllowAnonymous)
                renewSessionUrl += "&fail=" + thisUrl;
            else
                renewSessionUrl += "&fail=" + signinUrl;

            context.Response.Redirect(renewSessionUrl);
            return context.Response.WriteAsync(string.Empty);
        }

        private class Idenfitication: IIdentification
        {
            public string Identity { get; set; }
            public bool IsAnonymous { get; set; }
        }

        private class UpstreamIdentification : IUpstreamIdentification
        {
            public bool AllowAnonymous { get; set; }
        }

        #endregion

        #region Account functions

        private Task HandleSignup(IOwinContext context)
        {
            throw new NotImplementedException();
        }

        private Task HandleSignin(IOwinContext context)
        {
            throw new NotImplementedException();
        }

        private Task HandleSignout(IOwinContext context)
        {
            throw new NotImplementedException();
        }

        private Task HandleSendPasswordReset(IOwinContext context)
        {
            throw new NotImplementedException();
        }

        private Task HandleResetPassword(IOwinContext context)
        {
            throw new NotImplementedException();
        }

        private Task HandleRenewSession(IOwinContext context)
        {
            var upstreamSession = context.GetFeature<IUpstreamSession>();
            if (upstreamSession == null)
                throw new Exception("Session middleware is required for Form Identification to work");

            upstreamSession.EstablishSession(/* TODO: establish session from session ID */);
            throw new NotImplementedException();
        }

        private Task HandleClearSession(IOwinContext context)
        {
            var upstreamSession = context.GetFeature<IUpstreamSession>();
            if (upstreamSession == null)
                throw new Exception("Session middleware is required for Form Identification to work");

            upstreamSession.EstablishSession(/* TODO: establish session from session ID */);
            throw new NotImplementedException();
        }

        #endregion

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
            _signinPage = cleanUrl(configuration.SigninPage);
            _signoutPage = cleanUrl(configuration.SignoutPage);
            _signupSuccessPage = cleanUrl(configuration.SigninSuccessPage);
            _signupFailPage = cleanUrl(configuration.SignupFailPage);
            _signinSuccessPage = cleanUrl(configuration.SigninSuccessPage);
            _signinFailPage = cleanUrl(configuration.SigninFailPage);
            _signoutSuccessPage = cleanUrl(configuration.SignoutSuccessPage);
            _sendPasswordResetPage = cleanUrl(configuration.SendPasswordResetPage);
            _resetPasswordPage = cleanUrl(configuration.ResetPasswordPage);
            _renewSessionPage = cleanUrl(configuration.RenewSessionPage);
            _clearSessionPage = cleanUrl(configuration.ClearSessionPage);

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
            document = document.Replace("{signinPage}", _configuration.SigninPage);
            document = document.Replace("{signoutPage}", _configuration.SignoutPage);
            document = document.Replace("{signupSuccessPage}", _configuration.SignupSuccessPage);
            document = document.Replace("{signupFailPage}", _configuration.SignupFailPage);
            document = document.Replace("{signinSuccessPage}", _configuration.SigninSuccessPage);
            document = document.Replace("{signinFailPage}", _configuration.SigninFailPage);
            document = document.Replace("{signoutSuccessPage}", _configuration.SignoutSuccessPage);
            document = document.Replace("{sendPasswordResetPage}", _configuration.SendPasswordResetPage);
            document = document.Replace("{resetPasswordPage}", _configuration.ResetPasswordPage);
            document = document.Replace("{renewSessionPage}", _configuration.RenewSessionPage);
            document = document.Replace("{clearSessionPage}", _configuration.ClearSessionPage);
            document = document.Replace("{secureDomain}", _configuration.SecureDomain);
            document = document.Replace("{cookieName}", _configuration.CookieName);
            document = document.Replace("{sessionName}", _configuration.SessionName);
            document = document.Replace("{rememberMeFor}", _configuration.RememberMeFor.ToString());

            var defaultConfiguration = new FormIdentificationConfiguration();
            document = document.Replace("{documentationPage.default}", defaultConfiguration.DocumentationPage);
            document = document.Replace("{signupPage.default}", defaultConfiguration.SignupPage);
            document = document.Replace("{signinPage.default}", defaultConfiguration.SigninPage);
            document = document.Replace("{signoutPage.default}", defaultConfiguration.SignoutPage);
            document = document.Replace("{signupSuccessPage.default}", defaultConfiguration.SignupSuccessPage);
            document = document.Replace("{signupFailPage.default}", defaultConfiguration.SignupFailPage);
            document = document.Replace("{signinSuccessPage.default}", defaultConfiguration.SigninSuccessPage);
            document = document.Replace("{signinFailPage.default}", defaultConfiguration.SigninFailPage);
            document = document.Replace("{signoutSuccessPage.default}", defaultConfiguration.SignoutSuccessPage);
            document = document.Replace("{sendPasswordResetPage.default}", defaultConfiguration.SendPasswordResetPage);
            document = document.Replace("{resetPasswordPage.default}", defaultConfiguration.ResetPasswordPage);
            document = document.Replace("{renewSessionPage.default}", defaultConfiguration.RenewSessionPage);
            document = document.Replace("{clearSessionPage.default}", defaultConfiguration.ClearSessionPage);
            document = document.Replace("{secureDomain.default}", defaultConfiguration.SecureDomain);
            document = document.Replace("{cookieName.default}", defaultConfiguration.CookieName);
            document = document.Replace("{sessionName.default}", defaultConfiguration.SessionName);
            document = document.Replace("{rememberMeFor.default}", defaultConfiguration.RememberMeFor.ToString());

            document = document.Replace("{longDescription}", LongDescription);

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
                    return new Uri("https://github.com/Bikeman868/OwinFramework.Middleware/tree/master/OwinFramework.FormIdentification", UriKind.Absolute);
            }
            return null;
        }

        public string LongDescription
        {
            get { return GetEmbeddedResource("description.html"); }
        }

        public string ShortDescription
        {
            get { return "Allows users of the site to create accounts, login to those accounts etc."; }
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
                            RelativePath = string.IsNullOrEmpty(_secureDomain) 
                                ? _signupPage.Value 
                                : "https://" + _secureDomain + _signupPage.Value,
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
                            RelativePath = string.IsNullOrEmpty(_secureDomain)
                                ? _signinPage.Value
                                : "https://" + _secureDomain + _signinPage.Value,
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
                            RelativePath = string.IsNullOrEmpty(_secureDomain)
                                ? _signoutPage.Value
                                : "https://" + _secureDomain + _signoutPage.Value,
                            Description = "User logout",
                            Attributes = new List<IEndpointAttributeDocumentation>
                                {
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Method",
                                        Name = "POST",
                                        Description = "Logs out the current user and clears any cookie stored on the browser. Optionally Redirects the user"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Form Variable",
                                        Name = "email",
                                        Description = "The email address of the user who wants to reset their password"
                                    }
                                }
                        });

                if (_sendPasswordResetPage.HasValue)
                    documentation.Add(
                        new EndpointDocumentation
                        {
                            RelativePath = string.IsNullOrEmpty(_secureDomain)
                                ? _sendPasswordResetPage.Value
                                : "https://" + _secureDomain + _sendPasswordResetPage.Value,
                            Description = "Request a password reset email to be sent",
                            Attributes = new List<IEndpointAttributeDocumentation>
                                {
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Method",
                                        Name = "POST",
                                        Description = "Sends the user a password reset email"
                                    }
                                }
                        });

                if (_resetPasswordPage.HasValue)
                    documentation.Add(
                        new EndpointDocumentation
                        {
                            RelativePath = string.IsNullOrEmpty(_secureDomain)
                                ? _resetPasswordPage.Value
                                : "https://" + _secureDomain + _resetPasswordPage.Value,
                            Description = "Resets a user's password. One time use within expiry time",
                            Attributes = new List<IEndpointAttributeDocumentation>
                                {
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Method",
                                        Name = "POST",
                                        Description = "Sends the user a password reset email"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Form Variable",
                                        Name = "email",
                                        Description = "The email address of the user who wants to reset their password"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Form Variable",
                                        Name = "token",
                                        Description = "The password reset token that was included in the password reset email"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Form Variable",
                                        Name = "password",
                                        Description = "The new password to set for this user"
                                    }
                                }
                        });

                if (_renewSessionPage.HasValue)
                    documentation.Add(
                        new EndpointDocumentation
                        {
                            RelativePath = string.IsNullOrEmpty(_secureDomain)
                                ? _renewSessionPage.Value
                                : "https://" + _secureDomain + _renewSessionPage.Value,
                            Description = 
                                "Identifies the user and adds user identification to the users session. "+
                                "If the user is logged on then this is transparent to them. If they are not"+
                                "logged on they will be redirected to the login page if there is one.",
                            Attributes = new List<IEndpointAttributeDocumentation>
                                {
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Method",
                                        Name = "GET",
                                        Description = "Renews the users session by identifying them securely"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Parameter",
                                        Name = "sid",
                                        Description = "(required) The session id of the session to renew"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Parameter",
                                        Name = "success",
                                        Description = 
                                            "The URL to redirect the browser to when the renewal succeeds. "+
                                            "The request will succeed if the user is logged in with remember me "+
                                            "enabled and the remember me maximum time has not been exceeded, or "+
                                            "if the user sucesfully completes an account login"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Parameter",
                                        Name = "fail",
                                        Description =
                                            "The URL to redirect the browser to when the renewal fails." +
                                            "Renewal will fail only if the user is not logged in and failes to " +
                                            "provide valid credentials."
                                    },
                                }
                        });

                if (_clearSessionPage.HasValue)
                    documentation.Add(
                        new EndpointDocumentation
                        {
                            RelativePath = string.IsNullOrEmpty(_secureDomain)
                                ? _clearSessionPage.Value
                                : "https://" + _secureDomain + _clearSessionPage.Value,
                            Description =
                                "Clears the user identification from session and also deletes the " +
                                "user identification cookie on the browser.",
                            Attributes = new List<IEndpointAttributeDocumentation>
                                {
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Method",
                                        Name = "GET",
                                        Description = "Clears user identification information"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Parameter",
                                        Name = "sid",
                                        Description = "(required) The session id of the session to clear"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Parameter",
                                        Name = "success",
                                        Description = "The URL to redirect the browser to after clearing the session"
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
                        Id = "SignupSuccessCount",
                        Name = "Signup success count",
                        Description = "The number of successful account creation requests since startup",
                        Explanation = ""
                    });
                stats.Add(
                    new StatisticInformation
                    {
                        Id = "SignupFailCount",
                        Name = "Signup fail count",
                        Description = "The number of unsuccessful account creation requests since startup",
                        Explanation = ""
                    });
                stats.Add(
                    new StatisticInformation
                    {
                        Id = "SigninSuccessCount",
                        Name = "Signin success count",
                        Description = "The number of successful sign in requests since startup",
                        Explanation = ""
                    });
                stats.Add(
                    new StatisticInformation
                    {
                        Id = "SigninFailCount",
                        Name = "Signin fail count",
                        Description = "The number of unsuccessful sign in requests since startup",
                        Explanation = ""
                    });
                stats.Add(
                    new StatisticInformation
                    {
                        Id = "SignoutCount",
                        Name = "Signout count",
                        Description = "The number of times users explicitly logged out since startup",
                        Explanation = ""
                    });
                stats.Add(
                    new StatisticInformation
                    {
                        Id = "RenewSessionCount",
                        Name = "Renew session count",
                        Description = "The number of times the user identification was updated in a session",
                        Explanation = "These session refreshes happen whenever someone logs in, or gets reauthenticated because their session timed out"
                    });
                return stats;
            }
        }

        public IStatistic GetStatistic(string id)
        {
            switch (id)
            {
                case "SignupSuccessCount": return new IntStatistic(() => _signupSuccessCount);
                case "SignupFailCount": return new IntStatistic(() => _signupFailCount);
                case "SigninSuccessCount": return new IntStatistic(() => _signinSuccessCount);
                case "SigninFailCount": return new IntStatistic(() => _signinFailCount);
                case "SignoutCount": return new IntStatistic(() => _signoutCount);
                case "RenewSessionCount": return new IntStatistic(() => _renewSessionCount);
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
