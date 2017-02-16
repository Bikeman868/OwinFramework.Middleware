using System;
using System.Data.SqlClient;
using Prius.Contracts.Interfaces.External;

namespace OwinFramework.Middleware.TestServer.Prius
{
    internal class PriusErrorReporter : IErrorReporter
    {
        public void ReportError(Exception e, SqlCommand cmd, string subject, params object[] otherInfo)
        {
        }

        public void ReportError(Exception e, string subject, params object[] otherInfo)
        {
        }

        public void Dispose()
        {
        }
    }
}
