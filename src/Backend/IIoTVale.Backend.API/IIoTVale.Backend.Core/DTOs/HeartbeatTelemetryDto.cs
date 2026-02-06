using IIoTVale.Backend.Core.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace IIoTVale.Backend.Core.DTOs
{
    public class HeartbeatTelemetryDto : ITelemetryDto
    {
        public DateTime DateAndTime { get; set; }
        public SysAndNetworkStatus NetworkStatus { get; set; }
        /// <summary>
        /// Available datalogger memory in percentage.
        /// </summary>
        public double DataLoggerMemoryPercentage { get; set; }
        /// <summary>
        /// Battery voltage (Little Endian).
        /// </summary>
        public double BatteryVolts { get; set; }
        public SensorType SensorType { get; set; }
        public int AvailableChannels { get; set; }
        public byte[] ChannelsStatus { get; set; }

        public TelemetryMode TelemetryMode => TelemetryMode.UPDATE;
    }
}
