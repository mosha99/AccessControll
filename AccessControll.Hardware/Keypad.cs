using Iot.Device.Display;
using Iot.Device.FtCommon;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using System.Device.Gpio;
using System.Device.I2c;

namespace AccessControll.Hardware;
public class Keypad
{
    private readonly GpioController _controller;
    private readonly ILogger<Keypad> _logger;

    private const int I2cAddress = 32;
    private const int I2cBus = 3;
    private const int GpioPin = 73;

    private I2cDevice _keypadI2c;
    public Action<char> OnPress;

    private static readonly char[,] Keys = new char[,]
    {
        { '1', '2', '3', 'A' },
        { '4', '5', '6', 'B' },
        { '7', '8', '9', 'C' },
        { '*', '0', '#', 'D' }
    };

    public Keypad(GpioController controller, ILogger<Keypad> logger)
    {
        _controller = controller;
        _logger = logger;
    }

    public void Init()
    {
        _keypadI2c = I2cDevice.Create(new I2cConnectionSettings(I2cBus, I2cAddress));

        _keypadI2c.WriteByte(0xF0);

        _controller.OpenPin(GpioPin, PinMode.InputPullUp);
        _controller.RegisterCallbackForPinValueChangedEvent(GpioPin, PinEventTypes.Falling, (s, e) =>
        {
            OnInterrupt();
        });
    }

    private void OnInterrupt()
    {
        _logger.LogTrace("On Interrupt . . .");
        Thread.Sleep(25);

        char? detectedKey = null;

        for (int row = 0; row < 4; row++)
        {
            _keypadI2c.WriteByte((byte)(~(byte)(1 << row)));
            byte reading = _keypadI2c.ReadByte();

            for (int col = 0; col < 4; col++)
            {
                bool isPressed = ((int)reading & (1 << (col + 4))) == 0;
                if (isPressed)
                {
                    detectedKey = Keys[row, col];

                    // Wait for key release
                    while (((int)_keypadI2c.ReadByte() & (1 << (col + 4))) == 0)
                    {
                        Thread.Sleep(10);
                    }
                    break;
                }
            }

            if (detectedKey != null)
                break;
        }

        // Reset keypad
        _keypadI2c.WriteByte(0xF0);

        if (detectedKey.HasValue)
        {
            char key = detectedKey.Value;
            OnPress?.Invoke(key);
            _logger.LogDebug($"Key '{key}' Pressed");
        }
    }
}
