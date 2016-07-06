using System;

namespace OwinFramework.ExceptionReporter
{
    [Serializable]
    internal class ExceptionReporterConfiguration
    {
        public string Message { get; set; }
        public string Template { get; set; }
        public string RequiredPermission { get; set; }
        public bool Localhost { get; set; }
        public string EmailAddress { get; set; }
        public string EmailSubject { get; set; }

        public ExceptionReporterConfiguration()
        {
            Localhost = true;
            EmailSubject = "Unhandled exception in web site";
        }
    }
}
