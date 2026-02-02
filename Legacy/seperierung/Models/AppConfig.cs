using System.Collections.Generic;

namespace Mixr.Models
{
    public class AppConfig
    {
        public string com_port { get; set; } = "COM11";
        public int baud_rate { get; set; } = 230400;
        public List<string> slider_mapping { get; set; } = new();
        public Dictionary<string, List<string>> session_groups { get; set; } = new();
        public List<string> whitelist { get; set; } = new();
        public bool invert_sliders { get; set; } = true;
        public string noise_reduction { get; set; } = "default";
    }
}