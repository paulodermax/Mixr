public class AppConfig
{
    public string com_port { get; set; } = "";
    public int baud_rate { get; set; }
    public bool invert_sliders { get; set; }
    public string noise_reduction { get; set; } = "";
    public List<string> slider_mapping { get; set; } = new();
}
