using System.Collections.Generic;
using Ioc.Modules;
using OwinFramework.Interfaces.Builder;
using OwinFramework.Interfaces.Utility;
using OwinFramework.InterfacesV1.Facilities;
using OwinFramework.InterfacesV1.Middleware;
using OwinFramework.Session;

namespace OwinFramework.FormIdentification
{
    [Package]
    internal class Package: IPackage
    {
        public string Name { get { return "OWIN Framework session middleware"; } }

        public IList<IocRegistration> IocRegistrations
        {
            get
            {
                return new List<IocRegistration>
                {
                    new IocRegistration().Init<IMiddleware<ISession>, CacheSessionMiddleware>(),

                    new IocRegistration().Init<ICache>()
                };
            }
        }

    }
}
