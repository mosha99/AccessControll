using SixLabors.ImageSharp;

namespace AccessControll.Hardware;

public class PhysicalDoorService(Oled Oled, KeyboardRouter Router)
{
    private static string _inputBuffer = "";
    private const int CodeLength = 2;


    public Task<int?> ReadDoorCode()
    {
        var task = new TaskCompletionSource<int?>();

        UpdateUI();

        Task.Run(async () =>
        {
            // Save current subscribers and temporarily replace with door-code handler
            var listeners = Router.OnPress?.GetInvocationList() ?? [];

            Router.OnPress = c => OnKeyPress(c, code => task.SetResult(code));

            await Task.Delay(TimeSpan.FromSeconds(5));

            // Restore previous subscribers
            Router.OnPress = null;
            foreach (var item in listeners)
                Router.OnPress += item as Action<char> ?? throw new Exception();

            if (!task.Task.IsCompleted)
                task.SetResult(null);
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
