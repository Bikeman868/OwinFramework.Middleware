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
using OwinFramework.Interfaces.Utility;
using OwinFramework.InterfacesV1.Capability;
using OwinFramework.InterfacesV1.Facilities;
using OwinFramework.InterfacesV1.Middleware;
using OwinFramework.InterfacesV1.Upstream;
using OwinFramework.MiddlewareHelpers.Analysable;
using OwinFramework.MiddlewareHelpers.Identification;
using OwinFramework.MiddlewareHelpers.SelfDocumenting;
using OwinFramework.MiddlewareHelpers.Traceable;

namespace OwinFramework.FormIdentification
{
    public class FormIdentificationMiddleware:
        IMiddleware<IIdentification>,
        IUpstreamCommunicator<IUpstreamSession>,
        IConfigurable,
        ISelfDocumenting,
        IAnalysable,
        ITraceable
    {
        private const string _anonymousUserIdentity = "urn:form.identity:anonymous:";

        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        IList<IDependency> IMiddleware.Dependencies { get { return _dependencies; } }

        string IMiddleware.Name { get; set; }
        public Action<IOwinContext, Func<string>> Trace { get; set; }
        private TraceFilter _traceFilter;

        private readonly IIdentityStore _identityStore;
        private readonly IIdentityDirectory _identityDirectory;
        private readonly ITokenStore _tokenStore;
        private readonly IHostingEnvironment _hostingEnvironment;

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
        private string _sessionClaimsName;
        private string _sessionIsAnonymousName;
        private string _sessionMessageName;

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

        private PathString _changeEmailPage;
        private PathString _changeEmailSuccessPage;
        private PathString _changeEmailFailPage;

        private PathString _sendPasswordResetPage;
        private PathString _sendPasswordResetSuccessPage;
        private PathString _sendPasswordResetFailPage;

        private PathString _resetPasswordPage;
        private PathString _resetPasswordSuccessPage;
        private PathString _resetPasswordFailPage;

        private PathString _verifyEmailPage;
        private PathString _verifyEmailSuccessPage;
        private PathString _verifyEmailFailPage;

        private PathString _revertEmailPage;
        private PathString _revertEmailSuccessPage;
        private PathString _revertEmailFailPage;

        private PathString _renewSessionPage;
        private PathString _updateIdentityPage;

        public FormIdentificationMiddleware(
            IIdentityStore identityStore, 
            IIdentityDirectory identityDirectory,
            ITokenStore tokenStore,
            IHostingEnvironment hostingEnvironment)
        {
            _identityStore = identityStore;
            _identityDirectory = identityDirectory;
            _tokenStore = tokenStore;
            _hostingEnvironment = hostingEnvironment;
            _traceFilter = new TraceFilter(null, this);

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
                if (context.Request.Path == _changeEmailPage)
                    return HandleChangeEmail(context);
                if (context.Request.Path == _signupPage)
                    return HandleSignup(context);
                if (context.Request.Path == _changePasswordPage)
                    return HandleChangePassword(context);
                if (context.Request.Path == _verifyEmailPage)
                    return HandleRequestVerifyEmail(context);
                if (context.Request.Path == _revertEmailPage)
                    return HandleRevertEmail(context);
            }
            else if (context.Request.Method == "GET")
            {
                if (context.Request.Path == _renewSessionPage)
                    return HandleRenewSession(context);
                if (context.Request.Path == _updateIdentityPage)
                    return HandleRememberMe(context);
                if (context.Request.Path == _verifyEmailPage)
                    return HandleVerifyEmail(context);
                if (context.Request.Path == _documentationPage)
                    return DocumentConfiguration(context);
            }

            var identification = context.GetFeature<IIdentification>();

            if (identification != null && !identification.IsAnonymous && string.IsNullOrEmpty(identification.Identity))
            {
                // We get here when we don't know anything about the user because their session expired
                var upstreamIdentification = context.GetFeature<IUpstreamIdentification>();
                return RenewSession(context, upstreamIdentification);
            }

            return next();
        }

        #region User identification

        private void IdentifyUser(IOwinContext context)
        {
            // If identification middleware further up the pipeline already 
            // identified the user then do nothing here
            var priorIdentification = context.GetFeature<IIdentification>();
            if (priorIdentification != null &&
                !string.IsNullOrEmpty(priorIdentification.Identity) &&
                !priorIdentification.IsAnonymous)
            {
                _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " the user was already identified by prior middleware");
                return;
            }

            var upstreamSession = context.GetFeature<IUpstreamSession>();
            if (upstreamSession == null)
                throw new Exception("Session middleware is required for Form Identification to work");

            if (!upstreamSession.EstablishSession())
            {
                _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " the user can not be identified in the routing phase because session middleware was unable to establish a session during routing");
                return;
            }

            var session = context.GetFeature<ISession>();
            if (session == null)
                throw new Exception("Session middleware is required for Form Identification to work");

            string identity;
            List<string> purposes;
            AuthenticationStatus status;
            string rememberMe;
            List<IdentityClaim> claims;
            bool isAnonymous;
            GetSession(session, out identity, out purposes, out status, out rememberMe, out claims, out isAnonymous);

            var identification = new Identification(identity ?? string.Empty, claims, isAnonymous, purposes);
            context.SetFeature<IIdentification>(identification);

            identification.AllowAnonymous = true;
            context.SetFeature<IUpstreamIdentification>(identification);
        }

        private Task RenewSession(
            IOwinContext context, 
            IUpstreamIdentification upstreamIdentification)
        {
            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " renewing session");

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
            {
                renewSessionUrl += "&fail=" + Uri.EscapeDataString(thisUrl);
                _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " user is not required to sign in");
            }
            else
            {
                renewSessionUrl += "&fail=" + Uri.EscapeDataString(signinUrl);
                _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " user must sign in if identity can not be recovered from cookie");
            }

            _traceFilter.Trace(context, TraceLevel.Debug, () => GetType().Name + " redirecting to " + renewSessionUrl);
            
            context.Response.Redirect(renewSessionUrl);
            return context.Response.WriteAsync(string.Empty);
        }

        #endregion

        #region Account functions

        /// <summary>
        /// This request is handled in the main site domain when the user POSTs
        /// the sign up form.
        /// </summary>
        private Task HandleSignup(IOwinContext context)
        {
            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " handling sign up request");

            var form = context.Request.ReadFormAsync().Result;

            var email = form[_configuration.EmailFormField];
            var password = form[_configuration.PasswordFormField];
            var rememberMe = !string.IsNullOrEmpty(form[_configuration.RememberMeFormField]);
            var thisUrl = GetThisUrl(context);

            var session = context.GetFeature<ISession>();
            var upstreamSession = context.GetFeature<IUpstreamSession>();

            if (session == null || upstreamSession == null)
                throw new Exception("Session middleware is required for Form Identification to work");

            bool success;
            string identity = null;
            try
            {
                identity = _identityDirectory.CreateIdentity();
                _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " created new identity " + identity);

                success = _identityStore.AddCredentials(identity, email, password);

                if (success)
                {
                    _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " successfully replaced credentials for " + identity);
                    SetSessionMessage(session, string.Empty);
                }
                else
                {
                    _traceFilter.Trace(context, TraceLevel.Error, () => GetType().Name + " failed to replace credentials for " + identity);
                    SetSessionMessage(
                        session, 
                        "Your account was successfully created but something went wrong when saving your password. " +
                        "If you are unable to sign in with this email and password, please choose the reset password option. "+
                        "Applogies for the inconvenience.");
                }
            }
            catch (Exception ex)
            {
                _traceFilter.Trace(context, TraceLevel.Error, () => GetType().Name + " failed to create new identity: " + ex.Message);
                SetSessionMessage(
                    session, 
                    "An account could not be created with those details. If your email address was already used "+
                    "to sign up and do not know the password then please choose the password reset option. If " +
                    "you are still unable to sign up please contact our support team and tell them that the "+
                    "following error occurred: " + ex.Message);
                success = false;
            }

            if (success)
            {
                _signupSuccessCount++;

                try
                {
                    SendWelcomeEmail(context, identity, email);
                }
                catch (Exception ex)
                {
                    _traceFilter.Trace(
                        context, TraceLevel.Error, 
                        () => GetType().Name + " failed to send welcome email to " + email + " for " + identity + ". " + ex.Message);

                    SetSessionMessage(
                        session,
                        "An account was created for you, but there was a problem sending the " +
                        "welcome email that lets you confirm your email address. There is an option " +
                        "on the account page to resent this email. Please use this option to resend this " + 
                        "email. Our appoligies for this inconvenience.");
                }

                var authenticationResult = _identityStore.AuthenticateWithCredentials(email, password);

                if (authenticationResult.Status != AuthenticationStatus.Authenticated)
                {
                    _traceFilter.Trace(
                        context, TraceLevel.Error,
                        () => GetType().Name + " failed to authenticate as " + identity + " because " + authenticationResult.Status);

                    SetSessionMessage(
                        session,
                        "An account was created for you, but there was a problem automatically logging you into the website. " +
                        "Please login manually using the email address and password that you used to sign up. " +
                        "Our appoligies for this inconvenience.");
                }
                else
                {
                    _identityDirectory.UpdateClaim(identity, new IdentityClaim(ClaimNames.Email, email, ClaimStatus.Unverified));
                    var claims = _identityDirectory.GetClaims(authenticationResult.Identity);

                    SetSession(
                        session,
                        identity,
                        authenticationResult.Purposes,
                        authenticationResult.Status,
                        rememberMe ? authenticationResult.RememberMeToken : null,
                        claims,
                        false);
                }

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

                    _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " redirecting to " + updateIdentityUrl);
                    return Redirect(context, updateIdentityUrl);
                }

                _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " redirecting to " + (_signupSuccessPage.HasValue ? _signupSuccessPage.Value : thisUrl));
                return Redirect(context, _signupSuccessPage.HasValue ? _signupSuccessPage.Value : thisUrl);
            }

            _signupFailCount++;

            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " redirecting to " + (_signupFailPage.HasValue ? _signupFailPage.Value : thisUrl));
            return Redirect(context, _signupFailPage.HasValue ? _signupFailPage.Value : thisUrl);
        }

        /// <summary>
        /// This request is handled in the main site domain when the user POSTs
        /// the sign in form.
        /// </summary>
        private Task HandleSignin(IOwinContext context)
        {
            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " handling sign in request");

            var form = context.Request.ReadFormAsync().Result;

            var email = form[_configuration.EmailFormField];
            var password = form[_configuration.PasswordFormField];
            var rememberMe = !string.IsNullOrEmpty(form[_configuration.RememberMeFormField]);
            var thisUrl = GetThisUrl(context);

            var session = context.GetFeature<ISession>();
            var upstreamSession = context.GetFeature<IUpstreamSession>();
            if (session == null || upstreamSession == null)
                throw new Exception("Session middleware is required for Form Identification to work");

            bool success;
            IAuthenticationResult authenticationResult = null;
            try
            {
                authenticationResult = _identityStore.AuthenticateWithCredentials(email, password);
                success = authenticationResult.Status == AuthenticationStatus.Authenticated;

                if (success)
                {
                    _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " successfull login as " + authenticationResult.Identity);
                    SetSessionMessage(session, string.Empty);
                }
                else
                {
                    _traceFilter.Trace(context, TraceLevel.Error, () => GetType().Name + " failed to login as " + email + " because " + authenticationResult.Status);
                    SetSessionMessage(session, "Login failed. Please check your password and try again or select the password reset option.");
                }
            }
            catch (Exception ex)
            {
                success = false;

                _traceFilter.Trace(context, TraceLevel.Error, () => GetType().Name + " exception loging in as " + email + ". " + ex.Message);
                SetSessionMessage(
                    session, 
                    "Something went wrong in out system, please try logging in again. " +
                    "Our appologies for this inconvenience. If you see this message again " +
                    "please contact our support team. Thank you.");
            }

            if (success)
            {
                _signinSuccessCount++;

                var claims = _identityDirectory.GetClaims(authenticationResult.Identity);

                SetSession(
                    session, 
                    authenticationResult.Identity, 
                    authenticationResult.Purposes,
                    authenticationResult.Status,
                    rememberMe ? authenticationResult.RememberMeToken : null,
                    claims,
                    false);

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

                _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " redirecting to " + (_signinSuccessPage.HasValue ? _signinSuccessPage.Value : thisUrl));
                return Redirect(context, _signinSuccessPage.HasValue ? _signinSuccessPage.Value : thisUrl);
            }

            _signinFailCount++;

            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " redirecting to " + (_signinFailPage.HasValue ? _signinFailPage.Value : thisUrl));
            return Redirect(context, _signinFailPage.HasValue ? _signinFailPage.Value : thisUrl);
        }

        /// <summary>
        /// This request is handled in the main site domain when the user POSTs
        /// the sign out form.
        /// </summary>
        private Task HandleSignout(IOwinContext context)
        {
            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " handling sign out request");

            var upstreamSession = context.GetFeature<IUpstreamSession>();
            var session = context.GetFeature<ISession>();
            if (upstreamSession == null || session == null)
                throw new Exception("Session middleware is required for Form Identification to work");

            _signoutCount++;

            SetSessionMessage(session, string.Empty);
            SetSession(session, null, null, AuthenticationStatus.NotFound, null, null, true);

            var nonSecurePrefix = GetNonSecurePrefix(context);
            var thisUrl = GetThisUrl(context);

            if (string.IsNullOrEmpty(_secureDomain))
            {
                UpdateRememberMeCookie(context);

                var nonSecureSuccessUrl = _signoutSuccessPage.HasValue ? nonSecurePrefix + _signoutSuccessPage.Value : thisUrl;

                _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " no secure domain configured, redirecting to " + nonSecureSuccessUrl);
                return RedirectWithCookies(context, nonSecureSuccessUrl);
            }

            var securePrefix = GetSecurePrefix();

            var secureSuccessUrl = _signoutSuccessPage.HasValue ? nonSecurePrefix + _signoutSuccessPage.Value : thisUrl;

            var updateIdentityUrl = securePrefix + _updateIdentityPage.Value;
            updateIdentityUrl += "?sid=" + Uri.EscapeDataString(upstreamSession.SessionId);
            updateIdentityUrl += "&success=" + Uri.EscapeDataString(secureSuccessUrl);

            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " secure domain is configured, redirecting to " + updateIdentityUrl);
            return Redirect(context, updateIdentityUrl);
        }

        /// <summary>
        /// This request is handled in the main site domain when the user POSTs
        /// the change password form.
        /// </summary>
        private Task HandleChangePassword(IOwinContext context)
        {
            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " handling change password request");

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

            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " secure domain is configured, redirecting to " + (_changePasswordFailPage.HasValue ? _changePasswordFailPage.Value : thisUrl));
            return Redirect(context, _changePasswordFailPage.HasValue ? _changePasswordFailPage.Value : thisUrl);
        }

        /// <summary>
        /// This request is handled in the main site domain when the user POSTs
        /// the send password reset form.
        /// </summary>
        private Task HandleSendPasswordReset(IOwinContext context)
        {
            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " handling send password reset request");

            var session = context.GetFeature<ISession>();
            if (session == null)
                throw new Exception("Session middleware is required for Form Identification to work");

            var form = context.Request.ReadFormAsync().Result;
            var email = form[_configuration.EmailFormField];

            var thisUrl = GetThisUrl(context);

            var credential = _identityStore.GetUsernameCredential(email);

            if (credential == null)
            {
                SetSessionMessage(session, "No accont was found with this email address. Please sign up.");

                _traceFilter.Trace(
                    context, TraceLevel.Error, 
                    () => GetType().Name + " no credentals found for this email address, redirecting to " + 
                    (_sendPasswordResetFailPage.HasValue ? _sendPasswordResetFailPage.Value : thisUrl));
                return Redirect(context, _sendPasswordResetFailPage.HasValue ? _sendPasswordResetFailPage.Value : thisUrl);
            }

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

            _traceFilter.Trace(
                context, TraceLevel.Error,
                () => GetType().Name + " password reset email was sent, redirecting to " +
                (_sendPasswordResetSuccessPage.HasValue ? _sendPasswordResetSuccessPage.Value : thisUrl));
            return Redirect(context, _sendPasswordResetSuccessPage.HasValue ? _sendPasswordResetSuccessPage.Value : thisUrl);
        }

        /// <summary>
        /// This request is handled in the main site domain when the user follows the
        /// link in a password reset email then POSTs the reset password form.
        /// </summary>
        private Task HandleResetPassword(IOwinContext context)
        {
            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " handling reset password request");

            var form = context.Request.ReadFormAsync().Result;

            var tokenString = form[_configuration.TokenFormField];
            var email = form[_configuration.EmailFormField];
            var newPassword = form[_configuration.NewPasswordFormField];

            if (string.IsNullOrEmpty(tokenString))
                tokenString = context.Request.Query["token"];

            var thisUrl = GetThisUrl(context);

            var token = _tokenStore.GetToken(_configuration.PasswordResetTokenType, tokenString, "ResetPassword", email);

            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " password reset token status is " + token.Status);

            if (token.Status == TokenStatus.Allowed)
            {
                var credential =_identityStore.GetUsernameCredential(email);
                if (credential != null)
                {
                    if (_identityStore.ChangePassword(credential, newPassword))
                    {
                        _tokenStore.DeleteToken(tokenString);
                        _traceFilter.Trace(
                            context, TraceLevel.Information,
                            () => GetType().Name + " password was successfully changed, redirecting to " +
                            (_resetPasswordSuccessPage.HasValue ? _resetPasswordSuccessPage.Value : thisUrl));
                        return Redirect(context, _resetPasswordSuccessPage.HasValue ? _resetPasswordSuccessPage.Value : thisUrl);
                    }
                }
            }

            _traceFilter.Trace(
                context, TraceLevel.Error,
                () => GetType().Name + " password was successfully reset, redirecting to " +
                (_resetPasswordFailPage.HasValue ? _resetPasswordFailPage.Value : thisUrl));
            return Redirect(context, _resetPasswordFailPage.HasValue ? _resetPasswordFailPage.Value : thisUrl);
        }

        /// <summary>
        /// This request is handled in the main site domain when the user POSTs
        /// the change email address form.
        /// </summary>
        private Task HandleChangeEmail(IOwinContext context)
        {
            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " handling change email request");

            var form = context.Request.ReadFormAsync().Result;
            var email = form[_configuration.EmailFormField];
            var password = form[_configuration.PasswordFormField];
            var newEmail = form[_configuration.NewEmailFormField];

            var thisUrl = GetThisUrl(context);

            var authenticationResult = _identityStore.AuthenticateWithCredentials(email, password);
            if (authenticationResult == null ||
                authenticationResult.Status != AuthenticationStatus.Authenticated ||
                (authenticationResult.Purposes != null && authenticationResult.Purposes.Count > 0))
            {
                _traceFilter.Trace(context, TraceLevel.Error, () => GetType().Name + " unable to change email address with these credentials");
                return Redirect(context, _changeEmailFailPage.HasValue ? _changeEmailFailPage.Value : thisUrl);
            }

            if (!_identityStore.AddCredentials(authenticationResult.Identity, newEmail, password))
            {
                _traceFilter.Trace(context, TraceLevel.Error, () => GetType().Name + " the identity store failed to update the email address");
                return Redirect(context, _changeEmailFailPage.HasValue ? _changeEmailFailPage.Value : thisUrl);
            }

            _identityDirectory.UpdateClaim(
                authenticationResult.Identity, 
                new IdentityClaim(ClaimNames.Email, newEmail, ClaimStatus.Unverified));

            _identityDirectory.UpdateClaim(
                authenticationResult.Identity,
                new IdentityClaim(ClaimNames.Username, newEmail, ClaimStatus.Verified));

            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " email address successfully changed");

            var fromEmail = _configuration.EmailChangeEmailFrom;
            if (string.IsNullOrWhiteSpace(fromEmail))
                fromEmail = "email-change@" + context.Request.Host;

            var emailClient = new SmtpClient();

            if (_revertEmailPage.HasValue)
            {
                _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " sending email to revert email address change");

                var fromEmailHtml = GetEmbeddedResource("EmailChangeFromEmail.html");
                var fromEmailText = GetEmbeddedResource("EmailChangeFromEmail.txt");

                var revertTtoken = _tokenStore.CreateToken(_configuration.RevertEmailTokenType, email, authenticationResult.Identity);
                var pageUrl = GetNonSecurePrefix(context) + _revertEmailPage;

                fromEmailHtml = fromEmailHtml
                    .Replace("{page}", pageUrl)
                    .Replace("{old-email}", email)
                    .Replace("{new-email}", newEmail)
                    .Replace("{token}", revertTtoken)
                    .Replace("{id}", authenticationResult.Identity);

                fromEmailText = fromEmailText
                    .Replace("{page}", pageUrl)
                    .Replace("{old-email}", email)
                    .Replace("{new-email}", newEmail)
                    .Replace("{token}", revertTtoken)
                    .Replace("{id}", authenticationResult.Identity);
            
                var mailMessage = new MailMessage(fromEmail, email, _configuration.EmailChangeEmailSubject, fromEmailText);
                mailMessage.Subject = _configuration.EmailChangeEmailSubject;
                mailMessage.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(fromEmailHtml, new ContentType("text/html")));

                emailClient.Send(mailMessage);
            }

            if (_verifyEmailPage.HasValue)
            {
                _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " sending email to confirm email address change");

                var toEmailHtml = GetEmbeddedResource("EmailChangeToEmail.html");
                var toEmailText = GetEmbeddedResource("EmailChangeToEmail.txt");

                var verifyTtoken = _tokenStore.CreateToken(_configuration.VerifyEmailTokenType, newEmail, authenticationResult.Identity);
                var pageUrl = GetNonSecurePrefix(context) + _verifyEmailPage;

                toEmailHtml = toEmailHtml
                    .Replace("{page}", pageUrl)
                    .Replace("{old-email}", email)
                    .Replace("{new-email}", newEmail)
                    .Replace("{token}", verifyTtoken)
                    .Replace("{id}", authenticationResult.Identity);

                toEmailText = toEmailText
                    .Replace("{page}", pageUrl)
                    .Replace("{old-email}", email)
                    .Replace("{new-email}", newEmail)
                    .Replace("{token}", verifyTtoken)
                    .Replace("{id}", authenticationResult.Identity);

                var mailMessage = new MailMessage(fromEmail, newEmail, _configuration.EmailChangeEmailSubject, toEmailText);
                mailMessage.Subject = _configuration.EmailChangeEmailSubject;
                mailMessage.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(toEmailHtml, new ContentType("text/html")));

                emailClient.Send(mailMessage);
            }

            _traceFilter.Trace(
                context, TraceLevel.Information,
                () => GetType().Name + " redirecting to " +
                (_changeEmailSuccessPage.HasValue ? _changeEmailSuccessPage.Value : thisUrl));
            return Redirect(context, _changeEmailSuccessPage.HasValue ? _changeEmailSuccessPage.Value : thisUrl);
        }

        /// <summary>
        /// This request is handled in the main site domain when the user clicks
        /// the link in the welcome message to verify their email address, or if
        /// they POST to request a new email to be sent.
        /// </summary>
        private Task HandleVerifyEmail(IOwinContext context)
        {
            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " handling verify email address request");

            var success = false;

            var tokenString = context.Request.Query["token"];
            var identity = context.Request.Query["id"];
            var email = context.Request.Query["email"];

            if (string.IsNullOrEmpty(tokenString) || 
                string.IsNullOrEmpty(identity) ||
                string.IsNullOrEmpty(email))
            {
                _traceFilter.Trace(context, TraceLevel.Error, () => GetType().Name + " email verification URL does not include all required query string parameters");
            }
            else
            {
                var token = _tokenStore.GetToken(_configuration.VerifyEmailTokenType, tokenString, email, identity);
                if (token.Status == TokenStatus.Allowed)
                {
                    _identityDirectory.UpdateClaim(
                        identity,
                        new IdentityClaim(ClaimNames.Email, email, ClaimStatus.Verified));
                    _tokenStore.DeleteToken(tokenString);
                    success = true;
                }
            }

            var nextPage = success ? _verifyEmailSuccessPage : _verifyEmailFailPage;
            if (!nextPage.HasValue)
            {
                _traceFilter.Trace(context, TraceLevel.Error, () => GetType().Name + " no configuration for success and/or fail page. Please check your configuration.");
                nextPage = _documentationPage;
            }

            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " redirecting to " + nextPage);
            return Redirect(context, nextPage.Value);
        }

        /// <summary>
        /// This request is handled in the main site domain when the user chooses
        /// not to proceed with their email address change. This method resets the
        /// email back to the original and also resets the password. This can be used
        /// in the situation where the user accidentally changed their email address to
        /// one that they don't own and cannot verify.
        /// </summary>
        private Task HandleRevertEmail(IOwinContext context)
        {
            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " handling revert email change request");

            var success = false;

            var form = context.Request.ReadFormAsync().Result;
            var tokenString = form[_configuration.TokenFormField];
            var identity = form[_configuration.IdentityFormField];
            var oldEmail = form[_configuration.EmailFormField];
            var password = form[_configuration.NewPasswordFormField];

            if (string.IsNullOrEmpty(tokenString) ||
                string.IsNullOrEmpty(identity) ||
                string.IsNullOrEmpty(oldEmail) ||
                string.IsNullOrEmpty(password))
            {
                _traceFilter.Trace(context, TraceLevel.Error, () => GetType().Name + " email revert page does not include all required form variables");
            }
            else
            {
                var token = _tokenStore.GetToken(_configuration.RevertEmailTokenType, tokenString, oldEmail, identity);
                if (token.Status == TokenStatus.Allowed)
                {
                    if (_identityStore.AddCredentials(identity, oldEmail, password))
                    {
                        _identityDirectory.UpdateClaim(
                            identity,
                            new IdentityClaim(ClaimNames.Email, oldEmail, ClaimStatus.Unverified));

                        _identityDirectory.UpdateClaim(
                            identity,
                            new IdentityClaim(ClaimNames.Username, oldEmail, ClaimStatus.Verified));

                        _tokenStore.DeleteToken(tokenString);

                        success = true;
                    }
                }
            }

            var nextPage = success ? _revertEmailSuccessPage : _revertEmailFailPage;
            if (!nextPage.HasValue)
            {
                _traceFilter.Trace(context, TraceLevel.Error, () => GetType().Name + " no configuration for success and/or fail page. Please check your configuration.");
                nextPage = _documentationPage;
            }

            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " redirecting to " + nextPage);
            return Redirect(context, nextPage.Value);
        }

        /// <summary>
        /// This request is handled in the main site domain when the user requests
        /// a new email verification email to be sent to them.
        /// </summary>
        private Task HandleRequestVerifyEmail(IOwinContext context)
        {
            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " handling request for new email verification email");
            
            var success = false;

            var identification = context.GetFeature<IIdentification>();
            if (identification != null &&
                !identification.IsAnonymous &&
                !string.IsNullOrEmpty(identification.Identity) &&
                identification.Identity != _anonymousUserIdentity)
            {
                var emailClaim = _identityDirectory.GetClaims(identification.Identity).FirstOrDefault(c => string.Equals(c.Name, ClaimNames.Email));
                if (emailClaim != null)
                {
                    SendWelcomeEmail(context, identification.Identity, emailClaim.Value);
                    success = true;
                }
            }

            var nextPage = success ? _signupSuccessPage : _verifyEmailFailPage;
            if (!nextPage.HasValue)
            {
                _traceFilter.Trace(context, TraceLevel.Error, () => GetType().Name + " no configuration for success and/or fail page. Please check your configuration.");
                nextPage = _documentationPage;
            }

            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " redirecting to " + nextPage);
            return Redirect(context, nextPage.Value);
        }

        /// <summary>
        /// This request is handled in the secure sub-domain when the users session expires.
        /// It tries to log the user in again using the remember me cookie.
        /// </summary>
        private Task HandleRenewSession(IOwinContext context)
        {
            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " handling session renewal request");

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
                SetSession(
                    session, 
                    _anonymousUserIdentity, 
                    null, 
                    AuthenticationStatus.NotFound, 
                    null, 
                    null,
                    true);
                return Redirect(context, failUrl);
            }

            var authenticationResult = _identityStore.RememberMe(rememberMeToken);
            var claims = _identityDirectory.GetClaims(authenticationResult.Identity);
            SetSession(
                session,
                authenticationResult.Identity ?? _anonymousUserIdentity, 
                authenticationResult.Purposes, 
                authenticationResult.Status, 
                authenticationResult.RememberMeToken,
                claims,
                authenticationResult.Status == AuthenticationStatus.Anonymous);


            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " redirecting to " + (authenticationResult.Status == AuthenticationStatus.Authenticated ? successUrl : failUrl));
            return Redirect(context, authenticationResult.Status == AuthenticationStatus.Authenticated ? successUrl : failUrl);
        }

        /// <summary>
        /// This request is handled on the secure sub-domain whenever the user logs in or out.
        /// It is responsible for setting or clearing the cookie thet is stored in the 
        /// secure sub-domain.
        /// </summary>
        private Task HandleRememberMe(IOwinContext context)
        {
            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " handling remember me request");

            var session = context.GetFeature<ISession>();
            var upstreamSession = context.GetFeature<IUpstreamSession>();
            if (upstreamSession == null || session == null)
                throw new Exception("Session middleware is required for Form Identification to work");

            var sessionId = context.Request.Query["sid"];
            var successUrl = context.Request.Query["success"];

            upstreamSession.EstablishSession(sessionId);
            UpdateRememberMeCookie(context);

            _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " redirecting to " + successUrl);
            return RedirectWithCookies(context, successUrl);
        }

        private void UpdateRememberMeCookie(IOwinContext context)
        {
            var session = context.GetFeature<ISession>();

            GetSession(
                session, 
                out string identity, 
                out List<string> purpose, 
                out AuthenticationStatus status, 
                out string rememberMeToken, 
                out List<IdentityClaim> claims, 
                out bool isAnonymous);

            if (string.IsNullOrEmpty(rememberMeToken))
            {
                _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " deleting remember me cookie " + _cookieName);
                context.Response.Cookies.Delete(_cookieName);
            }
            else
            {
                _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " setting remember me cookie " + _cookieName + "+" + rememberMeToken);
                context.Response.Cookies.Append(
                        _cookieName,
                        rememberMeToken,
                        new CookieOptions
                        {
                            HttpOnly = true,
                            Secure = context.Request.IsSecure,
                            Expires = DateTime.UtcNow + _configuration.RememberMeFor
                        });
            }
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
            IEnumerable<string> purposes,
            AuthenticationStatus status,
            string rememberMeToken,
            IEnumerable<IIdentityClaim> claims,
            bool isAnonymous)
        {
            session.Set(_sessionIdentityName, identity ?? string.Empty);
            session.Set(_sessionPurposeName, purposes == null ? new List<string>() : purposes.ToList());
            session.Set(_sessionStatusName, status);
            session.Set(_sessionRememberMeName, rememberMeToken);
            session.Set(_sessionClaimsName, claims == null ? null : claims.Select(c => new IdentityClaim(c)).ToList());
            session.Set(_sessionIsAnonymousName, isAnonymous);
        }

        private void GetSession(
            ISession session,
            out string identity,
            out List<string> purposes,
            out AuthenticationStatus status,
            out string rememberMeToken,
            out List<IdentityClaim> claims,
            out bool isAnonymous)
        {
            identity = session.Get<string>(_sessionIdentityName) ?? string.Empty;
            purposes = session.Get<List<string>>(_sessionPurposeName) ?? new List<string>();
            status = session.Get<AuthenticationStatus>(_sessionStatusName);
            rememberMeToken = session.Get<string>(_sessionRememberMeName);
            claims = session.Get<List<IdentityClaim>>(_sessionClaimsName);
            isAnonymous = session.Get<bool>(_sessionIsAnonymousName);
        }

        private void SetSessionMessage(ISession session, string message)
        {
            session.Set(_sessionMessageName, message);
        }

        private string GetThisUrl(IOwinContext context)
        {
            var requestRewriter = context.GetFeature<IRequestRewriter>();
            var path = requestRewriter == null ? context.Request.Path : requestRewriter.OriginalPath;

            var thisUrl = GetNonSecurePrefix(context) + path;
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

        private void SendWelcomeEmail(IOwinContext context, string identity, string email)
        {
            if (_verifyEmailPage.HasValue)
            {
                _traceFilter.Trace(context, TraceLevel.Information, () => GetType().Name + " sending welcome email to " + email);

                var pageUrl = GetNonSecurePrefix(context) + _verifyEmailPage;
                var tokenString = _tokenStore.CreateToken(_configuration.VerifyEmailTokenType, email, identity);
                var emailHtml = GetEmbeddedResource("WelcomeEmail.html");
                var emailText = GetEmbeddedResource("WelcomeEmail.txt");

                emailHtml = emailHtml
                    .Replace("{email}", email)
                    .Replace("{page}", pageUrl)
                    .Replace("{token}", tokenString)
                    .Replace("{id}", identity);

                emailText = emailText
                    .Replace("{email}", email)
                    .Replace("{page}", pageUrl)
                    .Replace("{token}", tokenString)
                    .Replace("{id}", identity);

                var fromEmail = _configuration.WelcomeEmailFrom;
                if (string.IsNullOrWhiteSpace(fromEmail))
                    fromEmail = "welcome@" + context.Request.Host;

                var mailMessage = new MailMessage(fromEmail, email, _configuration.WelcomeEmailSubject, emailText);
                mailMessage.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(emailHtml, new ContentType("text/html")));

                var emailClient = new SmtpClient();
                emailClient.Send(mailMessage);
            }
        }

        #endregion

        #region IConfigurable

        public void Configure(IConfiguration configuration, string path)
        {
            _traceFilter.ConfigureWith(configuration);
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

            _changeEmailPage = cleanUrl(configuration.ChangeEmailPage);
            _changeEmailSuccessPage = cleanUrl(configuration.ChangeEmailSuccessPage);
            _changeEmailFailPage = cleanUrl(configuration.ChangeEmailFailPage);

            _resetPasswordPage = cleanUrl(configuration.ResetPasswordPage);
            _resetPasswordSuccessPage = cleanUrl(configuration.ResetPasswordSuccessPage);
            _resetPasswordFailPage = cleanUrl(configuration.ResetPasswordFailPage);

            _verifyEmailPage = cleanUrl(configuration.VerifyEmailPage);
            _verifyEmailSuccessPage = cleanUrl(configuration.VerifyEmailSuccessPage);
            _verifyEmailFailPage = cleanUrl(configuration.VerifyEmailFailPage);

            _revertEmailPage = cleanUrl(configuration.RevertEmailPage);
            _revertEmailSuccessPage = cleanUrl(configuration.RevertEmailSuccessPage);
            _revertEmailFailPage = cleanUrl(configuration.RevertEmailFailPage);

            _renewSessionPage = cleanUrl(configuration.RenewSessionPage);
            _updateIdentityPage = cleanUrl(configuration.UpdateIdentityPage);

            _secureDomain = (configuration.SecureDomain ?? string.Empty).ToLower();
            _secureProtcol = (configuration.SecureProtocol ?? "https").ToLower();
            _cookieName = (configuration.CookieName ?? string.Empty).ToLower().Replace(' ', '-');
            _sessionIdentityName = (configuration.SessionIdentityName ?? string.Empty).ToLower().Replace(' ', '-');
            _sessionPurposeName = (configuration.SessionPurposeName ?? string.Empty).ToLower().Replace(' ', '-');
            _sessionStatusName = (configuration.SessionStatusName ?? string.Empty).ToLower().Replace(' ', '-');
            _sessionRememberMeName = (configuration.SessionRememberMeName ?? string.Empty).ToLower().Replace(' ', '-');
            _sessionClaimsName = (configuration.SessionClaimsName ?? string.Empty).ToLower().Replace(' ', '-');
            _sessionIsAnonymousName = (configuration.SessionIsAnonymousName ?? string.Empty).ToLower().Replace(' ', '-');
            _sessionMessageName = (configuration.SessionMessageName ?? string.Empty).ToLower().Replace(' ', '-');
            
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

            document = document.Replace("{changeEmailPage}", _changeEmailPage.ToString());
            document = document.Replace("{changeEmailSuccessPage}", _changeEmailSuccessPage.ToString());
            document = document.Replace("{changeEmailFailPage}", _changeEmailFailPage.ToString());

            document = document.Replace("{renewSessionPage}", _renewSessionPage.ToString());
            document = document.Replace("{updateIdentityPage}", _updateIdentityPage.ToString());

            document = document.Replace("{secureDomain}", _secureDomain);
            document = document.Replace("{secureProtocol}", _secureProtcol);
            document = document.Replace("{cookieName}", _cookieName);
            document = document.Replace("{sessionIdentityName}", _sessionIdentityName);
            document = document.Replace("{sessionPurposeName}", _sessionPurposeName);
            document = document.Replace("{sessionStatusName}", _sessionStatusName);
            document = document.Replace("{sessionRememberMeName}", _sessionRememberMeName);
            document = document.Replace("{sessionClaimsName}", _sessionClaimsName);
            document = document.Replace("{sessionAnonymousName}", _sessionIsAnonymousName);
            document = document.Replace("{sessionMessageName}", _sessionMessageName);

            document = document.Replace("{rememberMeFor}", _configuration.RememberMeFor.ToString());

            document = document.Replace("{emailFormField}", _configuration.EmailFormField);
            document = document.Replace("{newEmailFormField}", _configuration.NewEmailFormField);
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

            document = document.Replace("{changeEmailPage.default}", defaultConfiguration.ChangeEmailPage);
            document = document.Replace("{changeEmailSuccessPage.default}", defaultConfiguration.ChangeEmailSuccessPage);
            document = document.Replace("{changeEmailFailPage.default}", defaultConfiguration.ChangeEmailFailPage);

            document = document.Replace("{renewSessionPage.default}", defaultConfiguration.RenewSessionPage);
            document = document.Replace("{updateIdentityPage.default}", defaultConfiguration.UpdateIdentityPage);

            document = document.Replace("{secureDomain.default}", defaultConfiguration.SecureDomain);
            document = document.Replace("{secureProtocol.default}", defaultConfiguration.SecureProtocol);
            document = document.Replace("{cookieName.default}", defaultConfiguration.CookieName);
            document = document.Replace("{sessionIdentityName.default}", defaultConfiguration.SessionIdentityName);
            document = document.Replace("{sessionPurposeName.default}", defaultConfiguration.SessionPurposeName);
            document = document.Replace("{sessionStatusName.default}", defaultConfiguration.SessionStatusName);
            document = document.Replace("{sessionRememberMeName.default}", defaultConfiguration.SessionRememberMeName);
            document = document.Replace("{sessionClaimsName.default}", defaultConfiguration.SessionClaimsName);
            document = document.Replace("{sessionAnonymousName.default}", defaultConfiguration.SessionIsAnonymousName);

            document = document.Replace("{rememberMeFor.default}", defaultConfiguration.RememberMeFor.ToString());

            document = document.Replace("{emailFormField.default}", defaultConfiguration.EmailFormField);
            document = document.Replace("{newEmailFormField.default}", defaultConfiguration.NewEmailFormField);
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
                                        Name = _configuration.EmailFormField,
                                        Description = "The email address of the user wanting to create an account"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Form Variable",
                                        Name = _configuration.PasswordFormField,
                                        Description = "The password of the user trying to create an account"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Form Variable",
                                        Name = _configuration.RememberMeFormField,
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
                                        Name = _configuration.EmailFormField,
                                        Description = "The email address of the user trying to login"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Form Variable",
                                        Name = _configuration.PasswordFormField,
                                        Description = "The password of the user trying to login"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Form Variable",
                                        Name = _configuration.RememberMeFormField,
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
                                        Name = _configuration.EmailFormField,
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
                                        Name = _configuration.EmailFormField,
                                        Description = "The email address of the user who wants to reset their password"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Form Variable",
                                        Name = _configuration.TokenFormField,
                                        Description = "The password reset token that was included in the password reset email"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Form Variable",
                                        Name = _configuration.PasswordFormField,
                                        Description = "The new password to set for this user"
                                    }
                                }
                        });

                if (_changePasswordPage.HasValue)
                    documentation.Add(
                        new EndpointDocumentation
                        {
                            RelativePath = _changePasswordPage.Value,
                            Description = "Changes a user's password. The user must know their current password. Always POST to this endpoint over HTTPS",
                            Attributes = new List<IEndpointAttributeDocumentation>
                                {
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Method",
                                        Name = "POST",
                                        Description = "Changes the user's password"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Form Variable",
                                        Name = _configuration.EmailFormField,
                                        Description = "The email address of the user who wants to reset their password"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Form Variable",
                                        Name = _configuration.PasswordFormField,
                                        Description = "The user's current password"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Form Variable",
                                        Name = _configuration.NewPasswordFormField,
                                        Description = "The new password to set for this user"
                                    }
                                }
                        });

                if (_changeEmailPage.HasValue)
                    documentation.Add(
                        new EndpointDocumentation
                        {
                            RelativePath = _changeEmailPage.Value,
                            Description = "Changes a user's email address. The user must know their current password. Always POST to this endpoint over HTTPS",
                            Attributes = new List<IEndpointAttributeDocumentation>
                                {
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Method",
                                        Name = "POST",
                                        Description = "Changes the user's email address"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Form Variable",
                                        Name = _configuration.EmailFormField,
                                        Description = "The current email address of the user who wants to change their email address"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Form Variable",
                                        Name = _configuration.PasswordFormField,
                                        Description = "The password for this user"
                                    },
                                    new EndpointAttributeDocumentation
                                    {
                                        Type = "Form Variable",
                                        Name = _configuration.NewEmailFormField,
                                        Description = "The new email address to set for this user"
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
            var filePath = _hostingEnvironment.MapPath(filename);
            if (File.Exists(filePath))
            {
                using (var reader = File.OpenText(filePath))
                {
                    return reader.ReadToEnd();
                }
            }

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
