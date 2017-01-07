using System;
using Newtonsoft.Json;

namespace OwinFramework.Dart
{
    internal class DartConfiguration
    {
        [JsonProperty("dartUiRootUrl")]
        public string DartUiRootUrl { get; set; }

        [JsonProperty("defaultDocument")]
        public string DefaultDocument { get; set; }

        [JsonProperty("documentationRootUrl")]
        public string DocumentationRootUrl { get; set; }

        [JsonProperty("rootDartDirectory")]
        public string RootDartDirectory { get; set; }

        [JsonProperty("rootBuildDirectory")]
        public string RootBuildDirectory { get; set; }

        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        [JsonProperty("fileExtensions")]
        public ExtensionConfiguration[] FileExtensions { get; set; }

        [JsonProperty("maximumFileSizeToCache")]
        public long MaximumFileSizeToCache { get; set; }

        [JsonProperty("maximumCacheTime")]
        public TimeSpan MaximumCacheTime { get; set; }

        [JsonProperty("requiredPermission")]
        public string RequiredPermission { get; set; }

        [JsonProperty("version")]
        public int? Version { get; set; }

        public DartConfiguration()
        {
            DartUiRootUrl = "/ui";
            DefaultDocument = "index.html";
            DocumentationRootUrl = "/owin/dart/config";
            RootDartDirectory = "~\\ui\\web";
            RootBuildDirectory = "~\\ui\\build\\web";
            Enabled = true;
#if !DEBUG
            Version = 1;
#endif

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
                new ExtensionConfiguration{Extension = ".css", MimeType = "text/css", Expiry = oneWeek, Processing = FileProcessing.Less},

                new ExtensionConfiguration{Extension = ".js", MimeType = "application/javascript", Expiry = oneHour, Processing = FileProcessing.JavaScript},
                new ExtensionConfiguration{Extension = ".dart", MimeType = "application/dart", Expiry = oneHour, Processing = FileProcessing.Dart}
            };

            MaximumFileSizeToCache = 32 * 1024;
            MaximumCacheTime = TimeSpan.FromHours(1);
        }
    }

    internal enum FileProcessing { None, Html, Css, Dart, JavaScript, Less }


    internal class ExtensionConfiguration
    {
        [JsonProperty("expiry")]
        public TimeSpan? Expiry { get; set; }

        [JsonProperty("extension")]
        public string Extension { get; set; }

        [JsonProperty("mimeType")]
        public string MimeType { get; set; }

        [JsonProperty("processing")]
        public FileProcessing Processing { get; set; }

        public ExtensionConfiguration()
        {
            Expiry = TimeSpan.FromDays(7);
            Processing = FileProcessing.None;
        }
    }
}
