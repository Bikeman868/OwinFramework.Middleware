using System;

namespace OwinFramework.NotFound
{
    [Serializable]
    internal class NotFoundConfiguration
    {
        public string DocumentationRootUrl { get; set; }
        public string Template { get; set; }

        public NotFoundConfiguration()
        {
            Template = "";
            DocumentationRootUrl = "/owin/notFound/config";
        }
    }
}
