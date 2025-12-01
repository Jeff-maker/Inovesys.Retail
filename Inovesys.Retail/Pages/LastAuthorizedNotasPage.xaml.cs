using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inovesys.Retail.Entities;
using Inovesys.Retail.Services;
using Plugin.Maui.KeyListener;
using Microsoft.Maui.Dispatching; // Dispatcher
// ... outros usings

namespace Inovesys.Retail.Pages;

public partial class LastAuthorizedNotasPage : ContentPage
{
    public event Action<Invoice> NotaSelecionada;
    public event Action PageClosed;

    private List<Invoice> _invoices = new();
    private List<Invoice> _filteredInvoices = new();

    private readonly KeyboardBehavior _keyboardBehavior = new KeyboardBehavior();

    LiteDbService _db;
    UserConfig _userConfig;

    // debounce token
    private CancellationTokenSource _searchCts;

    public LastAuthorizedNotasPage(LiteDbService db, UserConfig userConfig)
    {
        InitializeComponent();

        var coll = db.GetCollection<Invoice>("invoice");

        // BUSCA AS 10 ÚLTIMAS INVOICES
        _invoices = coll
            .Find(i =>
                i.ClientId == userConfig.ClientId &&
                i.CompanyId == userConfig.DefaultCompanyId &&
                i.BrancheId == userConfig.DefaultBranche)
            .OrderByDescending(i => i.AuthorizationDate ?? i.IssueDate)
            .Take(50)
            .ToList();

        // inicialmente os filtrados são todos
        _filteredInvoices = new List<Invoice>(_invoices);
        NotasList.ItemsSource = _filteredInvoices;

        this.Loaded += LastAuthorizedNotasView_Loaded;
        _db = db;
        _userConfig = userConfig;
    }

    private async Task QueryAndApplyAsync(string query, CancellationToken token)
    {
        // executa a consulta no thread pool
        var results = await Task.Run(() =>
        {
            var coll = _db.GetCollection<Invoice>("invoice");

            // pega tudo (ou uma amostra) e aplica filtro em memória via LINQ
            var all = coll.FindAll(); // <-- NÃO passa Func aqui!

            // predicate base para cliente/empresa/filial
            bool BasePred(Invoice i) =>
                i.ClientId == _userConfig.ClientId &&
                i.CompanyId == _userConfig.DefaultCompanyId &&
                i.BrancheId == _userConfig.DefaultBranche;

            IEnumerable<Invoice> filtered;

            var q = (query ?? "").Trim();

            filtered = all.Where(i =>
                BasePred(i) &&
                !string.IsNullOrEmpty(i.Nfe) &&
                i.Nfe.Contains(q, StringComparison.OrdinalIgnoreCase)
            )
            .OrderByDescending(i => i.AuthorizationDate ?? i.IssueDate);

            return filtered.ToList();
        }, token);

        if (token.IsCancellationRequested) return;

        // atualiza UI via dispatcher
        await Dispatcher.DispatchAsync(() =>
        {
            try
            {
                _filteredInvoices = results ?? new List<Invoice>();
                NotasList.ItemsSource = _filteredInvoices;

                if (_filteredInvoices.Any())
                {
                    var first = _filteredInvoices.First();
                    _ = Dispatcher.DispatchAsync(async () =>
                    {
                        await Task.Delay(80);
                        try
                        {
                            NotasList.SelectedItem = first;
                            NotasList.ScrollTo(first, position: ScrollToPosition.Start, animate: true);
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                NotasList.Focus();
                                var sel = NotasList.SelectedItem;
                                NotasList.SelectedItem = null;
                                NotasList.SelectedItem = sel;
                            });
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Erro ao selecionar primeiro item após query: {ex}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao aplicar resultados da query: {ex}");
            }
        });
    }


    // ---------- Handlers de pesquisa ----------
    // Executado a cada alteração no texto (debounced)
    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        //DebounceAndQuery(e.NewTextValue);
    }
    bool entered = false;
    // Executado quando o usuário pressiona Search no teclado
    private async void OnSearchButtonPressed(object sender, EventArgs e)
    {
        entered = true;
        _searchCts?.Cancel();
        // chama e espera a consulta ao DB que atualiza a UI
        await QueryAndApplyAsync(NotaSearchBar?.Text ?? string.Empty, CancellationToken.None);
        entered = false;
    }


    // ---------- resto do seu código (Loaded, KeyUp, OnNotaTapped, etc) ----------
    private void LastAuthorizedNotasView_Loaded(object? sender, EventArgs e)
    {
        this.Loaded -= LastAuthorizedNotasView_Loaded;

        if (_filteredInvoices == null || !_filteredInvoices.Any())
            return;

        var first = _filteredInvoices.First();

        Dispatcher.DispatchAsync(async () =>
        {
            try
            {
                await Task.Delay(120);
                NotasList.SelectedItem = first;
                NotasList.ScrollTo(first, position: ScrollToPosition.Start, animate: false);
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    NotasList.Focus();
                    var selected = NotasList.SelectedItem;
                    NotasList.SelectedItem = null;
                    NotasList.SelectedItem = selected;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao focar o primeiro item: {ex}");
            }
        });

        if (!this.Behaviors.Contains(_keyboardBehavior))
        {
            _keyboardBehavior.KeyUp += OnKeyUp;
            this.Behaviors.Add(_keyboardBehavior);
        }
    }

    private void OnKeyUp(object sender, KeyPressedEventArgs args)
    {
        if (args.Keys == KeyboardKeys.Return)
        {
            if (NotaSearchBar.IsFocused && string.IsNullOrEmpty(NotaSearchBar.Text))
            {
                // campo de busca está focado e está vazio → não faz nada
                args.Handled = true;
                return;
            }
            if (entered)
            {
                // se o usuário não pressionou Enter para buscar, ignora
                args.Handled = true;
                return;
            }
            if (NotasList.SelectedItem is Invoice nota)
                NotaSelecionada?.Invoke(nota);

            args.Handled = true;
            return;
        }

        if (args.Keys == KeyboardKeys.Escape)
        {
            PageClosed?.Invoke();
            args.Handled = true;
            return;
        }
    }


    private void OnNotaTapped(object sender, EventArgs e)
    {
        if (sender is Border border && border.BindingContext is Invoice nota)
            NotaSelecionada?.Invoke(nota);
    }

    private void OnCloseClicked(object sender, EventArgs e)
    {
        PageClosed?.Invoke();
    }
}
