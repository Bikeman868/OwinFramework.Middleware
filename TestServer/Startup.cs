using System;
using System.IO;
using Ioc.Modules;
using Ninject;
using Owin;
using OwinFramework.Builder;
using OwinFramework.Interfaces.Builder;
using Urchin.Client.Sources;

namespace OwinFramework.Middleware.TestServer
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

            // Construct the configuration implementation that is registered with IoC (Urchin)
            var config = ninject.Get<IConfiguration>();

            // Get the Owin Framework builder registered with IoC
            var builder = ninject.Get<IBuilder>();

            // The static files middleware will allow remote clients to retrieve files from within this project
            // Configuration options limit the files that can be retrieved this way.
            builder.Register(ninject.Get<StaticFiles.StaticFilesMiddleware>())
                .As("Static files")
                .ConfigureWith(config, "/middleware/staticFiles")
                .RunAfter("LESS compiler");

            // The Less middleware will compile LESS into CSS on the fly
            builder.Register(ninject.Get<Less.LessMiddleware>())
                .As("LESS compiler")
                .ConfigureWith(config, "/middleware/less");

            // The route visualizer middleware will produce an SVG showing the Owin pipeline configuration
            builder.Register(ninject.Get<RouteVisualizer.RouteVisualizerMiddleware>())
                .As("Route visualizer")
                .ConfigureWith(config, "/middleware/visualizer");

            // The default document middleware will rewrite a request for the root document to an actual page on the site
            builder.Register(ninject.Get<DefaultDocument.DefaultDocumentMiddleware>())
                .As("Default document")
                .ConfigureWith(config, "/middleware/defaultDocument");

            // The default document middleware will rewrite a request for the root document to an actual page on the site
            //builder.Register(ninject.Get<NotFound.NotFoundMiddleware>())
            //    .As("Not found")
            //    .ConfigureWith(config, "/middleware/notFound");

            // The route visualizer middleware will produce an SVG showing the Owin pipeline configuration
            builder.Register(ninject.Get<AnalysisReporter.AnalysisReporterMiddleware>())
                .As("Analysis reporter")
                .ConfigureWith(config, "/middleware/analysis");

            // The route visualizer middleware will produce an SVG showing the Owin pipeline configuration
            builder.Register(ninject.Get<Documenter.DocumenterMiddleware>())
                .As("Documenter")
                .ConfigureWith(config, "/middleware/documenter");

            // The exception reporter middleware will catch exceptions and produce diagnostic output
            builder.Register(ninject.Get<ExceptionReporter.ExceptionReporterMiddleware>())
                .As("Exception reporter")
                .ConfigureWith(config, "/middleware/exceptions")
                .RunFirst();

            // The exception generator middleware will throw exceptions so that you can test the handler
            builder.Register(ninject.Get<ExceptionReporter.ExceptionGeneratorMiddleware>())
                .As("Exception generator")
                .RunLast();

            // Tell Owin to add our Owin Framework middleware to the Owin pipeline
            app.UseBuilder(builder);
        }
    }
}
