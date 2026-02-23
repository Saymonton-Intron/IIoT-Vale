using System;
using System.Collections.Generic;
using System.Text;

namespace IIoTVale.Backend.Core.DTOs.Telemetry
{
    public class DataModel
    {
        public double AccZ { get; set; }
        public double AccX { get; set; }
        public double AccY { get; set; }
        public DateTime SampleTime { get; set; }
    }
}
