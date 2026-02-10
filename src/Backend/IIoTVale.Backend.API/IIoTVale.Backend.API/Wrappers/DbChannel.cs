using IIoTVale.Backend.Core.DTOs;
using System.Threading.Channels;

namespace IIoTVale.Backend.API.Wrappers
{
    public class DbChannel
    {
        private readonly Channel<ITelemetryDto> _channel;

        public DbChannel()
        {
            _channel = Channel.CreateUnbounded<ITelemetryDto>();
        }

        public ChannelReader<ITelemetryDto> Reader => _channel.Reader;
        public ChannelWriter<ITelemetryDto> Writer => _channel.Writer;
    }
}
