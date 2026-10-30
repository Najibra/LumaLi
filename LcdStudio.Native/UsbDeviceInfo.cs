namespace LcdStudio.Native;

public sealed record UsbDeviceInfo(DeviceKind Kind, string Path, string Serial, string Name);
