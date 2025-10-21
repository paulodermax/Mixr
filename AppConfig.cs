public class AppConfig
{
    public List<string> slider_mapping { get; set; } = new();
    public Dictionary<string, List<string>> session_groups { get; set; } = new();
    public string com_port { get; set; } = "";
    public int baud_rate { get; set; }
    public bool invert_sliders { get; set; }
    public string noise_reduction { get; set; } = "";
}
