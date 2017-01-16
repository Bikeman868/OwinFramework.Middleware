using System.Collections.Generic;
using Ioc.Modules;
using OwinFramework.Interfaces.Utility;

namespace OwinFramework.OutputCache
{
    [Package]
    internal class Package: IPackage
    {
        public string Name { get { return "OWIN Framework versioning middleware"; } }

        public IList<IocRegistration> IocRegistrations
        {
            get
            {
                return new List<IocRegistration>
                {
                };
            }
        }

    }
}
