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
using OwinFramework.MiddlewareHelpers.Analysable;

namespace OwinFramework.Session
{
    public class CacheSessionMiddleware:
        IMiddleware<ISession>,
        IUpstreamCommunicator<IUpstreamSession>,
        IConfigurable,
        IAnalysable
    {
        private readonly IList<IDependency> _dependencies = new List<IDependency>();
        IList<IDependency> IMiddleware.Dependencies { get { return _dependencies; } }

        private readonly ICache _cache;

        string IMiddleware.Name { get; set; }

        private int _createdCount;

        private IDisposable _configurationRegistration;
        private SessionConfiguration _configuration;
        private string _cacheCategory;
        private TimeSpan _sessionDuration;
        private string _cookieName;

        public CacheSessionMiddleware(ICache cache)
        {
            _cache = cache;
            ConfigurationChanged(new SessionConfiguration());
        }

        public Task RouteRequest(IOwinContext context, Func<Task> next)
        {
            context.SetFeature<IUpstreamSession>(
                new Session(_cache, context, _sessionDuration, _cacheCategory, _cookieName));
            return next();
        }

        public Task Invoke(IOwinContext context, Func<Task> next)
        {
            var session = context.GetFeature<IUpstreamSession>() as Session;
            if (session != null)
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
            _cacheCategory = configuration.CacheCategory;
            _sessionDuration = configuration.SessionDuration;
            _cookieName = configuration.CookieName;
        }

        #endregion

        #region IAnalysable

        public IList<IStatisticInformation> AvailableStatistics
        {
            get
            {
                var stats = new List<IStatisticInformation>();
                stats.Add(
                    new StatisticInformation
                    {
                        Id = "CreatedCount",
                        Name = "Session created count",
                        Description = "The number of sessions created since startup"
                    });
                return stats;
            }
        }

        public IStatistic GetStatistic(string id)
        {
            switch (id)
            {
                case "CreatedCount":
                    return new IntStatistic(() => _createdCount);
            }
            return null;
        }

        #endregion

        private class CacheEntry
        {
            public string Name { get; set; }
            public string Json { get; set; }
        }

        private class DeserializedCacheEntry : CacheEntry
        {
            public object Value { get; set; }
        }

        private class Session : ISession, IUpstreamSession, IDisposable
        {
            private readonly ICache _cache;
            private readonly IOwinContext _context;
            private readonly TimeSpan _sessionDuration;
            private readonly string _cacheCategory;
            private readonly string _cookieName;

            private string _sessionId;
            private List<CacheEntry> _cacheEntries;
            private IDictionary<string, DeserializedCacheEntry> _sessionVariables;
            private HashSet<string> _modifiedKeys;

            public bool HasSession { get { return _sessionVariables != null; } }

            public Session(
                ICache cache, 
                IOwinContext context,
                TimeSpan sessionDuration,
                string cacheCategory,
                string cookieName)
            {
                _cache = cache;
                _context = context;
                _sessionDuration = sessionDuration;
                _cacheCategory = cacheCategory;
                _cookieName = cookieName;
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
                    if (_cacheEntries == null)
                    {
                        _cacheEntries = new List<CacheEntry>();
                    }
                    else
                    {
                        for (var i = 0; i < _cacheEntries.Count;)
                        {
                            if (_modifiedKeys.Contains(_cacheEntries[i].Name))
                                _cacheEntries.RemoveAt(i);
                            else
                                i++;
                        }
                    }

                    foreach (var key in _modifiedKeys)
                    {
                        var modifiedEntry = _sessionVariables[key];
                        _cacheEntries.Add(
                            new CacheEntry
                            {
                                Name = modifiedEntry.Name,
                                Json = JsonConvert.SerializeObject(modifiedEntry.Value)
                            });
                    }

                    _sessionVariables = null;
                    _cache.Put(_sessionId, _cacheEntries, _sessionDuration, _cacheCategory);
                }
            }

            public bool EstablishSession()
            {
                if (!HasSession)
                {
                    _sessionId = _context.Request.Cookies[_cookieName];
                    if (string.IsNullOrWhiteSpace(_sessionId))
                    {
                        _sessionId = Guid.NewGuid().ToShortString();
                        _cacheEntries = new List<CacheEntry>();
                        _context.Response.Cookies.Append(
                            _cookieName, 
                            _sessionId, 
                            new CookieOptions 
                            { 
                                Expires = DateTime.UtcNow.AddDays(1)
                            });
                    }
                    else
                    {
                        _cacheEntries = _cache.Get<List<CacheEntry>>(_sessionId, null, null, _cacheCategory);
                        if (_cacheEntries == null)
                            _cacheEntries = new List<CacheEntry>();
                    }
                    _modifiedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    _sessionVariables = new Dictionary<string, DeserializedCacheEntry>(StringComparer.OrdinalIgnoreCase);
                }
                return true;
            }

            public T Get<T>(string name)
            {
                if (!HasSession) return default(T);

                DeserializedCacheEntry deserializedCacheEntry;
                if (!_sessionVariables.TryGetValue(name, out deserializedCacheEntry))
                {
                    deserializedCacheEntry = new DeserializedCacheEntry { Name = name };

                    var cacheEntry = _cacheEntries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (cacheEntry != null)
                    {
                        deserializedCacheEntry.Json = cacheEntry.Json;
                        deserializedCacheEntry.Value = JsonConvert.DeserializeObject<T>(cacheEntry.Json);
                    }
                    else
                    {
                        deserializedCacheEntry.Value = default(T);
                    }

                    _sessionVariables[name] = deserializedCacheEntry;
                }
                return (T)deserializedCacheEntry.Value;
            }

            public void Set<T>(string name, T value)
            {
                if (HasSession)
                {
                    DeserializedCacheEntry deserializedCacheEntry;
                    if (_sessionVariables.TryGetValue(name, out deserializedCacheEntry))
                    {
                        deserializedCacheEntry.Value = value;
                    }
                    else
                    {
                        deserializedCacheEntry = new DeserializedCacheEntry
                            {
                                Name = name,
                                Value = value
                            };
                        _sessionVariables[name] = deserializedCacheEntry;
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
