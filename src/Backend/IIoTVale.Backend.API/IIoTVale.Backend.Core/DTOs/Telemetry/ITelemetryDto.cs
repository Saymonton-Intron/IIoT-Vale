using System;
using System.Collections.Generic;
using System.Text;

namespace IIoTVale.Backend.Core.DTOs.Telemetry
{
    public enum TelemetryMode
    {
        UPDATE,
        CREATE
    }

    public interface ITelemetryDto
    {
        public TelemetryMode TelemetryMode { get; }
    }
}
