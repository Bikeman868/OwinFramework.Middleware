using System;
using System.IO;
using System.Reflection;
using OwinFramework.Interfaces.Utility;

namespace OwinFramework.Middleware.TestServer
{
    /// <summary>
    /// The standard hosting environment implementation in the Owin Framework
    /// will map physical files relative to the bin/debug folder for console
    /// apps. This might be what you want for some console apps, but this
    /// scheme doesn't work well for this test application.
    /// </summary>
    internal class HostingEnvironment : IHostingEnvironment
    {
        private readonly string _webSitePath;

        public HostingEnvironment()
        {
            var codeBase = Path.GetDirectoryName(Assembly.GetExecutingAssembly().GetName().CodeBase);
            var localPath = new Uri(codeBase).LocalPath;

            while (!localPath.EndsWith("\\bin"))
            {
                var lastSeparator = localPath.LastIndexOf('\\');
                localPath = localPath.Substring(0, lastSeparator);
            }
            _webSitePath = localPath.Substring(0, localPath.Length - 4);
        }

        string IHostingEnvironment.MapPath(string path)
        {
            if (string.IsNullOrEmpty(path)) 
                return string.Empty;

            if (Path.IsPathRooted(path))
                return path;

            if (path.StartsWith("~\\"))
                path = path.Substring(2);

            return Path.Combine(_webSitePath, path);
        }
    }
}
