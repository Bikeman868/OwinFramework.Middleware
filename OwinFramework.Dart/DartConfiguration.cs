using System;

namespace OwinFramework.Dart
{
    [Serializable]
    internal class DartConfiguration
    {
        public string DartUiRootUrl { get; set; }
        public string DefaultDocument { get; set; }
        public string DocumentationRootUrl { get; set; }
        public string RootDartDirectory { get; set; }
        public string RootBuildDirectory { get; set; }
        public bool Enabled { get; set; }
        public ExtensionConfiguration[] FileExtensions { get; set; }
        public long MaximumFileSizeToCache { get; set; }
        public TimeSpan MaximumCacheTime { get; set; }
        public long TotalCacheSize { get; set; }
        public string RequiredPermission { get; set; }
        public int Version { get; set; }

        public DartConfiguration()
        {
            DartUiRootUrl = "/ui";
            DefaultDocument = "index.html";
            DocumentationRootUrl = "/owin/dart/config";
            RootDartDirectory = "~\\ui\\web";
            RootBuildDirectory = "~\\ui\\build\\web";
            Enabled = true;
            Version = 1;

            var oneWeek = TimeSpan.FromDays(7);
            var oneHour = TimeSpan.FromHours(1);

            FileExtensions = new [] 
            {
                new ExtensionConfiguration{Extension = ".bmp", MimeType = "image/bmp", Expiry = oneWeek},
                new ExtensionConfiguration{Extension = ".ico", MimeType = "image/ico", Expiry = oneWeek},
                new ExtensionConfiguration{Extension = ".jpg", MimeType = "image/jpeg", Expiry = oneWeek},
                new ExtensionConfiguration{Extension = ".jpeg", MimeType = "image/jpeg", Expiry = oneWeek},
                new ExtensionConfiguration{Extension = ".jfif", MimeType = "image/jpeg", Expiry = oneWeek},
                new ExtensionConfiguration{Extension = ".png", MimeType = "image/png", Expiry = oneWeek},
                new ExtensionConfiguration{Extension = ".tif", MimeType = "image/tif", Expiry = oneWeek},
                new ExtensionConfiguration{Extension = ".tiff", MimeType = "image/tif", Expiry = oneWeek},

                new ExtensionConfiguration{Extension = ".avi", MimeType = "video/avi", Expiry = oneWeek},
                new ExtensionConfiguration{Extension = ".mov", MimeType = "video/quicktime", Expiry = oneWeek},
                new ExtensionConfiguration{Extension = ".mp3", MimeType = "video/mpeg", Expiry = oneWeek},
                new ExtensionConfiguration{Extension = ".mp4", MimeType = "video/mpeg", Expiry = oneWeek},

                new ExtensionConfiguration{Extension = ".html", MimeType = "text/html", Expiry = oneWeek, Processing = FileProcessing.Html},
                new ExtensionConfiguration{Extension = ".htm", MimeType = "text/html", Expiry = oneWeek, Processing = FileProcessing.Html},
                new ExtensionConfiguration{Extension = ".shtml", MimeType = "text/html", Expiry = oneWeek, Processing = FileProcessing.Html},
                new ExtensionConfiguration{Extension = ".txt", MimeType = "text/plain", Expiry = oneHour},
                new ExtensionConfiguration{Extension = ".css", MimeType = "text/css", Expiry = oneWeek, Processing = FileProcessing.Css},

                new ExtensionConfiguration{Extension = ".js", MimeType = "application/javascript", Expiry = oneHour, Processing = FileProcessing.JavaScript},
                new ExtensionConfiguration{Extension = ".dart", MimeType = "application/dart", Expiry = oneHour, Processing = FileProcessing.Dart}
            };

            MaximumFileSizeToCache = 32 * 1024;
            MaximumCacheTime = TimeSpan.FromHours(1);
            TotalCacheSize = 50 * 1024 * 1024;
        }
    }

    internal enum FileProcessing { None, Html, Css, Dart, JavaScript }


    internal class ExtensionConfiguration
    {
        public TimeSpan? Expiry { get; set; }
        public string Extension { get; set; }
        public string MimeType { get; set; }
        public FileProcessing Processing { get; set; }

        public ExtensionConfiguration()
        {
            Expiry = TimeSpan.FromDays(7);
            Processing = FileProcessing.None;
        }
    }
}
