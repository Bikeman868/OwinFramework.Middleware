using Prius.Contracts.Interfaces.External;
using System;
using Ninject;

namespace OwinFramework.Middleware.TestServer.Prius
{
    internal class PriusFactory: IFactory
    {
        public static StandardKernel Ninject;

        public object Create(Type type)
        {
            return Ninject.Get(type);
        }

        public T Create<T>() where T : class
        {
            return Ninject.Get<T>();
        }
    }
}
