using SixLabors.ImageSharp;

namespace AccessControll.Hardware;

public class PhysicalDoorService(Oled Oled, Keypad Keypad)
{
    private static string _inputBuffer = "";
    private const int CodeLength = 2;


    public Task<int?> ReadDoorCode()
    {
        var task = new TaskCompletionSource<int?>();

        UpdateUI();

        Task.Run(async () =>
        {
            var lisstener = Keypad.OnPress.GetInvocationList();

            Keypad.OnPress = c => OnKeyPress(c, code => task.SetResult(code));

            await Task.Delay(TimeSpan.FromSeconds(5));

            Keypad.OnPress = null;

            foreach (var item in lisstener)
            {
                Keypad.OnPress += item as Action<char> ?? throw new Exception();
            }

            if (task.Task.IsCompleted)
            {
                task.SetResult(null);
            }
        });

        return task.Task;
    }


    public async void OnKeyPress(char key, Action<int?> CodeDoor)
    {
        Console.WriteLine($"-------Press For Dorr {key}------------");
        // پاک کردن با * یا #
        if (key == '*' || key == '#')
        {
            _inputBuffer = "";
            UpdateUI();
            return;
        }

        // فقط اعداد قبول می‌شن
        if (!char.IsDigit(key)) return;

        _inputBuffer += key;
        UpdateUI();

        if (_inputBuffer.Length >= CodeLength)
        {
            var code = _inputBuffer;
            _inputBuffer = "";
            UpdateUI();
            CodeDoor.Invoke(int.Parse(code));
        }
    }

    private void UpdateUI(string title = "شماره در :")
    {
        int length = _inputBuffer.Length;

        string body = length switch
        {
            0 => "_  _",
            1 => $"{_inputBuffer[0]}  _",
            _ => $"{_inputBuffer[0]}  {_inputBuffer[1]}",
        };

        Oled.RenderUI(title, body, Color.White);
    }
}
