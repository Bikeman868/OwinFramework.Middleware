using System;
using System.IO;
using Ioc.Modules;
using Ninject;
using Owin;
using OwinFramework.AnalysisReporter;
using OwinFramework.Builder;
using OwinFramework.Configuration.Urchin;
using OwinFramework.Interfaces.Builder;
using OwinFramework.RouteVisualizer;
using Urchin.Client.Sources;

namespace TestServer
{
    // You can use this as a template for the Owin Statup class in your application.
    public class Startup
    {
        /// <summary>
        /// This is used to hold onto a reference to the Urchin file store. If the file
        /// store is disposed by the garbage collector then it will no longer notice
        /// changes in the configuration file.
        /// </summary>
        private static IDisposable _configurationFileSource;

        public void Configuration(IAppBuilder app)
        {
            var packageLocator = new PackageLocator().ProbeBinFolderAssemblies();
            var ninject = new StandardKernel(new Ioc.Modules.Ninject.Module(packageLocator));
            
            // Tell urchin to get its configuration from the config.json file in this project. Note that if
            // you edit this file whilst the application is running the changes will be applied without 
            // restarting the application.
            var configFile = new FileInfo(AppDomain.CurrentDomain.SetupInformation.ApplicationBase + "config.json");
            _configurationFileSource = ninject.Get<FileSource>().Initialize(configFile, TimeSpan.FromSeconds(5));

            // Construct an adapter between the Owin Framework and Urchin configuration management system
            var urchin = ninject.Get<UrchinConfiguration>();

            // We will use the Owin Framework builder to build the Owin pipeline
            var builder = ninject.Get<IBuilder>();

            // The route visualizer middleware will produce an SVG showing the Owin pipeline configuration
            builder.Register(ninject.Get<RouteVisualizer>())
                .As("RouteVisualizer")
                .ConfigureWith(urchin, "/middleware/visualizer")
                .RunFirst();

            // The route visualizer middleware will produce an SVG showing the Owin pipeline configuration
            builder.Register(ninject.Get<AnalysisReporter>())
                .As("AnalysisReporter")
                .ConfigureWith(urchin, "/middleware/analysis");

            // Tell Owin to add our Owin Framework middleware to the Owin pipeline
            app.UseBuilder(builder);
        }
    }
}
