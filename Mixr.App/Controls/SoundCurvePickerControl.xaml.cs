using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mixr.Services;

namespace Mixr_App.Controls;

public sealed partial class SoundCurvePickerControl : UserControl
{
    Action<string>? _onCurveSelected;
    bool _suppressSelection;

    public SoundCurvePickerControl()
    {
        InitializeComponent();
        BuildCurveRadioButtons();
    }

    public void Bind(int sliderIndex, string sliderTitle, string currentYamlKey, Action<string> onCurveSelected)
    {
        _onCurveSelected = onCurveSelected;
        HeaderText.Text = $"Sound-Mapping — Slider {sliderIndex + 1}: {sliderTitle}";

        _suppressSelection = true;
        try
        {
            var yamlKey = VolumeCurveMapper.ToYamlKey(VolumeCurveMapper.Parse(currentYamlKey));
            for (var i = 0; i < CurveRadioButtons.Items.Count; i++)
            {
                if (CurveRadioButtons.Items[i] is RadioButton rb &&
                    rb.Tag is string tag &&
                    tag.Equals(yamlKey, StringComparison.OrdinalIgnoreCase))
                {
                    CurveRadioButtons.SelectedIndex = i;
                    break;
                }
            }
        }
        finally
        {
            _suppressSelection = false;
        }
    }

    void BuildCurveRadioButtons()
    {
        CurveRadioButtons.Items.Clear();
        foreach (var preset in VolumeCurveMapper.Presets)
        {
            CurveRadioButtons.Items.Add(new RadioButton
            {
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = preset.Title,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        },
                        new TextBlock
                        {
                            Text = preset.Description,
                            FontSize = 12,
                            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                            TextWrapping = TextWrapping.Wrap,
                        },
                    },
                },
                Tag = preset.YamlKey,
            });
        }
    }

    void CurveRadioButtons_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_suppressSelection || CurveRadioButtons.SelectedIndex < 0)
            return;

        if (CurveRadioButtons.SelectedItem is not RadioButton rb || rb.Tag is not string yamlKey)
            return;

        _onCurveSelected?.Invoke(yamlKey);
    }
}
