using System.Runtime.InteropServices;
using WindowsInput;
using WindowsInput.Native;

namespace Mixr.Services;

/// <summary>
/// Discord: Strg+Linksshift+Alt+9 (Mute), +0 (Deafen), +8 (Bildschirm teilen) — in Discord dieselben Shortcuts eintragen.
/// Simulation: LControl, LShift, LMenu + Taste; zuerst Scan-Codes, dann VK, zuletzt InputSimulator.
/// </summary>
public static class DiscordHotkeySimulator
{
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfScancode = 0x0008;

    private const uint VkLControl = 0xA2;
    private const uint VkLShift = 0xA0;
    private const uint VkLMenu = 0xA4;
    /// <summary>VK_9 — Mute (Ziffernreihe).</summary>
    public const uint VkMuteKey = 0x39;
    /// <summary>VK_0 — Deafen (Ziffernreihe).</summary>
    public const uint VkDeafenKey = 0x30;
    /// <summary>VK_8 — Bildschirm teilen (Ziffernreihe).</summary>
    public const uint VkShareScreenKey = 0x38;

    private const uint MapvkVkToVsc = 0;

    private static readonly InputSimulator Sim = new();
    private static readonly object Gate = new();

    public static void TriggerToggleMute()
    {
        lock (Gate)
        {
            if (TrySendCtrlShiftAltChordScan(VkMuteKey))
                return;
            if (TrySendCtrlShiftAltChordVk(VkMuteKey))
                return;
            Sim.Keyboard.ModifiedKeyStroke(
                new[] { VirtualKeyCode.LCONTROL, VirtualKeyCode.LSHIFT, VirtualKeyCode.LMENU },
                VirtualKeyCode.VK_9);
        }
    }

    public static void TriggerToggleDeafen()
    {
        lock (Gate)
        {
            if (TrySendCtrlShiftAltChordScan(VkDeafenKey))
                return;
            if (TrySendCtrlShiftAltChordVk(VkDeafenKey))
                return;
            Sim.Keyboard.ModifiedKeyStroke(
                new[] { VirtualKeyCode.LCONTROL, VirtualKeyCode.LSHIFT, VirtualKeyCode.LMENU },
                VirtualKeyCode.VK_0);
        }
    }

    public static void TriggerShareScreen()
    {
        lock (Gate)
        {
            if (TrySendCtrlShiftAltChordScan(VkShareScreenKey))
                return;
            if (TrySendCtrlShiftAltChordVk(VkShareScreenKey))
                return;
            Sim.Keyboard.ModifiedKeyStroke(
                new[] { VirtualKeyCode.LCONTROL, VirtualKeyCode.LSHIFT, VirtualKeyCode.LMENU },
                VirtualKeyCode.VK_8);
        }
    }

    static bool TrySendCtrlShiftAltChordScan(uint vk)
    {
        var scCtrl = VkToScancode(VkLControl);
        var scShift = VkToScancode(VkLShift);
        var scAlt = VkToScancode(VkLMenu);
        var scKey = VkToScancode(vk);
        if (scCtrl == 0 || scShift == 0 || scAlt == 0 || scKey == 0)
            return false;
        try
        {
            SendScan(scCtrl, false);
            StepDelay();
            SendScan(scShift, false);
            StepDelay();
            SendScan(scAlt, false);
            StepDelay();
            SendScan(scKey, false);
            StepDelay();
            SendScan(scKey, true);
            StepDelay();
            SendScan(scAlt, true);
            StepDelay();
            SendScan(scShift, true);
            StepDelay();
            SendScan(scCtrl, true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static ushort VkToScancode(uint vk) =>
        (ushort)(MapVirtualKey(vk, MapvkVkToVsc) & 0xFF);

    static void SendScan(ushort scan, bool keyUp)
    {
        uint flags = KeyeventfScancode;
        if (keyUp)
            flags |= KeyeventfKeyup;

        var input = new INPUT
        {
            type = InputKeyboard,
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = scan,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = UIntPtr.Zero,
            },
        };

        if (SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) == 0)
            throw new InvalidOperationException($"SendInput(Scan) Win32={Marshal.GetLastWin32Error()}");
    }

    static bool TrySendCtrlShiftAltChordVk(uint vk)
    {
        try
        {
            SendVk((ushort)VkLControl, false);
            StepDelay();
            SendVk((ushort)VkLShift, false);
            StepDelay();
            SendVk((ushort)VkLMenu, false);
            StepDelay();
            SendVk((ushort)vk, false);
            StepDelay();
            SendVk((ushort)vk, true);
            StepDelay();
            SendVk((ushort)VkLMenu, true);
            StepDelay();
            SendVk((ushort)VkLShift, true);
            StepDelay();
            SendVk((ushort)VkLControl, true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static void StepDelay() => Thread.Sleep(22);

    static void SendVk(ushort vk, bool keyUp)
    {
        uint flags = keyUp ? KeyeventfKeyup : 0;
        var input = new INPUT
        {
            type = InputKeyboard,
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = UIntPtr.Zero,
            },
        };

        if (SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) == 0)
            throw new InvalidOperationException($"SendInput(VK) Win32={Marshal.GetLastWin32Error()}");
    }

    [DllImport("user32.dll")]
    static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }
}
