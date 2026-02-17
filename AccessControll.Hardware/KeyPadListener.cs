using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AccessControll.Hardware;

public class KeyPadListener : BackgroundService
    {
        private readonly Keypad _keypad;
        private readonly Oled _oled;
        private readonly PhysicalAuthService _physicalAuth;
        private readonly ILogger<KeyPadListener> _logger;

        public KeyPadListener(Keypad keypad, Oled oled, PhysicalAuthService physicalAuth, ILogger<KeyPadListener> logger)
        {
            _keypad = keypad;
            _oled = oled;
            _physicalAuth = physicalAuth;
            _logger = logger;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("KeyPadListener started.");

            _keypad.Init();

            _keypad.OnPress += _physicalAuth.OnKeyPress;

            return Task.CompletedTask;
        }
    }

