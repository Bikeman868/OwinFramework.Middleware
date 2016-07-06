using System;

namespace OwinFramework.DefaultDocument
{
    [Serializable]
    internal class DefaultDocumentConfiguration
    {
        public string DocumentationRootUrl { get; set; }
        public string DefaultPage { get; set; }

        public DefaultDocumentConfiguration()
        {
            DocumentationRootUrl = "/owin/defaultDocument/config";
            DefaultPage = "/index.html";
        }
    }
}
