using System;

namespace OwinFramework.Documenter
{
    [Serializable]
    internal class DocumenterConfiguration
    {
        public string Path { get; set; }
        public bool Enabled { get; set; }
        public string RequiredPermission { get; set; }
        public string LocalFilePath { get; set; }

        public DocumenterConfiguration()
        {
            Path = "/owin/endpoints";
            Enabled = true;
            LocalFilePath = string.Empty;
        }
    }
}
