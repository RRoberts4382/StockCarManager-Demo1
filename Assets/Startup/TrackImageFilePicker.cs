using System;
using System.Runtime.InteropServices;
using System.Text;

internal static class TrackImageFilePicker
{
    public static string Pick(string prompt = "Choose a track diagram") =>
        Pick(prompt, "", "Images (*.png;*.jpg;*.jpeg)", "png,jpg,jpeg");

    internal static string Pick(string prompt, string initialFolder, params string[] filters)
    {
#if UNITY_EDITOR
        return UnityEditor.EditorUtility.OpenFilePanelWithFilters(prompt, initialFolder, filters);
#elif UNITY_STANDALONE_WIN
        const int maxPathCharacters = 32768;
        var filterBuilder = new StringBuilder();
        for (int i = 0; i < filters.Length; i += 2)
        {
            filterBuilder.Append(filters[i]).Append('\0');
            filterBuilder.Append("*.").Append(filters[i + 1].Replace(",", ";*.")).Append('\0');
        }
        char[] filterText = filterBuilder.Append('\0').ToString().ToCharArray();
        IntPtr file = IntPtr.Zero;
        IntPtr filter = IntPtr.Zero;
        IntPtr title = IntPtr.Zero;
        IntPtr initialDirectory = IntPtr.Zero;
        try
        {
            // Keep large dialog buffers off Unity's managed thread stack.
            file = Marshal.AllocHGlobal(maxPathCharacters * sizeof(char));
            Marshal.WriteInt16(file, 0);
            filter = Marshal.AllocHGlobal(filterText.Length * sizeof(char));
            Marshal.Copy(filterText, 0, filter, filterText.Length);
            title = Marshal.StringToHGlobalUni(prompt);
            if (!string.IsNullOrEmpty(initialFolder))
                initialDirectory = Marshal.StringToHGlobalUni(initialFolder);
            var dialog = new OpenFileName
            {
                size = Marshal.SizeOf<OpenFileName>(),
                owner = GetActiveWindow(),
                filter = filter,
                filterIndex = 1,
                file = file,
                maxFile = maxPathCharacters,
                title = title,
                initialDirectory = initialDirectory,
                flags = 0x00081808 // Explorer, existing file and path, preserve the working directory.
            };
            if (GetOpenFileName(ref dialog))
                return Marshal.PtrToStringUni(file);

            uint error = CommDlgExtendedError();
            if (error != 0)
                throw new ArgumentException($"Windows could not open the file picker (error {error}).");
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(initialDirectory);
            Marshal.FreeHGlobal(title);
            Marshal.FreeHGlobal(filter);
            Marshal.FreeHGlobal(file);
        }
#else
        throw new ArgumentException("File picking is available in the Unity Editor and Windows builds.");
#endif
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int size;
        public IntPtr owner;
        public IntPtr instance;
        public IntPtr filter;
        public IntPtr customFilter;
        public int maxCustomFilter;
        public int filterIndex;
        public IntPtr file;
        public int maxFile;
        public IntPtr fileTitle;
        public int maxFileTitle;
        public IntPtr initialDirectory;
        public IntPtr title;
        public uint flags;
        public ushort fileOffset;
        public ushort fileExtension;
        public IntPtr defaultExtension;
        public IntPtr customData;
        public IntPtr hook;
        public IntPtr templateName;
        public IntPtr reserved;
        public uint reservedFlags;
        public uint extendedFlags;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetOpenFileNameW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileName(ref OpenFileName dialog);

    [DllImport("comdlg32.dll")]
    private static extern uint CommDlgExtendedError();

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();
#endif
}
