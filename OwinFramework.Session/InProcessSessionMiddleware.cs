using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Owin;
using OwinFramework.Builder;
using OwinFramework.Interfaces.Builder;
using OwinFramework.InterfacesV1.Capability;
using OwinFramework.InterfacesV1.Middleware;
using OwinFramework.InterfacesV1.Upstream;

namespace OwinFramework.Session
{
    public class InProcessSessionMidleware : 
        IMiddleware<ISession>, 
        IUpstreamCommunicator<IUpstreamSession>,
        IConfigurable
    {
        public string Name { get; set; }
        public IList<IDependency> Dependencies { get; private set; }

        private readonly IDictionary<string, Session> _sessions;

        private IDisposable _configurationRegistration;
        private SessionConfiguration _configuration;

        public InProcessSessionMidleware()
        {
            _sessions = new Dictionary<string, Session>();
            Dependencies = new List<IDependency>();
        }

        public Task RouteRequest(IOwinContext context, Func<Task> next)
        {
            context.SetFeature<IUpstreamSession>(new Session(context, _configuration));
            return next();
        }

        public Task Invoke(IOwinContext context, Func<Task> next)
        {
            var session = context.GetFeature<IUpstreamSession>() as Session;

            if (session != null && session.SessionRequired)
            {
                var identification = context.GetFeature<IIdentification>();
                if (identification != null && !identification.IsAnonymous)
                {
                    lock (_sessions)
                    {
                        Session existingSession;
                        if (_sessions.TryGetValue(identification.Identity, out existingSession))
                        {
                            session = existingSession;
                        }
                        else
                        {
                            session.EstablishSession();
                            _sessions.Add(identification.Identity, session);
                        }
                    }
                }
            }

            if (session == null)
                session = new Session(context, _configuration);

            context.SetFeature<ISession>(session);

            return next();
        }

        #region IConfigurable

        public void Configure(IConfiguration configuration, string path)
        {
            _configurationRegistration = configuration.Register(
                path,
                ConfigurationChanged,
                _configuration);
        }

        private void ConfigurationChanged(SessionConfiguration configuration)
        {
            _configuration = configuration;
        }

        #endregion

        private class Session : ISession, IUpstreamSession
        {
            private readonly IOwinContext _context;
            private readonly SessionConfiguration _configuration ;

            private IDictionary<string, object> _sessionVariables;

            public bool HasSession { get { return _sessionVariables != null; } }
            public bool SessionRequired;

            public Session(IOwinContext context, SessionConfiguration configuration)
            {
                _context = context;
                _configuration = configuration;
            }

            public bool EstablishSession()
            {
                SessionRequired = true;
                if (!HasSession)
                    _sessionVariables = new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                return true;
            }

            public T Get<T>(string name)
            {
                if (!HasSession) return default(T);

                object value;
                return _sessionVariables.TryGetValue(name, out value) ? (T)value : default(T);
            }

            public void Set<T>(string name, T value)
            {
                if (HasSession)
                {
                    _sessionVariables[name] = value;
                }
            }

            public object this[string name]
            {
                get { return Get<object>(name); }
                set { Set(name, value); }
            }
        }

    }
}
