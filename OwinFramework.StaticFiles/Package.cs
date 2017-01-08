using System.Collections.Generic;
using Ioc.Modules;
using OwinFramework.Interfaces.Utility;

namespace OwinFramework.StaticFiles
{
    [Package]
    internal class Package: IPackage
    {
        public string Name { get { return "OWIN Framework static files middleware"; } }

        public IList<IocRegistration> IocRegistrations
        {
            get
            {
                return new List<IocRegistration>
                {
                    new IocRegistration().Init<IHostingEnvironment>()
                };
            }
        }

    }
}
