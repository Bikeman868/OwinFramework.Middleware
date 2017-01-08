﻿using System;
using System.IO;
using System.Reflection;
using Ioc.Modules;
using Ninject;
using Owin;
using OwinFramework.Builder;
using OwinFramework.Interfaces.Builder;
using OwinFramework.Interfaces.Utility;
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
            var packageLocator = new PackageLocator()
                .ProbeBinFolderAssemblies()
                .Add(Assembly.GetExecutingAssembly());
            var ninject = new StandardKernel(new Ioc.Modules.Ninject.Module(packageLocator));
            
            // Tell urchin to get its configuration from the config.json file in this project. Note that if
            // you edit this file whilst the application is running the changes will be applied without 
            // restarting the application.
            var hostingEnvironment = ninject.Get<IHostingEnvironment>();
            var configFile = new FileInfo(hostingEnvironment.MapPath("config.json"));
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
                .RunAfter("LESS compiler")
                .RunAfter("Dart");

            // The Less middleware will compile LESS into CSS on the fly
            builder.Register(ninject.Get<Less.LessMiddleware>())
                .As("LESS compiler")
                .ConfigureWith(config, "/middleware/less")
                .RunAfter("Dart");
                
            // The dart middleware will allow the example Dart UI to run in browsers that support Dart
            // natively as well as those that do not.
            builder.Register(ninject.Get<Dart.DartMiddleware>())
                .As("Dart")
                .ConfigureWith(config, "/middleware/dart");

            // The dart middleware will allow the example Dart UI to run in browsers that support Dart
            // natively as well as those that do not.
            builder.Register(ninject.Get<OutputCache.OutputCacheMiddleware>())
                .As("Output cache")
                .ConfigureWith(config, "/middleware/outputCache");

            // The route visualizer middleware will produce an SVG showing the Owin pipeline configuration
            builder.Register(ninject.Get<RouteVisualizer.RouteVisualizerMiddleware>())
                .As("Route visualizer")
                .ConfigureWith(config, "/middleware/visualizer");

            // The default document middleware will rewrite a request for the root document to an actual page on the site
            builder.Register(ninject.Get<DefaultDocument.DefaultDocumentMiddleware>())
                .As("Default document")
                .ConfigureWith(config, "/middleware/defaultDocument");

            // The not found middleware will always return a 404 response. Configure it to run after all
            // other middleware to catch requests that no other middleware handled
            //builder.Register(ninject.Get<NotFound.NotFoundMiddleware>())
            //    .As("Not found")
            //    .ConfigureWith(config, "/middleware/notFound")
            //    .RunLast();

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
