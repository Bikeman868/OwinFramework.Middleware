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
                new ExtensionConfiguration{Extension = ".bmp", MimeType = "image/bmp"},
                new ExtensionConfiguration{Extension = ".jpg", MimeType = "image/jpeg"},
                new ExtensionConfiguration{Extension = ".png", MimeType = "image/png"},
                new ExtensionConfiguration{Extension = ".html", MimeType = "text/html"},
                new ExtensionConfiguration{Extension = ".htm", MimeType = "text/html"},
                new ExtensionConfiguration{Extension = ".css", MimeType = "text/css"},
                new ExtensionConfiguration{Extension = ".txt", MimeType = "text/plain"},
                new ExtensionConfiguration{Extension = ".js", MimeType = "application/javascript"}
            };
            MaximumFileSizeToCache = 32 * 1024;
            MaximumCacheTime = TimeSpan.FromHours(1);
        }
    }

    internal class ExtensionConfiguration
    {
        public string Extension { get; set; }
        public string MimeType { get; set; }
    }
}
