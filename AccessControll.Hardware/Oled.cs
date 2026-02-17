using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Device.I2c;

namespace AccessControll.Hardware;

public class Oled
{
    private readonly ILogger<Oled> _logger;

    private const int I2cBus = 3;
    private const int OledAddress = 60;

    private I2cDevice _displayI2c;
    private Font _farsiFont;

    public Oled(ILogger<Oled> logger)
    {
        _logger = logger;
    }

    public void Init()
    {
        _displayI2c = I2cDevice.Create(new I2cConnectionSettings(I2cBus, OledAddress));
        InitializeOled();
        LoadFarsiFont();
        _logger.LogDebug("Oled Initialize Success Full");
    }

    public void RenderUI(string title, string body, Color borderColor)
    {
        using Image<L8> image = new Image<L8>(128, 64);

        image.Mutate(ctx =>
        {
            ctx.Fill(Color.Black);

            var pen = new SolidPen(Color.White, 1f);
            ctx.DrawPolygon(pen, new PointF[]
            {
                    new PointF(2f, 2f),
                    new PointF(126f, 2f),
                    new PointF(126f, 62f),
                    new PointF(2f, 62f)
            });

            if (_farsiFont == null) return;

            var titleOptions = new RichTextOptions(_farsiFont)
            {
                Origin = new PointF(122f, 6f),
                HorizontalAlignment = HorizontalAlignment.Right,
                WrappingLength = 115f
            };
            ctx.DrawText(titleOptions, title, Color.White);

            ctx.DrawLine(Color.White, 1f, new PointF[]
            {
                    new PointF(5f, 26f),
                    new PointF(123f, 26f)
            });

            var bodyOptions = new RichTextOptions(_farsiFont)
            {
                Origin = new PointF(64f, 38f),
                HorizontalAlignment = HorizontalAlignment.Center,
                WrappingLength = 115f
            };
            ctx.DrawText(bodyOptions, body, Color.White);
        });

        RenderImageToOled(image);
    }

    private void RenderImageToOled(Image<L8> image)
    {
        byte[] buffer = new byte[1024];

        image.ProcessPixelRows(accessor =>
        {
            for (int row = 0; row < 64; row++)
            {
                Span<L8> rowSpan = accessor.GetRowSpan(row);
                for (int col = 0; col < 128; col++)
                {
                    if (rowSpan[col].PackedValue > 128)
                    {
                        buffer[col + (row / 8) * 128] |= (byte)(1 << (row % 8));
                    }
                }
            }
        });

        // SSD1306 commands: set column/page address + start write
        _displayI2c.Write(new byte[] { 0x00, 0x21, 0x00, 0x7F, 0x22, 0x00, 0x07 });

        byte[] payload = new byte[1025];
        payload[0] = 0x40; // data mode
        Array.Copy(buffer, 0, payload, 1, 1024);
        _displayI2c.Write(payload);
    }

    private void LoadFarsiFont()
    {
        try
        {
            if (SystemFonts.Collection.TryGet("Vazirmatn", out FontFamily fontFamily))
                _farsiFont = fontFamily.CreateFont(14f, FontStyle.Regular);
            else
                _farsiFont = SystemFonts.CreateFont("DejaVu Sans", 14f);
        }
        catch
        {
            Console.WriteLine("Font load failed");
        }
    }

    private void InitializeOled()
    {
        // SSD1306 initialization sequence
        _displayI2c.Write(new byte[]
        {
                0x00, // command mode
                0xAE, // display off
                0xD5, 0x80, // clock divide ratio
                0xA8, 0x3F, // multiplex ratio (64 rows)
                0xD3, 0x00, // display offset
                0x40, // start line = 0
                0x8D, 0x14, // charge pump enabled
                0x20, 0x00, // horizontal addressing mode
                0xA1, // segment remap
                0xC8, // COM scan direction
                0xDA, 0x12, // COM pins config
                0x81, 0xCF, // contrast
                0xD9, 0xF1, // pre-charge period
                0xDB, 0x40, // VCOMH deselect level
                0xA4, // resume RAM content
                0xA6, // normal display
                0xAF  // display on
        });
    }
}