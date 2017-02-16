using System;

namespace OwinFramework.NotFound
{
    [Serializable]
    internal class FormIdentificationConfiguration
    {
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

        public string SendPasswordResetPage { get; set; }
        public string SendPasswordResetSuccessPage { get; set; }
        public string SendPasswordResetFailPage { get; set; }

        public string ResetPasswordPage { get; set; }
        public string ResetPasswordSuccessPage { get; set; }
        public string ResetPasswordFailPage { get; set; }

        public string RenewSessionPage { get; set; }
        public string ClearSessionPage { get; set; }

        public string CookieName { get; set; }
        public string SessionName { get; set; }
        public TimeSpan RememberMeFor { get; set; }

        public string EmailFormField { get; set; }
        public string PasswordFormField { get; set; }
        public string NewPasswordFormField { get; set; }
        public string RememberMeFormField { get; set; }
        public string TokenFormField { get; set; }
        
        public FormIdentificationConfiguration()
        {
            SecureDomain = string.Empty;
            CookieName = "form-identification-identity";
            SessionName = "form-identification-identity";
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
            ClearSessionPage = "/formId/clear";

            EmailFormField = "email";
            PasswordFormField = "password";
            NewPasswordFormField = "newPassword";
            RememberMeFormField = "rememberMe";
            TokenFormField = "token";
        }
    }
}
