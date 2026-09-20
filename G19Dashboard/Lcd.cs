using System;
using System.Runtime.InteropServices;
public static class LcdProbe {
 const string D=@"C:\Program Files\Logitech Gaming Software\SDK\LCD\x64\LogitechLcd.dll";
 [DllImport(D,CharSet=CharSet.Unicode,CallingConvention=CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.I1)] public static extern bool LogiLcdInit(string name,int type);
 [DllImport(D,CallingConvention=CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.I1)] public static extern bool LogiLcdIsConnected(int type);
 [DllImport(D,CharSet=CharSet.Unicode,CallingConvention=CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.I1)] public static extern bool LogiLcdColorSetTitle(string text,int r,int g,int b);
 [DllImport(D,CharSet=CharSet.Unicode,CallingConvention=CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.I1)] public static extern bool LogiLcdColorSetText(int line,string text,int r,int g,int b);
 [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern void LogiLcdUpdate();
 [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern void LogiLcdShutdown();
}
