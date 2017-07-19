using System;

namespace OwinFramework.StaticFiles
{
    [Serializable]
    internal class StaticFilesConfiguration
    {
        public string StaticFilesRootUrl { get; set; }
        public string DocumentationRootUrl { get; set; }
        public string RootDirectory { get; set; }
        public bool Enabled { get; set; }
        public bool AnalyticsEnabled { get; set; }
        public bool IncludeSubFolders { get; set; }
        public ExtensionConfiguration[] FileExtensions { get; set; }
        public long MaximumFileSizeToCache { get; set; }
        public TimeSpan MaximumCacheTime { get; set; }
        public string RequiredPermission { get; set; }

        public StaticFilesConfiguration()
        {
            StaticFilesRootUrl = "/assets";
            DocumentationRootUrl = "/owin/staticFiles/config";
            RootDirectory = "~\\assets";
            Enabled = true;
            AnalyticsEnabled = true;
            IncludeSubFolders = true;
            FileExtensions = new [] 
            {
                new ExtensionConfiguration{Extension = ".bmp", MimeType = "image/bmp", IsText = false },
                new ExtensionConfiguration{Extension = ".jpg", MimeType = "image/jpeg", IsText = false },
                new ExtensionConfiguration{Extension = ".png", MimeType = "image/png", IsText = false },
                new ExtensionConfiguration{Extension = ".gif", MimeType = "image/gif", IsText = false },
                new ExtensionConfiguration{Extension = ".html", MimeType = "text/html", IsText = true },
                new ExtensionConfiguration{Extension = ".htm", MimeType = "text/html", IsText = true },
                new ExtensionConfiguration{Extension = ".css", MimeType = "text/css", IsText = true },
                new ExtensionConfiguration{Extension = ".txt", MimeType = "text/plain", IsText = true },
                new ExtensionConfiguration{Extension = ".js", MimeType = "application/javascript", IsText = true }
            };
            MaximumFileSizeToCache = 32 * 1024;
            MaximumCacheTime = TimeSpan.FromHours(1);
        }
    }

    internal class ExtensionConfiguration
    {
        public string Extension { get; set; }
        public string MimeType { get; set; }
        public bool IsText { get; set; }
    }
}
