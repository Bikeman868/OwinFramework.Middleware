using System.Collections.Generic;
using Ioc.Modules;
using OwinFramework.Interfaces.Utility;

namespace OwinFramework.Dart
{
    [Package]
    internal class Package: IPackage
    {
        public string Name { get { return "OWIN Framework Dart middleware"; } }

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
