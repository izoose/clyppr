using NAudio.CoreAudioApi;

namespace Clipper.App;

/// <summary>One selectable microphone in Settings (null Id = the Windows default device).</summary>
public sealed class MicDeviceItem
{
    public string? Id { get; init; }
    public string Name { get; init; } = "";
    public override string ToString() => Name;
}

public static class MicDevices
{
    public static List<MicDeviceItem> List()
    {
        var list = new List<MicDeviceItem> { new() { Id = null, Name = "System default" } };
        try
        {
            using var en = new MMDeviceEnumerator();
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                list.Add(new MicDeviceItem { Id = d.ID, Name = d.FriendlyName });
        }
        catch { /* enumeration is best-effort; default always present */ }
        return list;
    }
}
