using System;
using Newtonsoft.Json;

namespace OwinFramework.Dart
{
    internal class DartConfiguration
    {
        [JsonProperty("documentationRootUrl")]
        public string DocumentationRootUrl { get; set; }

        [JsonProperty("uiRootUrl")]
        public string UiRootUrl { get; set; }

        [JsonProperty("dartUiRootUrl")]
        public string DartUiRootUrl { get; set; }

        [JsonProperty("compiledUiRootUrl")]
        public string CompiledUiRootUrl { get; set; }

        [JsonProperty("defaultDocument")]
        public string DefaultDocument { get; set; }

        [JsonProperty("analyticsEnabled")]
        public bool AnalyticsEnabled { get; set; }

        public DartConfiguration()
        {
            DocumentationRootUrl = "/owin/dart/config";
            UiRootUrl = "/ui";
            DartUiRootUrl = "/ui/web";
            CompiledUiRootUrl = "/ui/build/web";
            DefaultDocument = "index.html";
            AnalyticsEnabled = true;
        }
    }
}
