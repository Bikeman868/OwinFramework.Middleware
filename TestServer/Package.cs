using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ioc.Modules;
using Ninject;
using OwinFramework.Interfaces.Utility;
using OwinFramework.Middleware.TestServer.Prius;
using Prius.Contracts.Interfaces.External;

namespace OwinFramework.Middleware.TestServer
{
    [Package]
    internal class Package: IPackage
    {
        public string Name { get { return "OWIN Framework middleware test server"; } }

        public IList<IocRegistration> IocRegistrations
        {
            get
            {
                return new List<IocRegistration>
                {
                    new IocRegistration().Init<IHostingEnvironment, HostingEnvironment>(),
                    new IocRegistration().Init<IFactory, PriusFactory>(),
                    new IocRegistration().Init<IErrorReporter, PriusErrorReporter>(),
                };
            }
        }

    }
}
