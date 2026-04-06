using Mixr.Models;

namespace Mixr.Services;

public static class MixrConfigClone
{
    public static MixrConfig DeepClone(MixrConfig c)
    {
        return new MixrConfig
        {
            ComPort = c.ComPort,
            BaudRate = c.BaudRate,
            InvertSliders = c.InvertSliders,
            SliderMapping = new List<string>(c.SliderMapping),
            ButtonMapping = new List<string>(c.ButtonMapping),
            SessionGroups = c.SessionGroups.ToDictionary(
                kv => kv.Key,
                kv => new List<string>(kv.Value),
                StringComparer.OrdinalIgnoreCase),
        };
    }
}
