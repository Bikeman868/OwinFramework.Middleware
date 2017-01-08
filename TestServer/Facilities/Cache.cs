using System;
using System.Collections.Generic;

namespace OwinFramework.Middleware.TestServer.Facilities
{
    internal class Cache: InterfacesV1.Facilities.ICache
    {
        private IDictionary<string, object> _cache = new Dictionary<string, object>();

        public bool Delete(string key)
        {
            lock (_cache)
            {
                var result = _cache.ContainsKey(key);
                _cache.Remove(key);
                return result;
            }
        }

        public T Get<T>(string key, T defaultValue, TimeSpan? lockTime)
        {
            lock (_cache)
            {
                if (_cache.ContainsKey(key))
                    return (T) _cache[key];
                return default(T);
            }
        }

        public bool Put<T>(string key, T value, TimeSpan? lifespan)
        {
            lock (_cache)
            {
                var result = _cache.ContainsKey(key);
                _cache[key] = value;
                return result;
            }
        }
    }
}
