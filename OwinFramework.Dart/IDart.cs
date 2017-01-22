using OwinFramework.InterfacesV1.Middleware;

namespace OwinFramework.Dart
{
    public interface IDart : IRequestRewriter
    {
        /// <summary>
        /// Returns true if the browser making the request has native support for
        /// programs written in the Dart programming language.
        /// </summary>
        bool IsDartSupported { get; }
    }
}
