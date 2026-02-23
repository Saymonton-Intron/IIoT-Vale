using IIoTVale.Backend.API.Services;
using IIoTVale.Backend.Core.DTOs.Configuration;

namespace IIoTVale.Backend.API.Features.Alarms
{
    public class AlarmsService
    {
        private readonly ILogger<AlarmsService> _logger;
        private readonly DatabaseService _databaseService;

        public AlarmsService(ILogger<AlarmsService> logger, DatabaseService databaseService)
        {
            _logger = logger;
            _databaseService = databaseService;
        }

        public async Task<bool> PostAlarmsConfigForSensor(GlobalAlarmConfig alarmConfig, string selectedSensor, CancellationToken cancellationToken = default)
        {
            if (alarmConfig is not null && !string.IsNullOrWhiteSpace(selectedSensor))
            {
                _logger.LogInformation("Salvando configuração de alarme para o sensor: {SelectedSensor}", selectedSensor);
                return await _databaseService.SaveAlarmConfigAsync(selectedSensor, alarmConfig, cancellationToken);
            }
            return false;
        }

        public async Task<GlobalAlarmConfig?> GetAlarmsConfigForSensor(string selectedSensor, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(selectedSensor))
            {
                _logger.LogInformation("Buscando configuração de alarme para o sensor: {SelectedSensor}", selectedSensor);
                return await _databaseService.GetAlarmConfigAsync(selectedSensor, cancellationToken);
            }
            return null;
        }
    }
}
