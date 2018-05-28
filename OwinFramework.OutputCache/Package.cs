using System.Collections.Generic;
using Ioc.Modules;
using OwinFramework.Interfaces.Builder;
using OwinFramework.Interfaces.Utility;
using OwinFramework.InterfacesV1.Middleware;

namespace OwinFramework.OutputCache
{
    [Package]
    internal class Package: IPackage
    {
        public string Name { get { return "OWIN Framework output cache middleware"; } }

        public IList<IocRegistration> IocRegistrations
        {
            get
            {
                return new List<IocRegistration>
                {
                    new IocRegistration().Init<IMiddleware<IOutputCache>, OutputCacheMiddleware>(),

                    new IocRegistration().Init<IHostingEnvironment>(),
                    new IocRegistration().Init<InterfacesV1.Facilities.ICache>()
                };
            }
        }

    }
}
