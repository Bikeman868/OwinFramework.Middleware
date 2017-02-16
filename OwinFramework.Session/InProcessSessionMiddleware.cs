using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        IList<IDependency> IMiddleware.Dependencies { get { return _dependencies; } }

        private readonly IDictionary<string, Session> _sessions;
        private readonly Thread _cleanupThread;

        private IDisposable _configurationRegistration;
        private SessionConfiguration _configuration;

        public InProcessSessionMidleware()
        {
            _sessions = new Dictionary<string, Session>();
            ConfigurationChanged(new SessionConfiguration());

            _cleanupThread = new Thread(CleanUp) 
            {
                IsBackground = true,
                Name = "RemoveExpiredSessions",
                Priority = ThreadPriority.BelowNormal
            };
            _cleanupThread.Start();
        }

        public Task RouteRequest(IOwinContext context, Func<Task> next)
        {
            Session session;

            var sessionId = context.Request.Cookies[_configuration.CookieName];
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                session = StartSession(context);
                lock (_sessions)
                    _sessions.Add(session.SessionId, session);
            }
            else
            {
                lock (_sessions)
                {
                    if (_sessions.TryGetValue(sessionId, out session))
                    {
                        if (session.IsExpired)
                        {
                            context.Response.Cookies.Delete(sessionId);
                            _sessions.Remove(sessionId);

                            session = StartSession(context);
                            _sessions.Add(session.SessionId, session);
                        }
                    }
                    else
                    {
                        session = StartSession(context);
                        _sessions.Add(session.SessionId, session);
                    }
                }
            }

            context.SetFeature<IUpstreamSession>(session);
            context.SetFeature<ISession>(session);

            return next();
        }

        public Task Invoke(IOwinContext context, Func<Task> next)
        {
            return next();
        }

        private Session StartSession(IOwinContext context)
        {
            var sessionId = Guid.NewGuid().ToShortString();
            context.Response.Cookies.Append(
                _configuration.CookieName,
                sessionId,
                new CookieOptions
                {
                    Expires = DateTime.UtcNow.AddDays(1)
                });
            return new Session(sessionId, context, _configuration);
        }

        private void CleanUp()
        {
            while(true)
            {
                try
                {
                    Thread.Sleep(100);
                    List<string> sessionIds;
                    lock(_sessions)
                    {
                        sessionIds = _sessions.Keys.ToList();
                    }
                    var i = 0;
                    foreach (var sessionId in sessionIds)
                    {
                        if ((i++ & 15) == 0) Thread.Sleep(1);
                        lock(_sessions)
                        {
                            Session session;
                            if (_sessions.TryGetValue(sessionId, out session))
                            {
                                if (session.IsExpired)
                                    _sessions.Remove(sessionId);
                            }
                        }
                    }
                }
                catch (ThreadAbortException)
                {
                    return;
                }
                catch
                { 
                }
            }
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
            private readonly SessionConfiguration _configuration;
            private readonly DateTime _whenExpires;
            private readonly IDictionary<string, object> _sessionVariables;

            public string SessionId { get; private set; }
            public bool HasSession { get { return true; } }

            public Session(string sessionId, IOwinContext context, SessionConfiguration configuration)
            {
                SessionId = sessionId;
                _context = context;
                _configuration = configuration;
                _whenExpires = DateTime.UtcNow + configuration.SessionDuration;
                _sessionVariables = new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }

            public bool IsExpired { get { return DateTime.UtcNow > _whenExpires; } }

            public bool EstablishSession(string sessionId)
            {
                if (sessionId != null && !string.Equals(sessionId, SessionId, StringComparison.Ordinal))
                    throw new NotImplementedException("In-process session middleware does not have the ability to establish a specific session. Suggest using CacheSessionMiddleware instead");
                return true;
            }

            public T Get<T>(string name)
            {
                object value;
                return _sessionVariables.TryGetValue(name, out value) ? (T)value : default(T);
            }

            public void Set<T>(string name, T value)
            {
                _sessionVariables[name] = value;
            }

            public object this[string name]
            {
                get { return Get<object>(name); }
                set { Set(name, value); }
            }
        }

    }
}
