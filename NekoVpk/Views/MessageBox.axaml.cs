using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using MsBox.Avalonia.Enums;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon; 

namespace NekoVpk.Views;

public partial class CustomMessageBox : Window
{
    public CustomMessageBox()
    {
        InitializeComponent();
    }

    public CustomMessageBox(string title, string message, ButtonEnum buttons, MsBoxIcon icon = MsBoxIcon.None, bool isDanger = false)
    {
        InitializeComponent();
        
        TitleBlock.Text = title;
        MessageBlock.Text = message;

        if (icon == MsBoxIcon.Warning || icon == MsBoxIcon.Error)
        {
            IconBlock.IsVisible = true;
            IconBlock.Text = icon == MsBoxIcon.Warning ? "⚠️" : "❌";
        }
        else if (icon == MsBoxIcon.Info || icon == MsBoxIcon.Success)
        {
            IconBlock.IsVisible = true;
            IconBlock.Text = icon == MsBoxIcon.Info ? "❔" : "✔";
        }

        if (buttons == ButtonEnum.YesNo)
        {
            YesBtn.IsVisible = true;
            NoBtn.IsVisible = true;
            OkBtn.IsVisible = false;
        }
        else if (buttons == ButtonEnum.Ok)
        {
            YesBtn.IsVisible = false;
            NoBtn.IsVisible = false;
            OkBtn.IsVisible = true;
        }

        if (isDanger)
        {
            YesBtn.Classes.Remove("Primary");
            YesBtn.Classes.Add("Danger");
        }
    }

    public CustomMessageBox(string title, string message, string yesText, string noText, string? thirdText = null, bool isDanger = false)
    {
        InitializeComponent();
        
        TitleBlock.Text = title;
        MessageBlock.Text = message;

        YesBtn.IsVisible = true;
        NoBtn.IsVisible = true;

        YesBtnText.Text = yesText;
        NoBtnText.Text = noText;

        if (!string.IsNullOrEmpty(thirdText))
        {
            OkBtn.IsVisible = true;
            OkBtnText.Text = thirdText;
        }
        else
        {
            OkBtn.IsVisible = false;
        }

        if (isDanger)
        {
            IconBlock.IsVisible = true;
            IconBlock.Text = "⚠️";
            
            if (SweepLightRect.Fill is Avalonia.Media.ConicGradientBrush brush)
            {
                brush.GradientStops[2].Color = Avalonia.Media.Color.Parse("#4DDC2626");
            }
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void Button_Click(object? sender, RoutedEventArgs e)
    {
        if (sender == YesBtn)
            Close(ButtonResult.Yes);
        else if (sender == NoBtn)
            Close(ButtonResult.No);
        else if (sender == OkBtn)
            Close(ButtonResult.Ok);
        else
            Close(ButtonResult.None);
    }
}