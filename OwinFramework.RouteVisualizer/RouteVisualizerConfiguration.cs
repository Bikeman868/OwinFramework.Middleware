using System;

namespace OwinFramework.RouteVisualizer
{
    [Serializable]
    internal class RouteVisualizerConfiguration
    {
        public string Path { get; set; }
        public bool Enabled { get; set; }
        public string RequiredPermission { get; set; }

        public RouteVisualizerConfiguration()
        {
            Path = "/owin/visualization";
            Enabled = true;
        }
    }
}
