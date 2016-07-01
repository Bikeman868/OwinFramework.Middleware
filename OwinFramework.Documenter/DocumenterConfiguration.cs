using System;

namespace OwinFramework.Middleware
{
    [Serializable]
    public class DocumenterConfiguration
    {
        public string Path { get; set; }
        public bool Enabled { get; set; }
        public string RequiredPermission { get; set; }

        public DocumenterConfiguration()
        {
            Path = "/owin/analytics";
            Enabled = true;
        }
    }
}
