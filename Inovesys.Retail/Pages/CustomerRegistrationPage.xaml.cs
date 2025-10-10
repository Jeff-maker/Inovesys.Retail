using Inovesys.Retail.Services;

namespace Inovesys.Retail.Pages;

public partial class CustomerRegistrationPage : ContentPage
{

    private LiteDbService _db;
	

    public CustomerRegistrationPage(string cpf, LiteDbService liteDatabase)
	{
        
        InitializeComponent();

        DocumentEntry.Text = cpf;

        _db = liteDatabase;
	}

    private void OnSaveClicked(object sender, EventArgs e)
    {
        
    }
}