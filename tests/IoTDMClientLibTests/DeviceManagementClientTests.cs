/*
Copyright 2017 Microsoft
Permission is hereby granted, free of charge, to any person obtaining a copy of this software 
and associated documentation files (the "Software"), to deal in the Software without restriction, 
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, 
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, 
subject to the following conditions:

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT 
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. 
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, 
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH 
THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
using Microsoft.Devices.Management;
using Microsoft.VisualStudio.TestPlatform.UnitTestFramework;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage.Streams;

using Microsoft.Devices.Management.Message;
using Microsoft.Devices.Management.Models;
using Microsoft.Azure.Devices.Shared;

namespace IoTDMClientLibTests
{
    class TwinMockup : IDeviceTwin
    {
        public Task<TwinCollection> GetDesiredPropertiesAsync()
        {
            throw new NotImplementedException();
        }

        Task IDeviceTwin.RefreshConnectionAsync()
        {
            throw new NotImplementedException();
        }

        Task IDeviceTwin.ReportProperties(Dictionary<string, object> collection)
        {
            return null;
        }

        Task IDeviceTwin.SetMethodHandlerAsync(string methodName, Func<string, Task<string>> methodHandler)
        {
            throw new NotImplementedException();
        }
    }

    class HandlerMockupForReboot : IDeviceManagementRequestHandler
    {
        bool rebootResponse;

        public HandlerMockupForReboot(bool rebootResponse)
        {
            this.rebootResponse = rebootResponse;
        }

        Task<bool> IDeviceManagementRequestHandler.IsSystemRebootAllowed()
        {
            return Task.FromResult<bool>(rebootResponse);
        }
    }

    class HandlerMockupForAppInstall : IDeviceManagementRequestHandler
    {
        public string AppName;
        public HandlerMockupForAppInstall(string appName){ AppName = appName; }

        Task<bool> IDeviceManagementRequestHandler.IsSystemRebootAllowed()
        {
            throw new NotImplementedException();
        }
    }

    class ConfigurationProxyMockup : ISystemConfiguratorProxy
    {
        IRequest request = null;
        IResponse response = null;

        public Task<IResponse> SendCommandAsync(IRequest request)
        {
            this.request = request;
            // Sending bytes over to SystemConfigurator...
            if (request.Tag == DMMessageKind.InstallApp)
            {
                var appinstallReq = (AppInstallRequest)request; // cast must succeed
                this.response = new StatusCodeResponse(ResponseStatus.Success, request.Tag);
                return Task.FromResult<IResponse>(response);
            }
            else if (request.Tag == DMMessageKind.ImmediateReboot)
            {
                var appinstallReq = (ImmediateRebootRequest)request; // cast must succeed
                this.response = new StatusCodeResponse(ResponseStatus.Success, DMMessageKind.ImmediateReboot);
                return Task.FromResult<IResponse>(response);
            }
            else throw new Exception("Unsupported command");
        }

        public IRequest ReceivedRequest => this.request;
        public IResponse ReturnedResponse => this.response;
    }

    class ICommandLineProxyMockup : ICommandLineProxy
    {
        public Task<string> ScreenCapture(string filename)
        {
            throw new NotImplementedException();
        }

        public Task<IDictionary<string, ApplicationTypes>> IotStartup(IotStartupCommands command)
        {
            throw new NotImplementedException();
        }
    }

    class DevicePortalCoreApiProxyMockup : IDevicePortalCoreApiProxy
    {
        public Task<IList<Process>> GetModernApplicationProcesses()
        {
            throw new NotImplementedException();
        }
    }

    [TestClass]
    public class DeviceManagementClientTests
    {
        [TestMethod]
        public void MockupProxyImmediateRebootTest()
        {
            var twin = new TwinMockup();
            var requestHandler = new HandlerMockupForReboot(true);
            var configProxy = new ConfigurationProxyMockup();
            var commandLineProxy = new CommandLineProxy();
            var deviceApi = new DevicePortalCoreApiProxyMockup();

            var dmClient = DeviceManagementClient.Create(twin, requestHandler, configProxy, commandLineProxy, deviceApi);
            dmClient.ImmediateRebootAsync().Wait();

            Assert.AreEqual(configProxy.ReceivedRequest.Tag, DMMessageKind.ImmediateReboot);
            Assert.AreEqual(configProxy.ReturnedResponse.Tag, DMMessageKind.ImmediateReboot);
            Assert.AreEqual(configProxy.ReturnedResponse.Status, ResponseStatus.Success);
        }

        [TestMethod]
        public void MockupProxyPostponedRebootTest()
        {
            var twin = new TwinMockup();
            var requestHandler = new HandlerMockupForReboot(false);
            var proxy = new ConfigurationProxyMockup();
            var commandLine = new CommandLineProxy();
            var deviceApi = new DevicePortalCoreApiProxyMockup();

            var dmClient = DeviceManagementClient.Create(twin, requestHandler, proxy, commandLine, deviceApi);
            dmClient.ImmediateRebootAsync().Wait();

            Assert.AreEqual(proxy.ReceivedRequest, null);
            Assert.AreEqual(proxy.ReturnedResponse, null);
        }

        [TestMethod]
        public void MockupProxyInstallAppTest()
        {
            var twin = new TwinMockup();
            var proxy = new ConfigurationProxyMockup();

            var appInstallRequest = new AppInstallRequest(new AppInstallInfo() { AppxPath = "abc", PackageFamilyName = "def", Dependencies = new List<String>() { "ghi", "jkl" } });
            var response = proxy.SendCommandAsync(appInstallRequest).Result;

            Assert.AreEqual(response.Status, ResponseStatus.Success);
            Assert.AreEqual(response.Tag, DMMessageKind.InstallApp);
        }
    }
}
