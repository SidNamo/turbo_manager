namespace TurboManager;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Gma.System.MouseKeyHook;

internal static class Program
{
    public static Action<string>? InvokeRemoveTurbo;

    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new TurboManagerForm());
    }
}

public partial class TurboManagerForm : Form
{
    private IKeyboardMouseEvents _globalHook;
    private string turboDesignatedKey = null;
    private Dictionary<string, (TurboKeyControl control, TurboKeyItem item)> turboKeyMap = new();

    private FlowLayoutPanel flowKeys;
    private Label lblTurboKey;

    private bool settingTurbo = false;

    public TurboManagerForm()
    {
        Program.InvokeRemoveTurbo = RemoveTurboKey;
        SetupUI();
        SubscribeGlobalHooks();
    }

    private void SetupUI()
    {
        this.Text = "Turbo Manager";
        this.Size = new Size(500, 400);

        var btnSetTurbo = new Button { Text = "터보지정키 설정", Location = new Point(20, 20), Width = 120 };
        btnSetTurbo.Click += BtnSetTurbo_Click;
        this.Controls.Add(btnSetTurbo);

        lblTurboKey = new Label { Text = "현재 터보지정키: (미지정)", Location = new Point(150, 25), Width = 300 };
        this.Controls.Add(lblTurboKey);

        flowKeys = new FlowLayoutPanel
        {
            Location = new Point(20, 60),
            Width = 440,
            Height = 280,
            AutoScroll = true,
            WrapContents = false,
            FlowDirection = FlowDirection.TopDown
        };
        this.Controls.Add(flowKeys);
    }

    private void BtnSetTurbo_Click(object sender, EventArgs e)
    {
        lblTurboKey.Text = "터보지정키: 입력 대기 중...";
        settingTurbo = true;
    }

    private void SubscribeGlobalHooks()
    {
        _globalHook = Hook.GlobalEvents();
        _globalHook.KeyDown += OnKeyDown;
        _globalHook.MouseDown += OnMouseDown;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.KeyCode.ToString();

        if (settingTurbo)
        {
            turboDesignatedKey = key;
            lblTurboKey.Text = $"현재 터보지정키: {turboDesignatedKey}";
            settingTurbo = false;
            return;
        }

        if (turboDesignatedKey == key)
        {
            var combined = KeyboardStateHelper.GetPressedKeys().Cast<string>()
                .Concat(MouseStateHelper.GetPressedButtons())
                .Where(k => k != turboDesignatedKey)
                .ToList();

            if (combined.Any())
            {
                var first = combined.First();
                if (first.StartsWith("L") || first.StartsWith("R") || first.StartsWith("M"))
                    AddTurboKey("mouse_" + first);
                else
                    AddTurboKey("keyboard_" + first);
            }
            return;
        }

        string keyId = "keyboard_" + key;
        if (turboKeyMap.TryGetValue(keyId, out var pair))
        {
            pair.item.Start();
        }
    }

    private void OnMouseDown(object sender, MouseEventArgs e)
    {
        string btn = e.Button switch
        {
            MouseButtons.Left => "LButton",
            MouseButtons.Right => "RButton",
            MouseButtons.Middle => "MButton",
            _ => null
        };

        if (btn == null) return;

        if (settingTurbo)
        {
            turboDesignatedKey = btn;
            lblTurboKey.Text = $"현재 터보지정키: {turboDesignatedKey}";
            settingTurbo = false;
            return;
        }

        if (turboDesignatedKey == btn)
        {
            var combined = KeyboardStateHelper.GetPressedKeys().Cast<string>()
                .Concat(MouseStateHelper.GetPressedButtons())
                .Where(k => k != turboDesignatedKey)
                .ToList();

            if (combined.Any())
            {
                var first = combined.First();
                if (first.StartsWith("L") || first.StartsWith("R") || first.StartsWith("M"))
                    AddTurboKey("mouse_" + first);
                else
                    AddTurboKey("keyboard_" + first);
            }
            return;
        }

        string keyId = "mouse_" + btn;
        if (turboKeyMap.TryGetValue(keyId, out var pair))
        {
            pair.item.Start();
        }
    }

    private void AddTurboKey(string key)
    {
        if (turboKeyMap.ContainsKey(key))
        {
            RemoveTurboKey(key);
            return;
        }

        var control = new TurboKeyControl(key);
        var item = new TurboKeyItem(key, control.Interval);
        control.OnDelete += () => RemoveTurboKey(key);
        control.OnIntervalChanged += (v) => item.UpdateInterval(v);

        turboKeyMap[key] = (control, item);
        flowKeys.Controls.Add(control);
        //item.Start();
    }

    private void RemoveTurboKey(string key)
    {
        if (turboKeyMap.TryGetValue(key, out var pair))
        {
            //pair.item.Stop();
            flowKeys.Controls.Remove(pair.control);
            turboKeyMap.Remove(key);
        }
    }
}

public class TurboKeyControl : UserControl
{
    public event Action? OnDelete;
    public event Action<int>? OnIntervalChanged;

    private TrackBar _trackBar;
    private Label _lblInterval;

    public int Interval => _trackBar.Value;

    public TurboKeyControl(string key)
    {
        this.Width = 420;
        this.Height = 45;

        var lblKey = new Label
        {
            Text = key,
            Width = 80,
            Left = 0,
            Top = 10
        };

        _trackBar = new TrackBar
        {
            Minimum = 1,
            Maximum = 150,
            Value = 10,
            Width = 200,
            Left = 85,
            Top = 0,
            TickFrequency = 10,
            Height = 30
        };

        _lblInterval = new Label
        {
            Text = $"{_trackBar.Value} ms",
            Left = 290,
            Top = 10,
            Width = 50
        };

        _trackBar.Scroll += (s, e) =>
        {
            _lblInterval.Text = $"{_trackBar.Value} ms";
            OnIntervalChanged?.Invoke(_trackBar.Value);
        };

        var btn = new Button
        {
            Text = "삭제",
            Width = 50,
            Left = 350,
            Top = 10
        };
        btn.Click += (s, e) => OnDelete?.Invoke();

        this.Controls.Add(lblKey);
        this.Controls.Add(_trackBar);
        this.Controls.Add(_lblInterval);
        this.Controls.Add(btn);
    }
}


public class TurboKeyItem
{
    private readonly string key;
    private int interval;
    private static Dictionary<string, int> activeTimers = new();

    // Multimedia Timer 관련
    [System.Runtime.InteropServices.DllImport("winmm.dll", SetLastError = true)]
    private static extern int timeSetEvent(uint delay, uint resolution, TimerCallback callback, IntPtr user, uint fuEvent, uint fuFlags);

    [System.Runtime.InteropServices.DllImport("winmm.dll", SetLastError = true)]
    private static extern void timeKillEvent(int id);

    private delegate void TimerCallback(uint id, uint msg, IntPtr user, IntPtr param1, IntPtr param2);

    private int _timerId = 0;
    private TimerCallback? _callback;

    public TurboKeyItem(string key, int interval)
    {
        this.key = key;
        this.interval = interval;
    }

    public void UpdateInterval(int v)
    {
        interval = v;
        // 재시작 시 반영됨
    }

    public void Start()
    {
        lock (activeTimers)
        {
            if (activeTimers.ContainsKey(key))
                return;

            _callback = new TimerCallback((id, msg, user, param1, param2) =>
            {
                try
                {
                    if (key.StartsWith("mouse_"))
                    {
                        var btn = key.Replace("mouse_", "");
                        if (IsMouseButtonPressed(btn))
                            MouseClick(btn);
                        else
                            Stop(); // 누르고 있지 않으면 정지
                    }
                    else if (key.StartsWith("keyboard_"))
                    {
                        var btn = key.Replace("keyboard_", "");
                        if (IsKeyboardKeyPressed(btn))
                            SendKeyPress(btn);
                        else
                            Stop(); // 누르고 있지 않으면 정지
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[TurboTimer] Error: {ex.Message}");
                    Stop();
                }
            });

            _timerId = timeSetEvent((uint)interval, 0, _callback, IntPtr.Zero,
                1 /* TIME_PERIODIC */,
                0x0001 /* TIME_CALLBACK_FUNCTION */);

            if (_timerId != 0)
                activeTimers[key] = _timerId;
        }
    }

    public void Stop()
    {
        lock (activeTimers)
        {
            if (activeTimers.TryGetValue(key, out var id))
            {
                timeKillEvent(id);
                activeTimers.Remove(key);
            }
        }
    }

    private bool IsMouseButtonPressed(string btn)
    {
        return btn switch
        {
            "LButton" => (GetAsyncKeyState(0x01) & 0x8000) != 0,
            "RButton" => (GetAsyncKeyState(0x02) & 0x8000) != 0,
            "MButton" => (GetAsyncKeyState(0x04) & 0x8000) != 0,
            _ => false
        };
    }

    private bool IsKeyboardKeyPressed(string keyName)
    {
        if (Enum.TryParse(typeof(Keys), keyName, out var result))
        {
            int vkey = (int)(Keys)result;
            return (GetAsyncKeyState(vkey) & 0x8000) != 0;
        }
        return false;
    }

    private void MouseClick(string btn)
    {
        const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        const uint MOUSEEVENTF_LEFTUP = 0x0004;
        const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        const uint MOUSEEVENTF_MIDDLEUP = 0x0040;

        uint down = 0, up = 0;

        switch (btn)
        {
            case "LButton":
                down = MOUSEEVENTF_LEFTDOWN;
                up = MOUSEEVENTF_LEFTUP;
                break;
            case "RButton":
                down = MOUSEEVENTF_RIGHTDOWN;
                up = MOUSEEVENTF_RIGHTUP;
                break;
            case "MButton":
                down = MOUSEEVENTF_MIDDLEDOWN;
                up = MOUSEEVENTF_MIDDLEUP;
                break;
        }

        mouse_event(down, 0, 0, 0, 0);
    }

    private void SendKeyPress(string keyName)
    {
        if (Enum.TryParse(typeof(Keys), keyName, out var result))
        {
            Keys key = (Keys)result;
            keybd_event((byte)key, 0, 0, 0);           // KEYDOWN
            Thread.Sleep(5);                           // 짧은 지연
            keybd_event((byte)key, 0, 0x0002, 0);       // KEYUP
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);
}

public static class MouseStateHelper
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public static List<string> GetPressedButtons()
    {
        var list = new List<string>();
        if ((GetAsyncKeyState(0x01) & 0x8000) != 0) list.Add("LButton");
        if ((GetAsyncKeyState(0x02) & 0x8000) != 0) list.Add("RButton");
        if ((GetAsyncKeyState(0x04) & 0x8000) != 0) list.Add("MButton");
        return list;
    }
}

public static class KeyboardStateHelper
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public static List<string> GetPressedKeys()
    {
        var list = new List<string>();
        foreach (Keys key in Enum.GetValues(typeof(Keys)))
        {
            if ((GetAsyncKeyState((int)key) & 0x8000) != 0)
                list.Add(key.ToString());
        }
        return list;
    }
}
