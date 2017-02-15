using System;
using Newtonsoft.Json;

namespace OwinFramework.Session
{
    [Serializable]
    internal class SessionConfiguration
    {
        /// <summary>
        /// Sessions are stored in the cache using this category.
        /// The cache facility can be configured to handle these categories
        /// differently - for example storing in process memory or on a
        /// central server.
        /// </summary>
        [JsonProperty("cacheCategory")]
        public string CacheCategory { get; set; }

        /// <summary>
        /// The length of time before a session will expire and need to be
        /// renewed
        /// </summary>
        [JsonProperty("sessionDuration")]
        public TimeSpan SessionDuration { get; set; }

        /// <summary>
        /// The name of the cookie to store on the browser containing the session id
        /// </summary>
        [JsonProperty("cookieName")]
        public string CookieName { get; set; }

        public SessionConfiguration()
        {
            CacheCategory = "session";
            SessionDuration = TimeSpan.FromMinutes(20);
            CookieName = "owin-framework-sid";
        }
    }
}
