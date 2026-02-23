using IIoTVale.Backend.Core.DTOs.Telemetry;
using System.Threading.Channels;

namespace IIoTVale.Backend.API.Wrappers
{
    public class UiChannel
    {
        private readonly Channel<ITelemetryDto> _channel;

        public UiChannel()
        {
            _channel = Channel.CreateUnbounded<ITelemetryDto>();
        }

        public ChannelReader<ITelemetryDto> Reader => _channel.Reader;
        public ChannelWriter<ITelemetryDto> Writer => _channel.Writer;
    }
}
