using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Owin;
using OwinFramework.Builder;
using OwinFramework.Interfaces.Builder;
using OwinFramework.InterfacesV1.Middleware;

namespace OwinFramework.Middleware.TestServer.Middleware
{
    internal class LogUserInfoMiddleware: IMiddleware<object>
    {
        public string Name { get; set; }

        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        public IList<IDependency> Dependencies { get { return _dependencies; } }

        public LogUserInfoMiddleware()
        {
            this.RunAfter<IIdentification>();
        }

        public Task Invoke(IOwinContext context, Func<Task> next)
        {
            var output = "User identification for " + context.Request.Uri + ".";

            var identification = context.GetFeature<IIdentification>();
            if (identification != null)
            {
                if (identification.IsAnonymous)
                    output += " Anonymous user";
                else
                    output += " User " + identification.Identity;
                if (identification.Claims != null && identification.Claims.Count > 0)
                {
                    output += " claiming";
                    foreach (var claim in identification.Claims)
                        output += " " + claim.Name + "=" + claim.Value + "(" + claim.Status + ")";
                }
            }

            Console.WriteLine(output);

            return next();
        }

    }
}
