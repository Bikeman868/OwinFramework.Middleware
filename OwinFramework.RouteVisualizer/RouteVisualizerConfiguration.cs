using System;
using Newtonsoft.Json;

namespace OwinFramework.RouteVisualizer
{
    [Serializable]
    internal class RouteVisualizerConfiguration
    {
        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        [JsonProperty("requiredPermission")]
        public string RequiredPermission { get; set; }

        [JsonProperty("analyticsEnabled")]
        public bool AnalyticsEnabled { get; set; }
        
        [JsonProperty("documentationRootUrl")]
        public string DocumentationRootUrl { get; set; }

        public RouteVisualizerConfiguration()
        {
            Path = "/owin/visualization";
            DocumentationRootUrl = Path + "/docs/configuration";
            Enabled = true;
        }
    }
}
