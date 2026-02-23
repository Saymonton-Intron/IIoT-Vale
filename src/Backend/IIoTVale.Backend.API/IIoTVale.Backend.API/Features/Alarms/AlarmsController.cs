using IIoTVale.Backend.API.Services;
using IIoTVale.Backend.Core.DTOs.Configuration;
using Microsoft.AspNetCore.Mvc;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace IIoTVale.Backend.API.Features.Alarms
{
    [Route("api/[controller]")]
    [ApiController]
    public class AlarmsController : ControllerBase
    {
        private readonly ILogger<AlarmsController> _logger;
        private readonly AlarmsService _alarmsService;

        public AlarmsController(ILogger<AlarmsController> logger, AlarmsService alarmsService)
        {
            _logger = logger;
            _alarmsService = alarmsService;
        }


        // GET: api/Alarms/{selectedSensor}
        [HttpGet("{selectedSensor}")]
        public async Task<IActionResult> Get(string selectedSensor, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(selectedSensor))
            {
                return BadRequest("O MAC address do sensor é obrigatório.");
            }

            var config = await _alarmsService.GetAlarmsConfigForSensor(selectedSensor, cancellationToken);

            if (config is not null)
            {
                return Ok(config);
            }

            return NotFound($"Configuração de alarme não encontrada para o sensor {selectedSensor}.");
        }

        // POST api/Alarms/{selectedSensor}
        [HttpPost("{selectedSensor}")]
        public async Task<IActionResult> Post([FromBody] GlobalAlarmConfig alarmConfig, string selectedSensor, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(selectedSensor))
            {
                return BadRequest("O MAC address do sensor é obrigatório.");
            }

            if (alarmConfig is null)
            {
                return BadRequest("A configuração de alarme é obrigatória.");
            }

            var result = await _alarmsService.PostAlarmsConfigForSensor(alarmConfig, selectedSensor, cancellationToken);

            if (result)
            {
                return Ok(new { message = "Configuração de alarme salva com sucesso.", macAddress = selectedSensor });
            }

            return StatusCode(500, "Erro ao salvar configuração de alarme.");
        }

        // DELETE api/Alarms/{id}
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }
    }
}
