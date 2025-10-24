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



    public async Task ShowToast(string message, int durationInSeconds = 2)
    {
        var toastLabel = new Label
        {
            Text = message,
            TextColor = Colors.Black, // 🔧 Letra preta
            Padding = new Thickness(16),
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };

        var toastBorder = new Border
        {
            Background = Colors.White, // 🔧 Fundo branco
            StrokeShape = new RoundRectangle
            {
                CornerRadius = new CornerRadius(12)
            },
            Margin = new Thickness(20),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center, // 👈 Centralizado na tela
            Content = toastLabel,
            Opacity = 0,
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Opacity = 0.3f,
                Offset = new Point(4, 4),
                Radius = 6
            }
        };

        // Acessa a janela principal e faz o cast para ContentPage
        var page = Application.Current?.Windows[0]?.Page;

        if (page is not null)
        {
            var layout = GetLayoutFromPage(page);
            if (layout is not null)
            {
                layout.Children.Add(toastBorder);

                await toastBorder.FadeTo(1, 250);
                await Task.Delay(durationInSeconds * 1000);
                await toastBorder.FadeTo(0, 250);

                layout.Children.Remove(toastBorder);
            }
        }
    }

    private static Layout? GetLayoutFromPage(Page page)
    {
        // Se for ContentPage e o Content for Layout
        if (page is ContentPage cp && cp.Content is Layout layout)
            return layout;

        // Se for ContentPage com ScrollView → procurar o Layout interno
        if (page is ContentPage cpScroll && cpScroll.Content is ScrollView scroll &&
            scroll.Content is Layout scrollLayout)
            return scrollLayout;

        // Se for NavigationPage
        if (page is NavigationPage nav &&
            nav.CurrentPage is ContentPage navContent)
        {
            if (navContent.Content is Layout navLayout)
                return navLayout;

            if (navContent.Content is ScrollView navScroll &&
                navScroll.Content is Layout navScrollLayout)
                return navScrollLayout;
        }

        // Se for Shell
        if (page is Shell shell &&
            shell.CurrentPage is ContentPage shellContent)
        {
            if (shellContent.Content is Layout shellLayout)
                return shellLayout;

            if (shellContent.Content is ScrollView shellScroll &&
                shellScroll.Content is Layout shellScrollLayout)
                return shellScrollLayout;
        }

        return null;
    }


}


