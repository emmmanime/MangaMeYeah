using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;

namespace MangaMeYeah.Services;

public static class NativeColorDialogService
{
    [DllImport("comdlg32.dll", SetLastError = true)]
    private static extern bool ChooseColor(ref CHOOSECOLOR lpcc);

    [StructLayout(LayoutKind.Sequential)]
    private struct CHOOSECOLOR
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public int rgbResult;
        public IntPtr lpCustColors;
        public int Flags;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
    }

    private const int CC_RGBINIT = 0x00000001;
    private const int CC_FULLOPEN = 0x00000002;
    private const int CC_ANYCOLOR = 0x00000100;

    // 16 custom color slots persisted across calls
    private static readonly int[] CustomColors = new int[16];

    public static Color? PromptColor(System.Windows.Window owner, Color initialColor)
    {
        var helper = new WindowInteropHelper(owner);
        var handle = helper.Handle;

        int initBgr = (initialColor.B << 16) | (initialColor.G << 8) | initialColor.R;

        var custColorsHandle = GCHandle.Alloc(CustomColors, GCHandleType.Pinned);
        try
        {
            var cc = new CHOOSECOLOR
            {
                lStructSize = Marshal.SizeOf<CHOOSECOLOR>(),
                hwndOwner = handle,
                rgbResult = initBgr,
                lpCustColors = custColorsHandle.AddrOfPinnedObject(),
                Flags = CC_RGBINIT | CC_FULLOPEN | CC_ANYCOLOR
            };

            if (ChooseColor(ref cc))
            {
                byte r = (byte)(cc.rgbResult & 0xFF);
                byte g = (byte)((cc.rgbResult >> 8) & 0xFF);
                byte b = (byte)((cc.rgbResult >> 16) & 0xFF);
                return Color.FromRgb(r, g, b);
            }
            return null;
        }
        finally
        {
            custColorsHandle.Free();
        }
    }
}
