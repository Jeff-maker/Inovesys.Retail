using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using Microsoft.Maui.Controls.Shapes;

public class ToastService
{

    public async Task ShowToastAsync(string message)
    {
        var toast = Toast.Make(
            message,
            ToastDuration.Short,
            14 // tamanho da fonte opcional
        );
        await toast.Show();
    }

}


