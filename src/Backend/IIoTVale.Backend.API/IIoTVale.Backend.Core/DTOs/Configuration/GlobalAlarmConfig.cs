using System;
using System.Collections.Generic;
using System.Text;

namespace IIoTVale.Backend.Core.DTOs.Configuration
{
    public class GlobalAlarmConfig
    {
        public MetricConfig Acc { get; set; }
        public MetricConfig Vel { get; set; }
        public MetricConfig Disp { get; set; }
    }
}
