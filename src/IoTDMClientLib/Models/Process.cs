using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Devices.Management.Models
{
    class Process
    {
        public string AppName { get; set; }
        public float CPUUsage { get; set; }
        public bool IsRunning { get; set; }
        public string PackageFullName { get; set; }
        public int ProcessId { get; set; }
    }
}
