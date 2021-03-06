﻿using System.Collections.Generic;
using Ioc.Modules;
using OwinFramework.Interfaces.Builder;
using OwinFramework.Interfaces.Utility;
using OwinFramework.InterfacesV1.Facilities;
using OwinFramework.InterfacesV1.Middleware;

namespace OwinFramework.FormIdentification
{
    [Package]
    internal class Package: IPackage
    {
        public string Name { get { return "OWIN Framework Form Identification middleware"; } }

        public IList<IocRegistration> IocRegistrations
        {
            get
            {
                return new List<IocRegistration>
                {
                    new IocRegistration().Init<IMiddleware<IIdentification>, FormIdentificationMiddleware>(),

                    new IocRegistration().Init<IIdentityStore>(),
                    new IocRegistration().Init<IIdentityDirectory>(),
                    new IocRegistration().Init<IHostingEnvironment>(),
                    new IocRegistration().Init<ITokenStore>()
                };
            }
        }

    }
}
