using System;

namespace OwinFramework.Middleware
{
    [Serializable]
    public class AnalysisReporterConfiguration
    {
        public string Path { get; set; }
        public bool Enabled { get; set; }
        public string RequiredPermission { get; set; }
        public string DefaultFormat { get; set; }

        public AnalysisReporterConfiguration()
        {
            Path = "/owin/analytics";
            Enabled = true;
            DefaultFormat = "application/json";
        }
    }
}
