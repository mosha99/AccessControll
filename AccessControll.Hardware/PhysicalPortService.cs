using AccessControll.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Device.Gpio;
using System.Device.I2c;

namespace AccessControll.Hardware;

public class PhysicalPortService(GpioController Controller , ILogger<PhysicalPortService> logger) : IPhysicalPortService
{
    public void SetPortState(string portAddress, bool low)
    {
        logger.LogInformation($"Set {portAddress} to {low}");
        try
        {
            int value = low ? 0 : 1;
            if (portAddress.StartsWith('@'))
            {
                var param = portAddress.Remove(0, 1).Split('-').Select(x => int.Parse(x)).ToList();
                if (param.Count != 3) throw new ArgumentException("Address Is invalid");
                using I2cDevice i2CDevice = I2cDevice.Create(new I2cConnectionSettings(param[0], param[1]));

                byte current = i2CDevice.ReadByte();
                i2CDevice.WriteByte((byte)(current | (value << param[2])));
            }
            else if (int.TryParse(portAddress, out var pinNumber))
            {
                var pin = Controller.OpenPin(pinNumber, PinMode.Output);
                pin.Write(value);
                pin.Dispose();
            }
            else throw new Exception("port address not find");
        }
        catch (Exception e)
        {
            logger.LogError(e,e.Message);
        }     

    }
}
