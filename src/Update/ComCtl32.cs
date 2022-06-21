using System;

namespace Squirrel.Update
{
    internal enum BOOL : int
    {
        FALSE = 0,
        TRUE = 1,
    }

    internal static partial class ComCtl32
    {
        public delegate IntPtr SUBCLASSPROC(
            IntPtr hWnd,
            int msg,
            IntPtr wParam,
            IntPtr lParam,
            UIntPtr uIdSubclass,
            UIntPtr dwRefData
        );

        [System.Runtime.InteropServices.DllImport("comctl32.dll", ExactSpelling = true)]
        public static extern BOOL SetWindowSubclass(
            IntPtr hWnd,
            IntPtr pfnSubclass,
            UIntPtr uIdSubclass,
            UIntPtr dwRefData
        );

        [System.Runtime.InteropServices.DllImport("comctl32.dll", ExactSpelling = true)]
        public static extern BOOL RemoveWindowSubclass(
            IntPtr hWnd,
            IntPtr pfnSubclass,
            UIntPtr uIdSubclass
        );

        [System.Runtime.InteropServices.DllImport("comctl32.dll", ExactSpelling = true)]
        public static extern IntPtr DefSubclassProc(
            IntPtr hWnd,
            int msg,
            IntPtr wParam,
            IntPtr lParam
        );
    }
}
