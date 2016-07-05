using System;

namespace OwinFramework.Middleware
{
    [Serializable]
    internal class StaticFilesConfiguration
    {
        public string StaticFilesRootUrl { get; set; }
        public string DocumentationRootUrl { get; set; }
        public string RootDirectory { get; set; }
        public bool Enabled { get; set; }
        public bool IncludeSubFolders { get; set; }
        public string[] FileExtensions { get; set; }
        public long MaximumFileSizeToCache { get; set; }
        public TimeSpan MaximumCacheTime { get; set; }
        public long TotalCacheSize { get; set; }
        public string RequiredPermission { get; set; }

        public StaticFilesConfiguration()
        {
            StaticFilesRootUrl = "/assets";
            DocumentationRootUrl = "/owin/staticFiles/config";
            RootDirectory = "/assets";
            Enabled = true;
            IncludeSubFolders = true;
            FileExtensions = new[] 
            {
                ".bmp",
                ".jpg",
                ".png",
                ".html",
                ".css",
                ".js"
            };
            MaximumFileSizeToCache = 32 * 1024;
            MaximumCacheTime = TimeSpan.FromHours(1);
            TotalCacheSize = 50 * 1024 * 1024;
        }
    }
}
