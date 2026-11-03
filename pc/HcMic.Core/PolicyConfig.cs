using System;
using System.Runtime.InteropServices;

namespace HcMic.Core
{
    // PolicyConfig COM（AudioSes.dll，Win7+）；用于管理端点可见性
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9"), ClassInterface(ClassInterfaceType.None)]
    public class PolicyConfigClient { }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPolicyConfig
    {
        int GetMixFormat([In, MarshalAs(UnmanagedType.LPWStr)] string wzDeviceId, out IntPtr ppFormat);
        int GetDeviceFormat([In, MarshalAs(UnmanagedType.LPWStr)] string wzDeviceId, int bDefault, out IntPtr ppFormat);
        int ResetDeviceFormat([In, MarshalAs(UnmanagedType.LPWStr)] string wzDeviceId);
        int SetDeviceFormat([In, MarshalAs(UnmanagedType.LPWStr)] string wzDeviceId, IntPtr pFormat, IntPtr pOldFormat);
        int GetProcessingPeriod([In, MarshalAs(UnmanagedType.LPWStr)] string wzDeviceId, IntPtr a, IntPtr b, IntPtr c);
        int SetProcessingPeriod([In, MarshalAs(UnmanagedType.LPWStr)] string wzDeviceId, IntPtr period);
        int GetShareMode([In, MarshalAs(UnmanagedType.LPWStr)] string wzDeviceId, IntPtr pMode);
        int SetShareMode([In, MarshalAs(UnmanagedType.LPWStr)] string wzDeviceId, IntPtr mode);
        int GetPropertyValue([In, MarshalAs(UnmanagedType.LPWStr)] string wzDeviceId, IntPtr pKey, IntPtr pValue);
        int SetPropertyValue([In, MarshalAs(UnmanagedType.LPWStr)] string wzDeviceId, IntPtr pKey, IntPtr pValue);
        int SetDefaultEndpoint([In, MarshalAs(UnmanagedType.LPWStr)] string wzDeviceId, uint eRole);
        int SetEndpointVisibility([In, MarshalAs(UnmanagedType.LPWStr)] string wzDeviceId, int bVisible); // 1=显示 0=隐藏
    }
}
