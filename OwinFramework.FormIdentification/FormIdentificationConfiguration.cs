using System;

namespace OwinFramework.FormIdentification
{
    [Serializable]
    internal class FormIdentificationConfiguration
    {
        public string SecureProtocol { get; set; }
        public string SecureDomain { get; set; }
        
        public string DocumentationPage { get; set; }

        public string SignupPage { get; set; }
        public string SignupSuccessPage { get; set; }
        public string SignupFailPage { get; set; }

        public string SigninPage { get; set; }
        public string SigninSuccessPage { get; set; }
        public string SigninFailPage { get; set; }

        public string SignoutPage { get; set; }
        public string SignoutSuccessPage { get; set; }

        public string ChangePasswordPage { get; set; }
        public string ChangePasswordSuccessPage { get; set; }
        public string ChangePasswordFailPage { get; set; }

        public string ChangeEmailPage { get; set; }
        public string ChangeEmailSuccessPage { get; set; }
        public string ChangeEmailFailPage { get; set; }

        public string SendPasswordResetPage { get; set; }
        public string SendPasswordResetSuccessPage { get; set; }
        public string SendPasswordResetFailPage { get; set; }

        public string ResetPasswordPage { get; set; }
        public string ResetPasswordSuccessPage { get; set; }
        public string ResetPasswordFailPage { get; set; }

        public string RenewSessionPage { get; set; }
        public string UpdateIdentityPage { get; set; }

        public string CookieName { get; set; }
        public string SessionIdentityName { get; set; }
        public string SessionPurposeName { get; set; }
        public string SessionStatusName { get; set; }
        public string SessionRememberMeName { get; set; }
        public string SessionEmailName { get; set; }

        public TimeSpan RememberMeFor { get; set; }

        public string EmailFormField { get; set; }
        public string PasswordFormField { get; set; }
        public string NewPasswordFormField { get; set; }
        public string RememberMeFormField { get; set; }
        public string TokenFormField { get; set; }
        public string NewEmailFormField { get; set; }

        public string PasswordResetTokenType { get; set; }
        public string PasswordResetEmailFrom { get; set; }
        public string PasswordResetEmailSubject { get; set; }

        public string EmailChangeEmailFrom { get; set; }
        public string EmailChangeEmailSubject { get; set; }
        
        public FormIdentificationConfiguration()
        {
            SecureDomain = string.Empty;
            SecureProtocol = "https";

            CookieName = "form-identification";
            SessionIdentityName = "form-identification-identity";
            SessionPurposeName = "form-identification-purpose";
            SessionStatusName = "form-identification-status";
            SessionRememberMeName = "form-identification-rememberme";
            SessionEmailName = "form-identification-email";

            RememberMeFor = TimeSpan.FromDays(90);

            DocumentationPage = "/formId/config";

            SignupPage = "/formId/signup";
            SignupSuccessPage = "/welcome";
            SignupFailPage = "/formId/signup";

            SigninPage = "/formId/signin";
            SigninSuccessPage = "/";
            SigninFailPage = "/formId/signin";

            SignoutPage = "/formId/signout";
            SignoutSuccessPage = "/";

            ChangeEmailPage = "/formId/changeEmail";
            ChangeEmailSuccessPage = "/";
            ChangeEmailFailPage = "/formId/changeEmail";

            ChangePasswordPage = "/formId/changePassword";
            ChangePasswordSuccessPage = "/";
            ChangePasswordFailPage = "/formId/changePassword";

            SendPasswordResetPage = "/formId/sendPasswordReset";
            SendPasswordResetSuccessPage = "/";
            SendPasswordResetFailPage = "/formId/sendPasswordReset";

            ResetPasswordPage = "/formId/resetPassword";
            ResetPasswordSuccessPage = "/";
            ResetPasswordFailPage = "/formId/resetPassword";

            RenewSessionPage = "/formId/renew";
            UpdateIdentityPage = "/formId/update";

            EmailFormField = "email";
            PasswordFormField = "password";
            NewPasswordFormField = "newPassword";
            RememberMeFormField = "rememberMe";
            TokenFormField = "token";
            NewEmailFormField = "newEmail";

            PasswordResetTokenType = "PasswordResetl";
            PasswordResetEmailFrom = "";
            PasswordResetEmailSubject = "Reset your password";

            EmailChangeEmailFrom = "";
            EmailChangeEmailSubject = "Email address updated";
        }
    }
}
