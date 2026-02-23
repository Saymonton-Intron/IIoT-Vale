using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IIoTVale.Backend.Core.DTOs.Configuration
{
    public class MetricConfig
    {
        public bool Enabled { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        [JsonPropertyName("hysteresis_percent")]
        public double HysteresisPercent { get; set; }
        [JsonPropertyName("delay_ms")]
        public double DelayMs { get; set; }
        public AxisConfig Axes { get; set; }
    }
}
