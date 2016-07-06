using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Owin;
using OwinFramework.Builder;
using OwinFramework.Interfaces.Builder;
using OwinFramework.Interfaces.Routing;

namespace OwinFramework.DefaultDocument
{
    public class DefaultDocumentMiddleware:
        IMiddleware<object>,
        IRoutingProcessor,
        OwinFramework.InterfacesV1.Capability.IConfigurable
    {
        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        IList<IDependency> IMiddleware.Dependencies { get { return _dependencies; } }

        string IMiddleware.Name { get; set; }

        public DefaultDocumentMiddleware()
        {
            this.RunFirst();
        }

        public Task Invoke(IOwinContext context, Func<Task> next)
        {
            return next();
        }

        public Task RouteRequest(IOwinContext context, Func<Task> next)
        {
            if (!context.Request.Path.HasValue || context.Request.Path.Value == "/" || context.Request.Path.Value == "")
            {
                context.Request.Path = new PathString(_defaultDocument);
                // TODO: do we need to update all the related properties?
            }
            return next();
        }

        private IDisposable _configurationRegistration;
        private string _defaultDocument = "/index.html";

        public void Configure(IConfiguration configuration, string path)
        {
            _configurationRegistration = configuration.Register(
                path,
                cfg => 
                    {
                        if (string.IsNullOrEmpty(cfg))
                            _defaultDocument = "/";
                        else if (!cfg.StartsWith("/"))
                            _defaultDocument = "/" + cfg;
                        else
                            _defaultDocument = cfg;
                    },
                _defaultDocument);
        }
    }
}
