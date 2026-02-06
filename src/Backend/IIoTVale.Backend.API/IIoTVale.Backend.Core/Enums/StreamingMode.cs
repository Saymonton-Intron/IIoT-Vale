using System;
using System.Collections.Generic;
using System.Text;

namespace IIoTVale.Backend.Core.Enums
{
    public enum StreamingMode
    {
        /// <summary>
        /// Sends MQTT (TX)
        /// </summary>
        TRANSMISSION,
        /// <summary>
        /// Autonomous mode on
        /// </summary>
        STAND_ALONE,
    }
}
