using System;
using System.Collections.Generic;
using System.Text;

namespace IIoTVale.Backend.Core.Enums
{
    public class DaqOptions
    {
        public bool DataLogger { get; set; }
        public bool StoreAndForward { get; set; }
        public StreamingType StreamingType { get; set; }
        public bool Transmission { get; set; }
        public bool StandAlone { get; set; }
    }
}
