using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace OwinFramework.OutputCache
{
    [Serializable]
    internal class OutputCacheConfiguration
    {
        [JsonProperty("maximumTotalMemory")]
        public long MaximumTotalMemory { get; set; }

        [JsonProperty("fileTypes")]
        public FileTypes FileTypes { get; set; }

        [JsonProperty("maximumCacheTime")]
        public TimeSpan? MaximumCacheTime { get; set; }

        [JsonProperty("urls")]
        public UrlPatternConfiguration[] Urls { get; set; }

        public OutputCacheConfiguration()
        {
            MaximumTotalMemory = 1000 * 1024 * 1024;
            FileTypes = new FileTypes();
            MaximumCacheTime = TimeSpan.FromHours(1);
            Urls = new[]{ new UrlPatternConfiguration()};
        }
    }

    [Serializable]
    internal class UrlPatternConfiguration
    {
        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("includeSubPaths")]
        public bool IncludeSubPaths { get; set; }

        [JsonProperty("maximumTotalMemory")]
        public long? MaximumTotalMemory { get; set; }

        [JsonProperty("fileTypes")]
        public FileTypes FileTypes { get; set; }

        [JsonProperty("maximumCacheTime")]
        public TimeSpan? MaximumCacheTime { get; set; }

        [JsonProperty("maximumFileSizeToCache")]
        public long? MaximumFileSizeToCache { get; set; }

        public UrlPatternConfiguration()
        {
            Path = "/";
            IncludeSubPaths = true;
            FileTypes = new FileTypes 
            {
                FileExtensionsToInclude = new[]{".html", ".htm", ".ico", ".jpg", ".bmp", ".png", ".css", ".js"},
                FileExtensionsToExclude = new[]{".aspx"}
            };
            MaximumTotalMemory = null;
            MaximumFileSizeToCache = 50 * 1024 * 1024;
            MaximumCacheTime = null;
        }
    }

    internal class FileTypes
    {
        [JsonProperty("")]
        public string[] FileExtensionsToInclude { get; set; }

        [JsonProperty("")]
        public string[] FileExtensionsToExclude { get; set; }

        public FileTypes()
        {
            FileExtensionsToInclude = new string[0];
            FileExtensionsToExclude = new string[0];
        }
    }

}
