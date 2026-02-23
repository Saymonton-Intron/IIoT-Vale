using IIoTVale.Backend.Core.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace IIoTVale.Backend.Core.DTOs.Telemetry
{
    public class DaqProfileTelemetryDto : ITelemetryDto
    {
        public string SensorMAC { get; set; }
        public DaqMode DaqMode { get; set; }
        public DaqOptions DaqOptions { get; set; }
        public int MaxSampleRate { get; set; }
        public int SampleRate { get; set; }
        public TelemetryMode TelemetryMode => TelemetryMode.CREATE;
    }
}
