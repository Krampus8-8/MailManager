using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace MailManager.App.Services;

internal sealed class SecretClipboard
{
    private const uint UnicodeText = 13;
    private const uint MoveableZeroInitialized = 0x42;
    private readonly DispatcherTimer _timer = new();
    private uint _sequence;

    internal SecretClipboard() => _timer.Tick += (_, _) => Clear();

    internal void Copy(string value, int seconds)
    {
        var excluded = RegisterClipboardFormatW("ExcludeClipboardContentFromMonitorProcessing");
        var owner = new WindowInteropHelper(Application.Current.MainWindow).Handle;
        if (excluded == 0 || !OpenClipboard(owner))
            throw ClipboardUnavailable();

        try
        {
            if (!EmptyClipboard())
                throw ClipboardUnavailable();

            // Устанавливаем ограничение до размещения текста и передаём обе записи сразу.
            SetFormat(excluded, [1, 0, 0, 0], terminatorBytes: 0);
            var text = Encoding.Unicode.GetBytes(value);
            try
            {
                SetFormat(UnicodeText, text, terminatorBytes: 2);
                _sequence = GetClipboardSequenceNumber();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(text);
            }
        }
        finally
        {
            CloseClipboard();
        }

        _timer.Stop();
        _timer.Interval = TimeSpan.FromSeconds(seconds);
        _timer.Start();
    }

    internal void Clear()
    {
        _timer.Stop();
        if (_sequence == 0 || _sequence != GetClipboardSequenceNumber())
            return;

        if (!OpenClipboard(0))
        {
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Start();
            return;
        }

        try
        {
            // Повторная сверка под блокировкой защищает текст, скопированный другой программой.
            if (_sequence == GetClipboardSequenceNumber() && !EmptyClipboard())
            {
                _timer.Interval = TimeSpan.FromSeconds(1);
                _timer.Start();
                return;
            }

            _sequence = 0;
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static void SetFormat(uint format, byte[] data, int terminatorBytes)
    {
        var memory = GlobalAlloc(MoveableZeroInitialized, (nuint)(data.Length + terminatorBytes));
        if (memory == 0)
            throw ClipboardUnavailable();

        try
        {
            var pointer = GlobalLock(memory);
            if (pointer == 0)
                throw ClipboardUnavailable();

            try
            {
                Marshal.Copy(data, 0, pointer, data.Length);
            }
            finally
            {
                GlobalUnlock(memory);
            }

            if (SetClipboardData(format, memory) == 0)
                throw ClipboardUnavailable();

            memory = 0; // После успешной передачи память принадлежит Windows.
        }
        finally
        {
            if (memory != 0)
                GlobalFree(memory);
        }
    }

    private static ExternalException ClipboardUnavailable() => new("Clipboard unavailable.");

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormatW(string name);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool OpenClipboard(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint SetClipboardData(uint format, nint memory);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern nint GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern bool GlobalUnlock(nint memory);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern nint GlobalFree(nint memory);
}
