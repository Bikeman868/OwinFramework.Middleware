﻿using System;
using Newtonsoft.Json;
using OwinFramework.InterfacesV1.Middleware;

namespace OwinFramework.OutputCache
{
    [Serializable]
    internal class OutputCacheConfiguration
    {
        /// <summary>
        /// The relative path within your site where you can get documentation on
        /// this versioning middleware
        /// </summary>
        [JsonProperty("documentationRootUrl")]
        public string DocumentationRootUrl { get; set; }

        /// <summary>
        /// The current version number or NULL to disable versioning of assets
        /// </summary>
        [JsonProperty("version")]
        public int? Version { get; set; }

        /// <summary>
        /// Output from downstream middleware with any of these mime types
        /// will have {_v_} version markers replaced with the current version
        /// or with nothing if the current version is null
        /// </summary>
        [JsonProperty("mimeTypes")]
        public string[] MimeTypes { get; set; }

        /// <summary>
        /// Incomming requests for files with any of these extensions will have
        /// the version number stripped from the URL before chaining to the rest
        /// of the OWIN pipeline. Set to an empty list to replace version numbers
        /// on all incomming requests which match the versioned file name pattern
        /// </summary>
        [JsonProperty("fileExtensions")]
        public string[] FileExtensions { get; set; }

        /// <summary>
        /// When this is true, requests for assets that are not the current version
        /// with result in a 404 response. When this is false the most recent version
        /// of the asset will be served nomatter which version is requested.
        /// </summary>
        [JsonProperty("exactVersion")]
        public bool ExactVersion { get; set; }

        public OutputCacheConfiguration()
        {
            Version = 1;
            MimeTypes = new [] 
            {
                "text/html",
                "text/css",
                "application/javascript"
            };
            FileExtensions = new string[0];
            ExactVersion = false;
        }
    }

}