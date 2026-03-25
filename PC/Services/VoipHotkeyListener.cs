using System.Runtime.InteropServices;

namespace Mixr.Services;

/// <summary>
/// Strg+Linksshift+Alt+9 (Mute) / +0 (Deafen). Ziffernreihe oder Numpad (VK 0x39/0x30 bzw. 0x69/0x60).
/// Modifier nur links: VK_LCONTROL / VK_LSHIFT / VK_LMENU.
/// </summary>
public static class VoipHotkeyListener
{
    const int WhKeyboardLl = 13;
    const int WmKeydown = 0x0100;
    const int WmSyskeydown = 0x0104;
    const uint LlkhfInjected = 0x10;

    const int VkLControl = 0xA2;
    const int VkLShift = 0xA0;
    const int VkLMenu = 0xA4;

    const uint VkNumpad9 = 0x69;
    const uint VkNumpad0 = 0x60;

    const uint VkMute = DiscordHotkeySimulator.VkMuteKey;
    const uint VkDeafen = DiscordHotkeySimulator.VkDeafenKey;

    private const uint MapvkVkToVsc = 0;

    static Action? _onMuteUi;
    static Action? _onDeafenUi;
    static IntPtr _hookId = IntPtr.Zero;
    static long _lastMuteMs;
    static long _lastDeafenMs;
    const int DebounceMs = 400;

    static readonly LowLevelKeyboardProc Proc = HookCallback;

    delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    public static void Start(Action onMuteUi, Action onDeafenUi)
    {
        _onMuteUi = onMuteUi;
        _onDeafenUi = onDeafenUi;

        var t = new Thread(HookThread)
        {
            IsBackground = true,
            Name = "MixrVoipHotkeys",
        };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    static void HookThread()
    {
        _hookId = SetWindowsHookEx(WhKeyboardLl, Proc, GetModuleHandle(IntPtr.Zero), 0);
        if (_hookId == IntPtr.Zero)
        {
            Console.Error.WriteLine(
                "[Mixr] VoIP-Tastatur-Hook nicht gesetzt (SetWindowsHookEx fehlgeschlagen).");
            return;
        }

        MSG msg;
        while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WmKeydown || wParam == (IntPtr)WmSyskeydown))
        {
            var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if ((kb.flags & LlkhfInjected) != 0)
                return CallNextHookEx(_hookId, nCode, wParam, lParam);

            bool ctrl = (GetAsyncKeyState(VkLControl) & 0x8000) != 0;
            bool shift = (GetAsyncKeyState(VkLShift) & 0x8000) != 0;
            bool alt = (GetAsyncKeyState(VkLMenu) & 0x8000) != 0;
            if (!ctrl || !shift || !alt)
                return CallNextHookEx(_hookId, nCode, wParam, lParam);

            long now = Environment.TickCount64;
            uint vk = kb.vkCode;
            uint scanLow = kb.scanCode & 0xFFU;

            uint muteScanMain = MapVirtualKey(VkMute, MapvkVkToVsc) & 0xFFU;
            uint muteScanNum = MapVirtualKey(VkNumpad9, MapvkVkToVsc) & 0xFFU;
            uint deafenScanMain = MapVirtualKey(VkDeafen, MapvkVkToVsc) & 0xFFU;
            uint deafenScanNum = MapVirtualKey(VkNumpad0, MapvkVkToVsc) & 0xFFU;

            bool isMuteKey = vk == VkMute || vk == VkNumpad9
                || (muteScanMain != 0 && scanLow == muteScanMain)
                || (muteScanNum != 0 && scanLow == muteScanNum);

            bool isDeafenKey = vk == VkDeafen || vk == VkNumpad0
                || (deafenScanMain != 0 && scanLow == deafenScanMain)
                || (deafenScanNum != 0 && scanLow == deafenScanNum);

            if (isDeafenKey)
            {
                if (now - _lastDeafenMs >= DebounceMs)
                {
                    _lastDeafenMs = now;
                    _onDeafenUi?.Invoke();
                }
            }
            else if (isMuteKey)
            {
                if (now - _lastMuteMs >= DebounceMs)
                {
                    _lastMuteMs = now;
                    _onMuteUi?.Invoke();
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public UIntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int x;
        public int y;
    }

    [DllImport("user32.dll")]
    static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll", SetLastError = true)]
    static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(IntPtr lpModuleName);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);
}
