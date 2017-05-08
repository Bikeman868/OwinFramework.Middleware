using System;
using System.IO;
using System.Net.Mail;
using System.Reflection;
using Ioc.Modules;
using Ninject;
using Owin;
using OwinFramework.Builder;
using OwinFramework.Interfaces.Builder;
using OwinFramework.Interfaces.Utility;
using OwinFramework.InterfacesV1.Middleware;
using OwinFramework.Middleware.TestServer.Middleware;
using OwinFramework.Middleware.TestServer.Prius;
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
            // By explicitly adding this assembly to the package locator, any IoC mappings
            // in this assembly will take priority over assemblies found through probing.
            // Also when the package locator probes for assemblies it only looks for DLLs
            // and not executables.
            var packageLocator = new PackageLocator()
                .ProbeBinFolderAssemblies()
                .Add(Assembly.GetExecutingAssembly());

            // Construct the Ninject IoC container and configure it using information from 
            // the package locator
            var ninject = new StandardKernel(new Ioc.Modules.Ninject.Module(packageLocator));

            // The prius factory needs Ninject to resolve interfaces to concrete types
            PriusFactory.Ninject = ninject;
            
            // Tell urchin to get its configuration from the config.json file in this project. Note that if
            // you edit this file whilst the application is running the changes will be applied without 
            // restarting the application.
            var hostingEnvironment = ninject.Get<IHostingEnvironment>();
            var configFile = new FileInfo(hostingEnvironment.MapPath("config.json"));
            _configurationFileSource = ninject.Get<FileSource>().Initialize(configFile, TimeSpan.FromSeconds(5));

            // Construct the configuration mechanism that is registered with IoC (Urchin)
            var config = ninject.Get<IConfiguration>();

            // Get the Owin Framework builder registered with IoC
            var builder = ninject.Get<IBuilder>();

            // Output caching just makes the web site more efficient by capturing the output from
            // downstream middleware and reusing it for the next request. Note that the Dart middleware
            // produces different results for the same URL and therefore can not use output caching
            builder.Register(ninject.Get<OutputCache.OutputCacheMiddleware>())
                .As("Output cache")
                .ConfigureWith(config, "/middleware/outputCache")
                .RunAfter("Dart");

            // The Versioning middleware will add version numbers to static assets and
            // instruct the browser to cache them
            builder.Register(ninject.Get<Versioning.VersioningMiddleware>())
                .As("Versioning")
                .ConfigureWith(config, "/middleware/versioning")
                .RunAfter<IOutputCache>();

            // The dart middleware will allow the example Dart UI to run in browsers that support Dart
            // natively as well as supplying compiled JavaScript to browsers that dont support Dart natively.
            builder.Register(ninject.Get<Dart.DartMiddleware>())
                .As("Dart")
                .ConfigureWith(config, "/middleware/dart");

            // The Less middleware will compile LESS into CSS on the fly
            builder.Register(ninject.Get<Less.LessMiddleware>())
                .As("LESS compiler")
                .ConfigureWith(config, "/middleware/less")
                .RunAfter("Dart");
                
            // The static files middleware will allow requests to retrieve files of certian types
            // Configuration options limit the files that can be retrieved this way. The ConfigureWith
            // fluid method below specifies the location of this configuration in the config.json file
            builder.Register(ninject.Get<StaticFiles.StaticFilesMiddleware>())
                .As("Static files")
                .ConfigureWith(config, "/middleware/staticFiles")
                .RunAfter("LESS compiler")
                .RunAfter("Dart");

            // The form identification middleware provides login/logout password reset etc
            builder.Register(ninject.Get<FormIdentification.FormIdentificationMiddleware>())
                .As("Form Identification")
                .ConfigureWith(config, "/middleware/formIdentification")
                .RunAfter("Static files");

            // This middleware prints information about the logged on user to the console
            // for testing and debugging purposes
            builder.Register(ninject.Get<LogUserInfoMiddleware>())
                .As("Log user info");

            // The cache session middleware provides session management using the cache facility
            builder.Register(ninject.Get<Session.CacheSessionMiddleware>())
                .As("Cache session")
                .ConfigureWith(config, "/middleware/session")
                .RunAfter("Static files");

            // The route visualizer middleware will produce an SVG showing the Owin pipeline configuration
            builder.Register(ninject.Get<RouteVisualizer.RouteVisualizerMiddleware>())
                .As("Route visualizer")
                .ConfigureWith(config, "/middleware/visualizer")
                .RunAfter<IIdentification>();

            // The default document middleware will rewrite a request for the root document to a page on the site
            builder.Register(ninject.Get<DefaultDocument.DefaultDocumentMiddleware>())
                .As("Default document")
                .ConfigureWith(config, "/middleware/defaultDocument");

            // The not found middleware will always return a 404 response. Configure it to run after all
            // other middleware to catch requests that no other middleware handled
            builder.Register(ninject.Get<NotFound.NotFoundMiddleware>())
                .As("Not found")
                .ConfigureWith(config, "/middleware/notFound")
                .RunLast();

            // The analysis reporter middleware will format analytics from middleware that supports
            // this feature. The middleware can produce reports in various formats including
            // HTML, plain text and JSON. Analytics are things like the number of requests processed
            // per second and the average time taken to handle a request.
            builder.Register(ninject.Get<AnalysisReporter.AnalysisReporterMiddleware>())
                .As("Analysis reporter")
                .ConfigureWith(config, "/middleware/analysis")
                .RunAfter<IIdentification>();

            // The documenter middleware will extract documentation from middleware that is 
            // self documenting. If your web site is an API this is a more convenient way of
            // documenting the API than maintaining separate documentation, and the documentation
            // is always applicable to the specific version you are calling.
            builder.Register(ninject.Get<Documenter.DocumenterMiddleware>())
                .As("Documenter")
                .ConfigureWith(config, "/middleware/documenter")
                .RunAfter<IIdentification>();

            // The exception reporter middleware will catch exceptions and produce diagnostic output
            builder.Register(ninject.Get<ExceptionReporter.ExceptionReporterMiddleware>())
                .As("Exception reporter")
                .ConfigureWith(config, "/middleware/exceptions")
                .RunFirst();

            // The exception generator middleware will throw exceptions so that you can test the handler
            builder.Register(ninject.Get<ExceptionReporter.ExceptionGeneratorMiddleware>())
                .As("Exception generator")
                .RunLast();

            // Use the Owin Framework builder to add middleware to the Owin pipeline
            app.UseBuilder(builder);
        }
    }
}
