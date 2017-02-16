using Prius.Contracts.Interfaces.External;
using System;
using Ninject;

namespace OwinFramework.Middleware.TestServer.Prius
{
    internal class PriusFactory: IFactory
    {
        private readonly StandardKernel _ninject;

        public PriusFactory(StandardKernel ninject)
        {
            _ninject = ninject;
        }

        public object Create(Type type)
        {
            return _ninject.Get(type);
        }

        public T Create<T>() where T : class
        {
            return _ninject.Get<T>();
        }
    }
}
