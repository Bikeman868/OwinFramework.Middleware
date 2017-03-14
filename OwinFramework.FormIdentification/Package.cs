using System.Collections.Generic;
using Ioc.Modules;
using OwinFramework.Interfaces.Utility;
using OwinFramework.InterfacesV1.Facilities;

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
                    new IocRegistration().Init<IIdentityStore>(),
                    new IocRegistration().Init<IHostingEnvironment>(),
                    new IocRegistration().Init<ITokenStore>()
                };
            }
        }

    }
}
