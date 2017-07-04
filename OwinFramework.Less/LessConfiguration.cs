using System;

namespace OwinFramework.Less
{
    [Serializable]
    internal class LessConfiguration
    {
        public string RootUrl { get; set; }
        public string DocumentationRootUrl { get; set; }
        public string RootDirectory { get; set; }
        public bool Enabled { get; set; }
        public bool AnalyticsEnabled { get; set; }
        public bool Minify { get; set; }
        public bool TraceLog { get; set; }

        public LessConfiguration()
        {
            RootUrl = "/styles";
            DocumentationRootUrl = "/owin/less/config";
            RootDirectory = "~\\styles";
            Enabled = true;
            AnalyticsEnabled = true;
            Minify = false;
            TraceLog = false;
        }
    }
}
