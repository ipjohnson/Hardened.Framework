using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon.Lambda.Core;

namespace Hardened.Shared.Lambda.Testing
{
    public class TestClientApplication : IClientApplication
    {
        public TestClientApplication() : this("TestPackage","appTitle", "appVersionCode", "appVersionName", "installationId")
        {

        }

        public TestClientApplication(string appPackageName, string appTitle, string appVersionCode, string appVersionName, string installationId)
        {
            AppPackageName = appPackageName;
            AppTitle = appTitle;
            AppVersionCode = appVersionCode;
            AppVersionName = appVersionName;
            InstallationId = installationId;
        }

        public string AppPackageName { get; }

        public string AppTitle { get; }

        public string AppVersionCode { get; }

        public string AppVersionName { get; }

        public string InstallationId { get; }
    }
}
