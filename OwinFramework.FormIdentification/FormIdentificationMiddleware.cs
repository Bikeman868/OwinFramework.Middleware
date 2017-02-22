using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Net.Mime;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Owin;
using OwinFramework.Builder;
using OwinFramework.Interfaces.Builder;
using OwinFramework.InterfacesV1.Capability;
using OwinFramework.InterfacesV1.Facilities;
using OwinFramework.InterfacesV1.Middleware;
using OwinFramework.InterfacesV1.Upstream;
using OwinFramework.MiddlewareHelpers.Analysable;

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

        private readonly IIdentityStore _identityStore;
        private readonly ITokenStore _tokenStore;

        private volatile int _signupSuccessCount;
        private volatile int _signupFailCount;
        private volatile int _signinSuccessCount;
        private volatile int _signinFailCount;
        private volatile int _signoutCount;
        private volatile int _renewSessionCount;
        private volatile int _updateRememberMeCount;

        private IDisposable _configurationRegistration;
        private FormIdentificationConfiguration _configuration;

        private string _secureDomain;
        private string _secureProtcol;
        private string _cookieName;
        private string _sessionIdentityName;
        private string _sessionPurposeName;
        private string _sessionStatusName;
        private string _sessionRememberMeName;
        private string _sessionEmailName;

        private PathString _documentationPage;

        private PathString _signupPage;
        private PathString _signupSuccessPage;
        private PathString _signupFailPage;

        private PathString _signinPage;
        private PathString _signinSuccessPage;
        private PathString _signinFailPage;

        private PathString _signoutPage;
        private PathString _signoutSuccessPage;

        private PathString _changePasswordPage;
        private PathString _changePasswordSuccessPage;
        private PathString _changePasswordFailPage;

        private PathString _sendPasswordResetPage;
        private PathString _sendPasswordResetSuccessPage;
        private PathString _sendPasswordResetFailPage;

        private PathString _resetPasswordPage;
        private PathString _resetPasswordSuccessPage;
        private PathString _resetPasswordFailPage;

        private PathString _renewSessionPage;
        private PathString _updateIdentityPage;

        public FormIdentificationMiddleware(
            IIdentityStore identityStore, 
            ITokenStore tokenStore)
        {
            _identityStore = identityStore;
            _tokenStore = tokenStore;

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
                if (context.Request.Path == _changePasswordPage)
                    return HandleChangePassword(context);
            }
            else if (context.Request.Method == "GET")
            {
                if (context.Request.Path == _renewSessionPage)
                    return HandleRenewSession(context);
                if (context.Request.Path == _updateIdentityPage)
                    return HandleRememberMe(context);
                if (context.Request.Path == _documentationPage)
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

            var identity = session.Get<string>(_sessionIdentityName);
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

        private Task RenewSession(
            IOwinContext context, 
            IUpstreamIdentification upstreamIdentification)
        {
            var session = context.GetFeature<ISession>();
            var upstreamSession = context.GetFeature<IUpstreamSession>();
            if (session == null || upstreamSession == null)
                throw new Exception("Session middleware is required for Form Identification to work");

            var nonSecurePrefix = GetNonSecurePrefix(context);
            var securePrefix = GetSecurePrefix();
            var thisUrl = GetThisUrl(context);

            var signinUrl = _signinPage.HasValue ? nonSecurePrefix + _signinPage.Value : thisUrl;
            var renewSessionUrl = securePrefix + _renewSessionPage.Value;

            renewSessionUrl += "?sid=" + Uri.EscapeDataString(upstreamSession.SessionId);
            renewSessionUrl += "&success=" + Uri.EscapeDataString(thisUrl);

            if (upstreamIdentification.AllowAnonymous)
                renewSessionUrl += "&fail=" +  Uri.EscapeDataString(thisUrl);
            else
                renewSessionUrl += "&fail=" + Uri.EscapeDataString(signinUrl);

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

        /// <summary>
        /// This request is handled in the main site domain when the user POSTs
        /// the sign up form.
        /// </summary>
        private Task HandleSignup(IOwinContext context)
        {
            var form = context.Request.ReadFormAsync().Result;

            var email = form[_configuration.EmailFormField];
            var password = form[_configuration.PasswordFormField];
            var rememberMe = !string.IsNullOrEmpty(form[_configuration.RememberMeFormField]);
            var thisUrl = GetThisUrl(context);

            bool success;
            string identity = null;
            try
            {
                identity = _identityStore.CreateIdentity();
                success = _identityStore.AddCredentials(identity, email, password);
            }
            catch
            {
                success = false;
            }

            if (success)
            {
                _signupSuccessCount++;

                var session = context.GetFeature<ISession>();
                var upstreamSession = context.GetFeature<IUpstreamSession>();
                if (session == null || upstreamSession == null)
                    throw new Exception("Session middleware is required for Form Identification to work");

                var authenticationResult = _identityStore.AuthenticateWithCredentials(email, password);

                if (authenticationResult.Status != AuthenticationStatus.Authenticated)
                    throw new Exception("Login failed on newly created account");

                SetSession(
                    session, 
                    identity, 
                    authenticationResult.Purposes, 
                    authenticationResult.Status, 
                    rememberMe ? authenticationResult.RememberMeToken : null,
                    email);

                if (string.IsNullOrEmpty(_secureDomain))
                {
                    UpdateRememberMeCookie(context);
                }
                else
                {
                    var nonSecurePrefix = GetNonSecurePrefix(context);
                    var securePrefix = GetSecurePrefix();

                    var signupSuccessPage = _signupSuccessPage.HasValue ? nonSecurePrefix + _signupSuccessPage.Value : thisUrl;

                    var updateIdentityUrl = securePrefix + _updateIdentityPage.Value;
                    updateIdentityUrl += "?sid=" + Uri.EscapeDataString(upstreamSession.SessionId);
                    updateIdentityUrl += "&success=" + Uri.EscapeDataString(signupSuccessPage);

                    return Redirect(context, updateIdentityUrl);
                }

                return Redirect(context, _signupSuccessPage.HasValue ? _signupSuccessPage.Value : thisUrl);
            }

            _signupFailCount++;

            return Redirect(context, _signupFailPage.HasValue ? _signupFailPage.Value : thisUrl);
        }

        /// <summary>
        /// This request is handled in the main site domain when the user POSTs
        /// the sign in form.
        /// </summary>
        private Task HandleSignin(IOwinContext context)
        {
            var form = context.Request.ReadFormAsync().Result;

            var email = form[_configuration.EmailFormField];
            var password = form[_configuration.PasswordFormField];
            var rememberMe = !string.IsNullOrEmpty(form[_configuration.RememberMeFormField]);
            var thisUrl = GetThisUrl(context);

            bool success;
            IAuthenticationResult authenticationResult = null;
            try
            {
                authenticationResult = _identityStore.AuthenticateWithCredentials(email, password);
                success = authenticationResult.Status == AuthenticationStatus.Authenticated;
            }
            catch
            {
                success = false;
            }

            if (success)
            {
                _signinSuccessCount++;

                var session = context.GetFeature<ISession>();
                var upstreamSession = context.GetFeature<IUpstreamSession>();
                if (session == null || upstreamSession == null)
                    throw new Exception("Session middleware is required for Form Identification to work");

                SetSession(
                    session, 
                    authenticationResult.Identity, 
                    authenticationResult.Purposes,
                    authenticationResult.Status,
                    rememberMe ? authenticationResult.RememberMeToken : null,
                    email);

                if (string.IsNullOrEmpty(_secureDomain))
                {
                    UpdateRememberMeCookie(context);
                }
                else
                {
                    var nonSecurePrefix = GetNonSecurePrefix(context);
                    var securePrefix = GetSecurePrefix();

                    var signinSuccessPage = _signinSuccessPage.HasValue ? nonSecurePrefix + _signinSuccessPage.Value : thisUrl;

                    var updateIdentityUrl = securePrefix + _updateIdentityPage.Value;
                    updateIdentityUrl += "?sid=" + Uri.EscapeDataString(upstreamSession.SessionId);
                    updateIdentityUrl += "&success=" + Uri.EscapeDataString(signinSuccessPage);

                    return Redirect(context, updateIdentityUrl);
                }

                return Redirect(context, _signinSuccessPage.HasValue ? _signinSuccessPage.Value : thisUrl);
            }

            _signinFailCount++;

            return Redirect(context, _signinFailPage.HasValue ? _signinFailPage.Value : thisUrl);
        }

        /// <summary>
        /// This request is handled in the main site domain when the user POSTs
        /// the sign out form.
        /// </summary>
        private Task HandleSignout(IOwinContext context)
        {
            var upstreamSession = context.GetFeature<IUpstreamSession>();
            var session = context.GetFeature<ISession>();
            if (upstreamSession == null || session == null)
                throw new Exception("Session middleware is required for Form Identification to work");

            _signoutCount++;

            SetSession(session, null, null, AuthenticationStatus.NotFound, null, null);

            if (string.IsNullOrEmpty(_secureDomain))
            {
                UpdateRememberMeCookie(context);
                return context.Response.WriteAsync(string.Empty);
            }

            var nonSecurePrefix = GetNonSecurePrefix(context);
            var securePrefix = GetSecurePrefix();
            var thisUrl = GetThisUrl(context);

            var successUrl = _signoutSuccessPage.HasValue ? nonSecurePrefix + _signoutSuccessPage.Value : thisUrl;

            var updateIdentityUrl = securePrefix + _updateIdentityPage.Value;
            updateIdentityUrl += "?sid=" + Uri.EscapeDataString(upstreamSession.SessionId);
            updateIdentityUrl += "&success=" + Uri.EscapeDataString(successUrl);

            return Redirect(context, updateIdentityUrl);
        }

        /// <summary>
        /// This request is handled in the main site domain when the user POSTs
        /// the change password form.
        /// </summary>
        private Task HandleChangePassword(IOwinContext context)
        {
            var form = context.Request.ReadFormAsync().Result;

            var email = form[_configuration.EmailFormField];
            var password = form[_configuration.PasswordFormField];
            var newPassword = form[_configuration.NewPasswordFormField];

            var thisUrl = GetThisUrl(context);

            var authenticationResult = _identityStore.AuthenticateWithCredentials(email, password);
            if (authenticationResult.Status != AuthenticationStatus.Authenticated)
                return Redirect(context, _changePasswordFailPage.HasValue ? _changePasswordFailPage.Value : thisUrl);

            var credential = _identityStore.GetRememberMeCredential(authenticationResult.RememberMeToken);
            if (_identityStore.ChangePassword(credential, newPassword))
                return Redirect(context, _changePasswordSuccessPage.HasValue ? _changePasswordSuccessPage.Value : thisUrl);

            return Redirect(context, _changePasswordFailPage.HasValue ? _changePasswordFailPage.Value : thisUrl);
        }

        /// <summary>
        /// This request is handled in the main site domain when the user POSTs
        /// the send password reset form.
        /// </summary>
        private Task HandleSendPasswordReset(IOwinContext context)
        {
            var form = context.Request.ReadFormAsync().Result;
            var email = form[_configuration.EmailFormField];

            var thisUrl = GetThisUrl(context);

            var credential = _identityStore.GetUsernameCredential(email);
            if (credential == null)
                return Redirect(context, _sendPasswordResetFailPage.HasValue ? _sendPasswordResetFailPage.Value : thisUrl);

            var tokenString = _tokenStore.CreateToken(_configuration.PasswordResetTokenType, "ResetPassword", email);

            var emailHtml = GetEmbeddedResource("PasswordResetEmail.html");
            var emailText = GetEmbeddedResource("PasswordResetEmail.txt");

            var pageUrl = GetNonSecurePrefix(context) + _resetPasswordPage;

            emailHtml = emailHtml
                .Replace("{email}", email)
                .Replace("{page}", pageUrl)
                .Replace("{token}", tokenString);

            emailText = emailText
                .Replace("{email}", email)
                .Replace("{page}", pageUrl)
                .Replace("{token}", tokenString);

            var fromEmail = _configuration.PasswordResetEmailFrom;
            if (string.IsNullOrWhiteSpace(fromEmail))
                fromEmail = "password-reset@" + context.Request.Host;

            var mailMessage = new MailMessage(fromEmail, email, _configuration.PasswordResetEmailSubject, emailText);
            mailMessage.Subject = _configuration.PasswordResetEmailSubject;
            mailMessage.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(emailHtml, new ContentType("text/html")));

            var emailClient = new SmtpClient();
            emailClient.Send(mailMessage);

            return Redirect(context, _sendPasswordResetSuccessPage.HasValue ? _sendPasswordResetSuccessPage.Value : thisUrl);
        }

        /// <summary>
        /// This request is handled in the main site domain when the user POSTs
        /// the reset password form.
        /// </summary>
        private Task HandleResetPassword(IOwinContext context)
        {
            var form = context.Request.ReadFormAsync().Result;

            var tokenString = form[_configuration.TokenFormField];
            var email = form[_configuration.EmailFormField];
            var newPassword = form[_configuration.NewPasswordFormField];

            if (string.IsNullOrEmpty(tokenString))
                tokenString = context.Request.Query["token"];

            var thisUrl = GetThisUrl(context);

            var token = _tokenStore.GetToken(_configuration.PasswordResetTokenType, tokenString, "ResetPassword", email);
            if (token.Status == TokenStatus.Allowed)
            {
                var credential =_identityStore.GetUsernameCredential(email);
                if (credential != null)
                {
                    if (_identityStore.ChangePassword(credential, newPassword))
                    {
                        _tokenStore.DeleteToken(tokenString);
                        return Redirect(
                            context,
                            _resetPasswordSuccessPage.HasValue 
                                ? _resetPasswordSuccessPage.Value 
                                : thisUrl);
                    }
                }
            }

            return Redirect(context, _resetPasswordFailPage.HasValue ? _resetPasswordFailPage.Value : thisUrl);
        }

        /// <summary>
        /// This request is handled in the secure sub-domain when the users session expires.
        /// It tries to log the user in again using the remember me cookie.
        /// </summary>
        private Task HandleRenewSession(IOwinContext context)
        {
            var sessionId = context.Request.Query["sid"];
            var successUrl = context.Request.Query["success"];
            var failUrl = context.Request.Query["fail"];

            var upstreamSession = context.GetFeature<IUpstreamSession>();
            if (upstreamSession == null)
                throw new Exception("Session middleware is required for Form Identification to work");

            _renewSessionCount++;

            upstreamSession.EstablishSession(sessionId);
            var session = context.GetFeature<ISession>();

            var rememberMeToken = context.Request.Cookies[_cookieName];
            if (string.IsNullOrEmpty(rememberMeToken))
            {
                SetSession(session, _anonymousUserIdentity, null, AuthenticationStatus.NotFound, null, null);
                return Redirect(context, failUrl);
            }

            var authenticationResult = _identityStore.RememberMe(rememberMeToken);
            SetSession(
                session, 
                authenticationResult.Identity, 
                authenticationResult.Purposes, 
                authenticationResult.Status, 
                authenticationResult.RememberMeToken,
                null);

            return Redirect(
                context, 
                authenticationResult.Status == AuthenticationStatus.Authenticated 
                    ? successUrl : failUrl);
        }

        /// <summary>
        /// This request is handled on the secure sub-domain whenever the user logs in or out.
        /// It is responsible for setting or clearing the cookie thet is stored in the 
        /// secure sub-domain.
        /// </summary>
        private Task HandleRememberMe(IOwinContext context)
        {
            var session = context.GetFeature<ISession>();
            var upstreamSession = context.GetFeature<IUpstreamSession>();
            if (upstreamSession == null || session == null)
                throw new Exception("Session middleware is required for Form Identification to work");

            var sessionId = context.Request.Query["sid"];
            var successUrl = context.Request.Query["success"];

            upstreamSession.EstablishSession(sessionId);
            UpdateRememberMeCookie(context);

            return RedirectWithCookies(context, successUrl);
        }

        private void UpdateRememberMeCookie(IOwinContext context)
        {
            var session = context.GetFeature<ISession>();

            string identity;
            IList<string> purpose;
            AuthenticationStatus status;
            string rememberMeToken;
            string email;
            GetSession(session, out identity, out purpose, out status, out rememberMeToken, out email);

            if (string.IsNullOrEmpty(rememberMeToken))
                context.Response.Cookies.Delete(_cookieName);
            else
                context.Response.Cookies.Append(
                    _cookieName,
                    rememberMeToken,
                    new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = true,
                        Expires = DateTime.UtcNow + _configuration.RememberMeFor
                    });

            _updateRememberMeCount++;
        }

        private Task RedirectWithCookies(IOwinContext context, string url)
        {
            var html = GetEmbeddedResource("redirect.html");
            html = html.Replace("{redirectUrl}", url);
            return context.Response.WriteAsync(html);
        }

        private Task Redirect(IOwinContext context, string url)
        {
            context.Response.Redirect(url);
            return context.Response.WriteAsync(string.Empty);
        }

        private void SetSession(
            ISession session,
            string identity,
            IList<string> purposes,
            AuthenticationStatus status,
            string rememberMeToken,
            string email)
        {
            session.Set(_sessionIdentityName, identity ?? string.Empty);
            session.Set(_sessionPurposeName,
                purposes == null
                ? new List<string>()
                : purposes.ToList());
            session.Set(_sessionStatusName, status);
            session.Set(_sessionRememberMeName, rememberMeToken);
            session.Set(_sessionEmailName, email);
        }

        private void GetSession(
            ISession session,
            out string identity,
            out IList<string> purposes,
            out AuthenticationStatus status,
            out string rememberMeToken,
            out string email)
        {
            identity = session.Get<string>(_sessionIdentityName) ?? string.Empty;
            purposes = session.Get<List<string>>(_sessionPurposeName) ?? new List<string>();
            status = session.Get<AuthenticationStatus>(_sessionStatusName);
            rememberMeToken = session.Get<string>(_sessionRememberMeName);
            email = session.Get<string>(_sessionEmailName);
        }

        private string GetThisUrl(IOwinContext context)
        {
            var thisUrl = GetNonSecurePrefix(context) + context.Request.Path;
            if (context.Request.QueryString.HasValue && !string.IsNullOrEmpty(context.Request.QueryString.Value))
                thisUrl += "?" + context.Request.QueryString.Value;
            return thisUrl;
        }

        #endregion

        #region Helper methods

        private string GetSecurePrefix()
        {
            var securePrefix = string.IsNullOrEmpty(_secureDomain)
                ? string.Empty
                : _secureProtcol + "://" + _secureDomain;
            return securePrefix;
        }

        private string GetNonSecurePrefix(IOwinContext context)
        {
            var nonSecurePrefix = string.IsNullOrEmpty(_secureDomain)
                ? string.Empty
                : context.Request.Scheme + "://" + context.Request.Host;
            return nonSecurePrefix;
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

            _documentationPage = cleanUrl(configuration.DocumentationPage);

            _signupPage = cleanUrl(configuration.SignupPage);
            _signupSuccessPage = cleanUrl(configuration.SignupSuccessPage);
            _signupFailPage = cleanUrl(configuration.SignupFailPage);

            _signinPage = cleanUrl(configuration.SigninPage);
            _signinSuccessPage = cleanUrl(configuration.SigninSuccessPage);
            _signinFailPage = cleanUrl(configuration.SigninFailPage);

            _signoutPage = cleanUrl(configuration.SignoutPage);
            _signoutSuccessPage = cleanUrl(configuration.SignoutSuccessPage);

            _sendPasswordResetPage = cleanUrl(configuration.SendPasswordResetPage);
            _sendPasswordResetSuccessPage = cleanUrl(configuration.SendPasswordResetSuccessPage);
            _sendPasswordResetFailPage = cleanUrl(configuration.SendPasswordResetFailPage);

            _changePasswordPage = cleanUrl(configuration.ChangePasswordPage);
            _changePasswordSuccessPage = cleanUrl(configuration.ChangePasswordSuccessPage);
            _changePasswordFailPage = cleanUrl(configuration.ChangePasswordFailPage);

            _resetPasswordPage = cleanUrl(configuration.ResetPasswordPage);
            _resetPasswordSuccessPage = cleanUrl(configuration.ResetPasswordSuccessPage);
            _resetPasswordFailPage = cleanUrl(configuration.ResetPasswordFailPage);

            _renewSessionPage = cleanUrl(configuration.RenewSessionPage);
            _updateIdentityPage = cleanUrl(configuration.UpdateIdentityPage);

            _secureDomain = (configuration.SecureDomain ?? string.Empty).ToLower();
            _secureProtcol = (configuration.SecureProtocol ?? "https").ToLower();
            _cookieName = (configuration.CookieName ?? string.Empty).ToLower().Replace(' ', '-');
            _sessionIdentityName = (configuration.SessionIdentityName ?? string.Empty).ToLower().Replace(' ', '-');
            _sessionPurposeName = (configuration.SessionPurposeName ?? string.Empty).ToLower().Replace(' ', '-');
            _sessionStatusName = (configuration.SessionStatusName ?? string.Empty).ToLower().Replace(' ', '-');
            _sessionRememberMeName = (configuration.SessionRememberMeName ?? string.Empty).ToLower().Replace(' ', '-');
            _sessionEmailName = (configuration.SessionEmailName ?? string.Empty).ToLower().Replace(' ', '-');
        }

        #endregion

        #region ISelfDocumenting

        private Task DocumentConfiguration(IOwinContext context)
        {
            var document = GetEmbeddedResource("configuration.html");

            document = document.Replace("{documentationPage}", _documentationPage.ToString());

            document = document.Replace("{signupPage}", _signupPage.ToString());
            document = document.Replace("{signupSuccessPage}", _signupSuccessPage.ToString());
            document = document.Replace("{signupFailPage}", _signupFailPage.ToString());

            document = document.Replace("{signinPage}", _signinPage.ToString());
            document = document.Replace("{signinSuccessPage}", _signinSuccessPage.ToString());
            document = document.Replace("{signinFailPage}", _signinFailPage.ToString());

            document = document.Replace("{signoutPage}", _signoutPage.ToString());
            document = document.Replace("{signoutSuccessPage}", _signoutSuccessPage.ToString());

            document = document.Replace("{sendPasswordResetPage}", _sendPasswordResetPage.ToString());
            document = document.Replace("{sendPasswordResetSuccessPage}", _sendPasswordResetSuccessPage.ToString());
            document = document.Replace("{sendPasswordResetFailPage}", _sendPasswordResetFailPage.ToString());

            document = document.Replace("{resetPasswordPage}", _resetPasswordPage.ToString());
            document = document.Replace("{resetPasswordSuccessPage}", _resetPasswordSuccessPage.ToString());
            document = document.Replace("{resetPasswordFailPage}", _resetPasswordFailPage.ToString());

            document = document.Replace("{changePasswordPage}", _changePasswordPage.ToString());
            document = document.Replace("{changePasswordSuccessPage}", _changePasswordSuccessPage.ToString());
            document = document.Replace("{changePasswordFailPage}", _changePasswordFailPage.ToString());

            document = document.Replace("{renewSessionPage}", _renewSessionPage.ToString());
            document = document.Replace("{updateIdentityPage}", _updateIdentityPage.ToString());

            document = document.Replace("{secureDomain}", _secureDomain);
            document = document.Replace("{secureProtocol}", _secureProtcol);
            document = document.Replace("{cookieName}", _cookieName);
            document = document.Replace("{sessionIdentityName}", _sessionIdentityName);
            document = document.Replace("{sessionPurposeName}", _sessionPurposeName);
            document = document.Replace("{sessionStatusName}", _sessionStatusName);
            document = document.Replace("{SessionRememberMeName}", _sessionRememberMeName);
            document = document.Replace("{SessionEmailName}", _sessionEmailName);

            document = document.Replace("{rememberMeFor}", _configuration.RememberMeFor.ToString());

            document = document.Replace("{emailFormField}", _configuration.EmailFormField);
            document = document.Replace("{passwordFormField}", _configuration.PasswordFormField);
            document = document.Replace("{newPasswordFormField}", _configuration.NewPasswordFormField);
            document = document.Replace("{rememberMeFormField}", _configuration.RememberMeFormField);
            document = document.Replace("{tokenFormField}", _configuration.TokenFormField);

            var defaultConfiguration = new FormIdentificationConfiguration();
            document = document.Replace("{documentationPage.default}", defaultConfiguration.DocumentationPage);

            document = document.Replace("{signupPage.default}", defaultConfiguration.SignupPage);
            document = document.Replace("{signupSuccessPage.default}", defaultConfiguration.SignupSuccessPage);
            document = document.Replace("{signupFailPage.default}", defaultConfiguration.SignupFailPage);

            document = document.Replace("{signinPage.default}", defaultConfiguration.SigninPage);
            document = document.Replace("{signinSuccessPage.default}", defaultConfiguration.SigninSuccessPage);
            document = document.Replace("{signinFailPage.default}", defaultConfiguration.SigninFailPage);

            document = document.Replace("{signoutPage.default}", defaultConfiguration.SignoutPage);
            document = document.Replace("{signoutSuccessPage.default}", defaultConfiguration.SignoutSuccessPage);

            document = document.Replace("{sendPasswordResetPage.default}", defaultConfiguration.SendPasswordResetPage);
            document = document.Replace("{sendPasswordResetSuccessPage.default}", defaultConfiguration.SendPasswordResetSuccessPage);
            document = document.Replace("{sendPasswordResetFailPage.default}", defaultConfiguration.SendPasswordResetFailPage);

            document = document.Replace("{resetPasswordPage.default}", defaultConfiguration.ResetPasswordPage);
            document = document.Replace("{resetPasswordSuccessPage.default}", defaultConfiguration.ResetPasswordSuccessPage);
            document = document.Replace("{resetPasswordFailPage.default}", defaultConfiguration.ResetPasswordFailPage);

            document = document.Replace("{changePasswordPage.default}", defaultConfiguration.ChangePasswordPage);
            document = document.Replace("{changePasswordSuccessPage.default}", defaultConfiguration.ChangePasswordSuccessPage);
            document = document.Replace("{changePasswordFailPage.default}", defaultConfiguration.ChangePasswordFailPage);

            document = document.Replace("{renewSessionPage.default}", defaultConfiguration.RenewSessionPage);
            document = document.Replace("{updateIdentityPage.default}", defaultConfiguration.UpdateIdentityPage);

            document = document.Replace("{secureDomain.default}", defaultConfiguration.SecureDomain);
            document = document.Replace("{secureProtocol.default}", defaultConfiguration.SecureProtocol);
            document = document.Replace("{cookieName.default}", defaultConfiguration.CookieName);
            document = document.Replace("{sessionIdentityName.default}", defaultConfiguration.SessionIdentityName);
            document = document.Replace("{sessionPurposeName.default}", defaultConfiguration.SessionPurposeName);
            document = document.Replace("{sessionStatusName.default}", defaultConfiguration.SessionStatusName);
            document = document.Replace("{SessionRememberMeName.default}", defaultConfiguration.SessionRememberMeName);
            document = document.Replace("{SessionEmailName.default}", defaultConfiguration.SessionEmailName);

            document = document.Replace("{rememberMeFor.default}", defaultConfiguration.RememberMeFor.ToString());

            document = document.Replace("{emailFormField.default}", defaultConfiguration.EmailFormField);
            document = document.Replace("{passwordFormField.default}", defaultConfiguration.PasswordFormField);
            document = document.Replace("{newPasswordFormField.default}", defaultConfiguration.NewPasswordFormField);
            document = document.Replace("{rememberMeFormField.default}", defaultConfiguration.RememberMeFormField);
            document = document.Replace("{tokenFormField.default}", defaultConfiguration.TokenFormField);

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

                if (_documentationPage.HasValue)
                    documentation.Add(
                        new EndpointDocumentation
                        {
                            RelativePath = _documentationPage.Value,
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
                            Description = "User account creation via email and password. Always POST to this endpoint over HTTPS",
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
                            Description = "User login via email and password. Always POST to this endpoint over HTTPS",
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
                            RelativePath = _signoutPage.Value,
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
                            RelativePath = _sendPasswordResetPage.Value,
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
                            RelativePath = _resetPasswordPage.Value,
                            Description = "Resets a user's password. One time use within expiry time. Always POST to this endpoint over HTTPS",
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
                                : _secureProtcol + "://" + _secureDomain + _renewSessionPage.Value,
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

                if (_updateIdentityPage.HasValue)
                    documentation.Add(
                        new EndpointDocumentation
                        {
                            RelativePath = string.IsNullOrEmpty(_secureDomain)
                                ? _updateIdentityPage.Value
                                : _secureProtcol + "://" + _secureDomain + _updateIdentityPage.Value,
                            Description =
                                "Updates the user identification cookie from session",
                            Attributes = new List<IEndpointAttributeDocumentation>
                                {
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Method",
                                        Name = "GET",
                                        Description = "Updates user identification information"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Parameter",
                                        Name = "sid",
                                        Description = "The session id of the session"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Parameter",
                                        Name = "success",
                                        Description = "The URL to redirect the browser to after updating the cookie"
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
                stats.Add(
                    new StatisticInformation
                    {
                        Id = "UpdateRememberMeCount",
                        Name = "Update remember me count",
                        Description = "The number of times a remember me token was stored in the users cookies",
                        Explanation = "These updates happen whenever a user logs in with credentials or logs out"
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
                case "UpdateRememberMeCount": return new IntStatistic(() => _updateRememberMeCount);
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
