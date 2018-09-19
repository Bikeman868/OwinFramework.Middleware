using System;
using System.Collections.Generic;
using Microsoft.Owin;
using Newtonsoft.Json;

namespace OwinFramework.DefaultDocument
{
    [Serializable]
    internal class DefaultDocumentConfiguration
    {
        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        [JsonProperty("documentationRootUrl")]
        public string DocumentationRootUrl { get; set; }

        [JsonProperty("defaultPage")]
        public string DefaultPage { get; set; }

        [JsonProperty("paths")]
        public List<DefaultFolderPath> DefaultFolderPaths { get; set; }

        [JsonIgnore]
        public PathString DocumentationRootUrlString { get; set; }

        [JsonIgnore]
        public PathString DefaultPageString { get; set; }

        public DefaultDocumentConfiguration()
        {
            Enabled = true;
            DocumentationRootUrl = "/owin/defaultDocument/config";
            DefaultPage = "/index.html";

            Sanitize();
        }

        public DefaultDocumentConfiguration Sanitize()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(DocumentationRootUrl))
                {
                    DocumentationRootUrlString = new PathString();
                }
                else
                {
                    if (!DocumentationRootUrl.StartsWith("/"))
                        DocumentationRootUrl = "/" + DocumentationRootUrl;

                    DocumentationRootUrlString = new PathString(DocumentationRootUrl);
                }

                if (string.IsNullOrWhiteSpace(DefaultPage))
                {
                    DocumentationRootUrlString = new PathString();
                }
                else
                {
                    if (!DefaultPage.StartsWith("/"))
                        DefaultPage = "/" + DefaultPage;

                    DefaultPageString = new PathString(DefaultPage);
                }
            }
            catch
            {
                Enabled = false;
                throw;
            }

            if (DefaultFolderPaths != null)
            {
                for (var i = 0; i < DefaultFolderPaths.Count; i++)
                {
                    var path = DefaultFolderPaths[i];
                    try
                    {
                        path.FolderPathString = new PathString(path.FolderPath);
                        path.DefaultPageString = new PathString(path.DefaultPage);
                    }
                    catch
                    {
                        DefaultFolderPaths.RemoveAt(i);
                        i--;
                    }
                }
                if (DefaultFolderPaths.Count == 0)
                    DefaultFolderPaths = null;
            }
            return this;
        }
    }

    [Serializable]
    internal class DefaultFolderPath
    {
        [JsonProperty("path")]
        public string FolderPath { get; set; }

        [JsonProperty("defaultPage")]
        public string DefaultPage { get; set; }

        [JsonIgnore]
        public PathString FolderPathString { get; set; }

        [JsonIgnore]
        public PathString DefaultPageString { get; set; }
    }
}
