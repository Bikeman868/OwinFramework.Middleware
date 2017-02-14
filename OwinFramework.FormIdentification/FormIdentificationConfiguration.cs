using System;

namespace OwinFramework.NotFound
{
    [Serializable]
    internal class FormIdentificationConfiguration
    {
        public string SecureDomain { get; set; }
        
        public string DocumentationPage { get; set; }

        public string SignupPage { get; set; }
        public string SigninPage { get; set; }
        public string SignoutPage { get; set; }
        public string SignupSuccessPage { get; set; }
        public string SignupFailPage { get; set; }
        public string SigninSuccessPage { get; set; }
        public string SigninFailPage { get; set; }
        public string SignoutSuccessPage { get; set; }
        public string SendPasswordResetPage { get; set; }
        public string ResetPasswordPage { get; set; }
        public string RenewSessionPage { get; set; }
        public string ClearSessionPage { get; set; }

        public string CookieName { get; set; }
        public string SessionName { get; set; }
        
        public FormIdentificationConfiguration()
        {
            SecureDomain = string.Empty;
            CookieName = "forms_user_identification";
            SessionName = "forms_user_identification";

            DocumentationPage = "/formId/config";
            SignupPage = "/formId/signup";
            SigninPage = "/formId/signin";
            SignoutPage = "/formId/signout";
            SignupSuccessPage = "/welcome";
            SignupFailPage = "/formId/signup";
            SigninSuccessPage = "/";
            SigninFailPage = "/formId/signin";
            SignoutSuccessPage = "/";
            SendPasswordResetPage = "/formId/sendPasswordReset";
            ResetPasswordPage = "/formId/resetPassword";
            RenewSessionPage = "/formId/renew";
            ClearSessionPage = "/formId/clear";
        }
    }
}
