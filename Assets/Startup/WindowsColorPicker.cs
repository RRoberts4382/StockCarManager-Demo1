using System;
using System.Runtime.InteropServices;
using UnityEngine;

internal static class WindowsColorPicker
{
    public static bool TryPick(Color32 initial, out Color32 selected)
    {
        selected = initial;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        IntPtr custom = Marshal.AllocHGlobal(16 * sizeof(int));
        try
        {
            for (int i = 0; i < 16; i++)
                Marshal.WriteInt32(custom, i * sizeof(int), 0x00FFFFFF);

            var dialog = new ChooseColorData
            {
                Size = Marshal.SizeOf<ChooseColorData>(),
                Owner = GetActiveWindow(),
                Result = (uint)(initial.r | initial.g << 8 | initial.b << 16),
                CustomColors = custom,
                Flags = 0x03 // CC_RGBINIT | CC_FULLOPEN
            };

            if (!ChooseColor(ref dialog))
            {
                uint error = CommDlgExtendedError();
                if (error != 0)
                    throw new InvalidOperationException($"Windows could not open the color picker (error {error}).");
                return false;
            }

            selected = new Color32((byte)(dialog.Result & 255), (byte)((dialog.Result >> 8) & 255),
                (byte)((dialog.Result >> 16) & 255), 255);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(custom);
        }
#else
        throw new InvalidOperationException("The color picker is available on Windows. Enter a hex color in the field.");
#endif
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    [StructLayout(LayoutKind.Sequential)]
    private struct ChooseColorData
    {
        public int Size;
        public IntPtr Owner;
        public IntPtr Instance;
        public uint Result;
        public IntPtr CustomColors;
        public uint Flags;
        public IntPtr CustomData;
        public IntPtr Hook;
        public IntPtr Template;
    }

    [DllImport("comdlg32.dll", EntryPoint = "ChooseColorW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChooseColor(ref ChooseColorData dialog);

    [DllImport("comdlg32.dll")]
    private static extern uint CommDlgExtendedError();

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();
#endif
}
