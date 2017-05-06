using System;
using Microsoft.Owin.Hosting;

namespace OwinFramework.Middleware.TestServer
{
    /// <summary>
    /// This console application uses the Microsoft self hosted Owin package to
    /// listen on a specific port and handle requests using Owin. This was build
    /// to allow you to experiment with the middleware components available
    /// in the Owin Framework.
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            const string url = "http://localhost:12345";
            const string secureUrl = "http://localhost:54321";

            try
            {
                var startOptions = new StartOptions
                {
                    Urls = { url, secureUrl }
                };
                using (WebApp.Start<Startup>(startOptions))
                {
                    Console.WriteLine("Test server listening on " + url + " and " + secureUrl);
                    Console.WriteLine("Press any key to stop");
                }
            }
            catch (Exception ex)
            {
                while (ex != null)
                {
                    Console.WriteLine(ex.Message);
                    ex = ex.InnerException;
                }
            }
            Console.ReadLine();
        }
    }
}