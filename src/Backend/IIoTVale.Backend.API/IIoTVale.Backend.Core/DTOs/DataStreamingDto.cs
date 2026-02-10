using System;
using System.Collections.Generic;
using System.Text;

namespace IIoTVale.Backend.Core.DTOs
{
    public class DataStreamingDto : ITelemetryDto
    {
        public string SensorMAC { get; set; }
        public DateTime TimeStamp { get; set; }
        public int Frequency { get; set; }
        public List<DataModel> DataModel { get; set; }
        public TelemetryMode TelemetryMode => TelemetryMode.CREATE;
    }
}
