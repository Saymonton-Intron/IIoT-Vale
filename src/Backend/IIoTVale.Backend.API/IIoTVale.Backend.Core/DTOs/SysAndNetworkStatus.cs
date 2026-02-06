using System;
using System.Collections.Generic;
using System.Text;

namespace IIoTVale.Backend.Core.DTOs
{
    public class SysAndNetworkStatus
    {
        public int ErrorRate { get; set; }
        public int LQI { get; set; }
        public double InternalTemperature { get; set; }
    }
}
