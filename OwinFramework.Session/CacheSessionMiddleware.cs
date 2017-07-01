using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Owin;
using Newtonsoft.Json;
using OwinFramework.Builder;
using OwinFramework.Interfaces.Builder;
using OwinFramework.InterfacesV1.Capability;
using OwinFramework.InterfacesV1.Facilities;
using OwinFramework.InterfacesV1.Middleware;
using OwinFramework.InterfacesV1.Upstream;

namespace OwinFramework.Session
{
    public class CacheSessionMiddleware:
        IMiddleware<ISession>,
        IUpstreamCommunicator<IUpstreamSession>,
        IConfigurable
    {
        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        IList<IDependency> IMiddleware.Dependencies { get { return _dependencies; } }

        private readonly ICache _cache;

        string IMiddleware.Name { get; set; }

        private IDisposable _configurationRegistration;
        private SessionConfiguration _configuration;

        public CacheSessionMiddleware(ICache cache)
        {
            _cache = cache;
            ConfigurationChanged(new SessionConfiguration());
        }

        public Task RouteRequest(IOwinContext context, Func<Task> next)
        {
            var session = new Session(_cache, context, _configuration);
            context.SetFeature<IUpstreamSession>(session);
            context.SetFeature<ISession>(session);

            return next();
        }

        public Task Invoke(IOwinContext context, Func<Task> next)
        {
            var result = next();

            var session = context.GetFeature<ISession>() as Session;
            if (session != null)
                session.Dispose();

            return result;
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

        private class CacheEntry
        {
            public string Name { get; set; }
            public object Value { get; set; }
        }

        private class Session : ISession, IUpstreamSession, IDisposable
        {
            private readonly ICache _cache;
            private readonly IOwinContext _context;
            private readonly SessionConfiguration _configuration;

            private Dictionary<string, string> _cacheEntries;
            private IDictionary<string, CacheEntry> _sessionVariables;
            private HashSet<string> _modifiedKeys;

            public string SessionId { get; private set; }
            public bool HasSession { get { return _sessionVariables != null; } }

            public Session(
                ICache cache, 
                IOwinContext context,
                SessionConfiguration configuration)
            {
                _cache = cache;
                _context = context;
                _configuration = configuration;
            }

            ~Session()
            {
                Dispose(true);
            }

            public void Dispose()
            {
                GC.SuppressFinalize(this);
                Dispose(false);
            }

            private void Dispose(bool destructor)
            {
                if (HasSession && _modifiedKeys.Count > 0)
                {
                    _cacheEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var key in _modifiedKeys)
                    {
                        var modifiedEntry = _sessionVariables[key];
                        _cacheEntries[modifiedEntry.Name] = JsonConvert.SerializeObject(modifiedEntry.Value);
                    }

                    _sessionVariables = null;
                    _cache.Put(SessionId, _cacheEntries, _configuration.SessionDuration, _configuration.CacheCategory);
                }
            }

            public bool EstablishSession(string sessionId)
            {
                if (!HasSession || sessionId != null)
                {
                    SessionId = sessionId ?? _context.Request.Cookies[_configuration.CookieName];
                    _cacheEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (string.IsNullOrWhiteSpace(SessionId))
                    {
                        SessionId = Guid.NewGuid().ToShortString();
                        var cookieOptions = new CookieOptions
                            { 
                                Expires = DateTime.UtcNow.AddDays(1)
                            };
                        if (!string.IsNullOrEmpty(_configuration.CookieDomainName))
                            cookieOptions.Domain = _configuration.CookieDomainName;
                        _context.Response.Cookies.Append(_configuration.CookieName, SessionId, cookieOptions);
                    }
                    else
                    {
                        var cacheEntries = _cache.Get<Dictionary<string, string>>(SessionId, null, null, _configuration.CacheCategory);
                        if (cacheEntries != null)
                        {
                            foreach (var entry in cacheEntries)
                                _cacheEntries[entry.Key] = entry.Value;
                        }
                    }
                    _modifiedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    _sessionVariables = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
                }
                return true;
            }

            public T Get<T>(string name)
            {
                if (!HasSession) return default(T);

                CacheEntry cacheEntry;
                if (!_sessionVariables.TryGetValue(name, out cacheEntry))
                {
                    cacheEntry = new CacheEntry { Name = name };

                    string json;
                    cacheEntry.Value = _cacheEntries.TryGetValue(name, out json) 
                        ? JsonConvert.DeserializeObject<T>(json)
                        : default(T);

                    _sessionVariables[name] = cacheEntry;
                }
                return (T)cacheEntry.Value;
            }

            public void Set<T>(string name, T value)
            {
                if (HasSession)
                {
                    CacheEntry cacheEntry;
                    if (_sessionVariables.TryGetValue(name, out cacheEntry))
                    {
                        cacheEntry.Value = value;
                    }
                    else
                    {
                        cacheEntry = new CacheEntry
                            {
                                Name = name,
                                Value = value
                            };
                        _sessionVariables[name] = cacheEntry;
                    }
                    if (!_modifiedKeys.Contains(name)) _modifiedKeys.Add(name);
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
