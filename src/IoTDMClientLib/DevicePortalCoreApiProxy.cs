using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Devices.Management.Models;
using Newtonsoft.Json.Linq;

namespace Microsoft.Devices.Management
{
    class DevicePortalCoreApiProxy : IDevicePortalCoreApiProxy
    {   
        public async Task<IList<Process>> GetModernApplicationProcesses()
        {
            List<Models.Process> result = null;

            using (var httpClient = new System.Net.Http.HttpClient())
            {
                //ADD BASIC AUTH
                var authByteArray = System.Text.Encoding.ASCII.GetBytes("Administrator:p@ssw0rd");
                var authString = Convert.ToBase64String(authByteArray);
                httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authString);

                var resp = await httpClient.GetAsync(new Uri("http://localhost:8080/api/resourcemanager/processes"));

                if (resp.IsSuccessStatusCode)
                {
                    string strResult = await resp.Content.ReadAsStringAsync();
                    JArray jarr = (JArray)JObject.Parse(strResult)["Processes"];

                    return
                        (from c in jarr
                         where c["AppName"] != null
                         select new Models.Process()
                         {
                             AppName = c["AppName"].Value<string>(),
                             CPUUsage = c["CPUUsage"].Value<float>(),
                             IsRunning = c["IsRunning"].Value<bool>(),
                             PackageFullName = c["PackageFullName"].Value<string>(),
                             ProcessId = c["ProcessId"].Value<int>(),
                         }).ToList();
                       
                }
                else
                {
                    return null;
                }
            }
        }
    }
}
