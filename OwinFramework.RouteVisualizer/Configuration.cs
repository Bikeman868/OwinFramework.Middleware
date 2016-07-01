using System;

namespace OwinFramework.RouteVisualizer
{
    [Serializable]
    public class Configuration
    {
        public string Path { get; set; }
        public bool Enabled { get; set; }
        public string RequiredPermission { get; set; }

        public Configuration()
        {
            Path = "/owin/visualization";
            Enabled = true;
        }
    }
}
