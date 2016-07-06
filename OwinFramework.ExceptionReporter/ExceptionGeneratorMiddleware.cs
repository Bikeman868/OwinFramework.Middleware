using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OwinFramework.Interfaces.Builder;

namespace OwinFramework.ExceptionReporter
{
    public class ExceptionGeneratorMiddleware: IMiddleware<object>
    {
        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        public IList<IDependency> Dependencies { get { return _dependencies; } }

        public string Name { get; set; }

        Task IMiddleware.Invoke(Microsoft.Owin.IOwinContext context, Func<Task> next)
        {
            throw new Exception("This exception was thrown to test exception handling");
        }
    }
}
