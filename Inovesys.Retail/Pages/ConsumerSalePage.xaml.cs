using CommunityToolkit.Mvvm.Input;
using Inovesys.Retail.Entities;
using Inovesys.Retail.Models;
using Inovesys.Retail.Services;
using LiteDB;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;
using ZXing.Net.Maui;
using ZXing.Common;
using ZXing;
using Microsoft.Maui.Platform;
using System.Security.Cryptography.X509Certificates;

namespace Inovesys.Retail.Pages;

public partial class ConsumerSalePage : ContentPage
{
    private ObservableCollection<ConsumerSaleItem> _items = new();
    ProductRepositoryLiteDb _products;
    private readonly ObservableCollection<ProductSuggestion> ProductSuggestions = new();

    private LiteDbService _db;

    UserConfig userConfig;

    Client _client { set; get; }
    Company _company { set; get; }
    private readonly HttpClient _http;
    private X509Certificate2 _x509Certificate2;

    private Branche _branche { set; get; }

    // Defaults (pode ler de LiteDB/UserConfig depois)
    private const string DefaultPrinterName = "MP-4200 TH"; // nome no Windows

    private ToastService _toastService;

    private CancellationTokenSource? _typeDebounceCts;
    private const int DebounceMs = 300;

    private bool _suppressTextChanged = false;

    private bool _handlingUnfocus = false;

    // Terminadores comuns de scanner/simulador
    private static readonly char[] ScanTerminators = new[] { '\r', '\n', '\t', ';' };

    // Evita recursão quando setamos entryProductCode.Text programaticamente
    private bool _handlingScanEnd = false;


    public static readonly BindableProperty IsWorkingProperty =
        BindableProperty.Create(nameof(IsWorking), typeof(bool), typeof(ConsumerSalePage), false);

    public static readonly BindableProperty BusyTextProperty =
        BindableProperty.Create(nameof(BusyText), typeof(string), typeof(ConsumerSalePage), "Aguarde...");

    public bool IsWorking
    {
        get => (bool)GetValue(IsWorkingProperty);
        set => SetValue(IsWorkingProperty, value);
    }

    public string BusyText
    {
        get => (string)GetValue(BusyTextProperty);
        set => SetValue(BusyTextProperty, value);
    }

    // (opcional) contador para suportar busy aninhado
    private int _busyDepth = 0;
    private IDisposable BeginBusy(string text)
    {
        BusyText = text;
        Interlocked.Increment(ref _busyDepth);
        MainThread.BeginInvokeOnMainThread(() => IsWorking = true);
        return new ActionOnDispose(() =>
        {
            if (Interlocked.Decrement(ref _busyDepth) <= 0)
            {
                _busyDepth = 0;
                MainThread.BeginInvokeOnMainThread(() => { IsWorking = false; BusyText = "Aguarde..."; });
            }
        });
    }

    private sealed class ActionOnDispose : IDisposable
    {
        private readonly Action _onDispose;
        public ActionOnDispose(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => _onDispose();
    }



    public ConsumerSalePage(LiteDbService liteDatabase, ToastService toastService, IHttpClientFactory httpClientFactor, ProductRepositoryLiteDb products)
    {
        InitializeComponent();
        
        BindingContext = this;

        ItemsListView.ItemsSource = _items;
        UpdateTotal();
        _db = liteDatabase;
        _toastService = toastService;
        _http = httpClientFactor.CreateClient("api");
        _products = products;

        // escuta alterações na coleção(add / remove / reset) E em cada item
        _items.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (ConsumerSaleItem it in e.NewItems)
                    it.PropertyChanged += ItemOnPropertyChanged;

            if (e.OldItems != null)
                foreach (ConsumerSaleItem it in e.OldItems)
                    it.PropertyChanged -= ItemOnPropertyChanged;

            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                // reanexa (ex.: Clear)
                foreach (var it in _items)
                    it.PropertyChanged += ItemOnPropertyChanged;
            }
        };

        // comando (genérico string!)
        var searchCmd = new Command<string>(async term =>
        {
            if (_handlingScanEnd) return;

            if (_enter) return;

            // Fluxo normal: atualizar sugestões
            await SearchProductsAsync(term);
        });

        // liga direto no controle Zoft
        entryProductCode.TextChangedCommand = searchCmd;
      

    }

    private void ItemOnPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        UpdateTotal();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        userConfig = _db.GetCollection<UserConfig>("user_config").FindById("CURRENT");
        if (userConfig == null)
        {
            await _toastService.ShowToastAsync("Usuário não configurado.");
            return;
        }

        _client = _db.GetCollection<Client>("client").FindOne(c => c.Id == userConfig.ClientId);

        if (_client == null)
        {
            await _toastService.ShowToastAsync("Erro na leitura de cliente.");
            return;
        }

        _branche = _db.GetCollection<Branche>("branche").FindOne(b =>
            b.ClientId == userConfig.ClientId &&
            b.CompanyId == userConfig.DefaultCompanyId &&
            b.Id == userConfig.DefaultBranche);

        if (_branche == null)
        {
            await _toastService.ShowToastAsync("Erro na leitura de filial.");
            return;
        }
        else if (_company == null)
        {
            _company = _db.GetCollection<Company>("company").FindOne(c => c.Id == _branche.CompanyId && c.ClientId == _branche.ClientId);

            if (_company == null)
            {
                await _toastService.ShowToastAsync("Erro na leitura de empresa.");
                return;
            }
        }

        _x509Certificate2 = SefazHelpers.LoadCertificate(_company.CertificateId, _company.ClientId, _db);

        // ✅ Verifica se há notas fiscais pendentes de envio
        var invoiceCol = _db.GetCollection<Invoice>("invoice");
        bool hasPendingInvoice = invoiceCol.Exists(i =>
            i.ClientId == _branche.ClientId &&
            i.CompanyId == _branche.CompanyId &&
            i.BrancheId == _branche.Id &&
            i.NfeStatus == "NO_SEND");

        if (hasPendingInvoice)
        {

            bool enviarAgora = await DisplayAlert(
                       "Nota fiscal pendente",
                       "Há nota(s) fiscal(is) pendente(s) de envio para a SEFAZ.\nDeseja enviar agora?",
                       "Sim", "Não");

            if (enviarAgora)
            {
                // ⚠️ Aqui você pode chamar o serviço de envio, ex:
                var pendente = invoiceCol.FindOne(i =>
                    i.ClientId == _branche.ClientId &&
                    i.CompanyId == _branche.CompanyId &&
                    i.BrancheId == _branche.Id &&
                    i.NfeStatus == "NO_SEND");

                if (pendente != null)
                {
                    // Exemplo: chamar método fictício de envio
                    var sefaz = await EnviarNotaParaSefaz(pendente);
                    if (sefaz.Success)

                        await _toastService.ShowToastAsync("Nota enviada com sucesso.");
                    else
                        await _toastService.ShowToastAsync("Falha ao enviar nota.");
                }
            }
        }

        await CheckAndSendContingencyAsync();   // <— novo
        await CheckAndSendAuthorizedInvoicesAsync();

        await Task.Delay(100);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            entryCustomerCpf.Focus();
        });
    }


    private async Task<(bool Success, string QrCode, bool TransportOk, string StatusCode , string msg)> EnviarNotaParaSefaz(Invoice invoice)
    {
        try
        {
            // Carrega os itens da nota

            var itemCollection = _db.GetCollection<InvoiceItem>("invoiceitem");
            var items = itemCollection
                .Find(i => i.ClientId == invoice.ClientId && i.InvoiceId == invoice.InvoiceId)
                .ToList();

            // Carrega as taxas de cada item
            var taxCollection = _db.GetCollection<InvoiceItemTax>("invoiceitemtax");

            foreach (var item in items)
            {
                var taxes = taxCollection
                    .Find(t => t.ClientId == invoice.ClientId && t.InvoiceId == invoice.InvoiceId && t.ItemNumber == item.ItemNumber)
                    .ToList();

                item.Taxes = taxes;
            }

            invoice.Items = items
                 .OrderBy(x => x.ItemNumber)   // ordena por ItemNumber
                 .ToList();                    // cria nova lista ordenada

            var xmlBuilder = new NFeXmlBuilder(invoice, _client.EnvironmentSefaz, _db, _branche, _company, _x509Certificate2).Build();

            if (xmlBuilder.Error != null)
            {
                Console.WriteLine($"Erro ao gerar XML: {xmlBuilder.Error.Message}");
                // Opcional: detalhar exceção interna
                if (xmlBuilder.Error.InnerException != null)
                    await _toastService.ShowToastAsync(xmlBuilder.Error.InnerException.Message);
                return (false, null, true, "9999", xmlBuilder.Error.InnerException.Message);
            }

            var signedXml = xmlBuilder.Xml.InnerXml;
            var qrCode = xmlBuilder.QrCode;

            var sefazService = new SefazService(_db, _company); // instanciado com seus parâmetros

            var result = await sefazService.SendToSefazAsync(invoice.InvoiceId, signedXml, invoice, _client.EnvironmentSefaz, _x509Certificate2);

            if (result.Success)
            {
                invoice.NfeStatus = "AUTORIZADA";
                invoice.Protocol = result.ProtocolXml;

                // Desserializa o protocolo para pegar as datas e número
                var serializer = new XmlSerializer(typeof(ProtNFe));
                using var reader = new StringReader(result.ProtocolXml);
                var resultx = (ProtNFe)serializer.Deserialize(reader);

                invoice.LastUpdate = DateTime.UtcNow;
                invoice.QrCode = qrCode;
                invoice.IssueDate = DateTime.Parse(xmlBuilder.dateHoraEmissao);
                invoice.AuthorizationDate = resultx.InfProt.DhRecbto;
                invoice.Protocol = resultx.InfProt.NProt.ToString();

                // 🔹 Concatena o XML assinado + protocolo
                string xmlAutorizado =
                    $"<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                    "<nfeProc versao=\"4.00\" xmlns=\"http://www.portalfiscal.inf.br/nfe\">" +
                    signedXml + result.ProtocolXml +
                    "</nfeProc>";

                invoice.AuthorizedXml = Convert.ToBase64String(Encoding.UTF8.GetBytes(xmlAutorizado));

                // Atualiza no banco
                var invoiceCollection = _db.GetCollection<Invoice>("invoice");
                invoiceCollection.Update(invoice);

                return (true, qrCode, result.TransportOk, result.StatusCode,"Sucesso");

            }
            else
            {
                await _toastService.ShowToastAsync(result.StatusMessage);
                return (false, null, result.TransportOk, result.StatusCode, result.StatusMessage);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao carregar a nota: {ex.Message}");
            return (false, null, true, "999", ex.Message);
        }
    }

    private void UpdateTotal()
    {
        var total = _items.Sum(i => i.Price * i.Quantity);
        lblTotal.Text = $"Total: R$ {total.ToString("N2", new CultureInfo("pt-BR"))}";
        ProductSuggestions.Clear();
        entryProductCode.ItemsSource = null;

    }

    private CancellationTokenSource? _cts;
    private bool _enter = false;
    private async void OnProductCodeEntered(object sender, EventArgs e)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _enter = true;
        await Task.Run(async () =>
        {
            try
            {
                // espera 200ms sem novas teclas — típico tempo de scanner
                //ProductSuggestions.Clear();
                await Task.Delay(200, token);

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    var entry = (Entry)sender;
                    var code = entry.Text?.Trim();

                    if (string.IsNullOrWhiteSpace(code))
                        return;

                    var col = _db.GetCollection<Material>("material");
                    var product = col.FindOne(p =>
                        p.ClientId == _branche.ClientId &&
                        (p.Ean13 == code || p.Id == code));

                    if (product == null)
                    {

                        entryProductCode.Text = string.Empty;
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            entryProductCode.Focus();
                        });

                        _ = _toastService.ShowToastAsync("Produto não encontrado.");
                        return;
                    }

                    var priceCol = _db.GetCollection<MaterialPrice>("materialprice");
                    var today = DateTime.Today;

                    var price = priceCol.FindOne(p =>
                        p.ClientId == _branche.ClientId &&
                        p.MaterialId == product.Id &&
                        p.StartDate <= today &&
                        p.EndDate >= today);

                    if (price == null)
                    {
                        _ = _toastService.ShowToastAsync("Preço não encontrado.");
                        return;
                    }

                    int quantity = 1;
                    if (!int.TryParse(entryQuantity.Text?.Trim(), out quantity))
                        quantity = 1;

                    var nextItemNumber = _items.Any()
                        ? _items.Max(i => i.Id) + 1
                        : 1;

                    _items.Add(new ConsumerSaleItem
                    {
                        Id = nextItemNumber,
                        MaterialId = product.Id,
                        Description = product.Name,
                        Price = price.Price,
                        Quantity = quantity,
                        NCM = product.NcmId
                    });

                    entryProductCode.Text = string.Empty;
                    //await Task.Delay(100); // pequeno delay ajuda em dispositivos físicos
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        entryProductCode.Focus();

                    });
                    entryQuantity.Text = "1";
                    UpdateTotal();

                    entry.Text = string.Empty; // limpa após processar

                });
            }
            catch (TaskCanceledException) { }
        }, token);

        _enter = false;

    }

    private Task WaitScrollAsync(int targetIndex, int timeoutMs = 1000)
    {
        var tcs = new TaskCompletionSource();
        EventHandler<ItemsViewScrolledEventArgs> handler = null!;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        handler = (s, e) =>
        {
            if (e.LastVisibleItemIndex >= targetIndex || sw.ElapsedMilliseconds > timeoutMs)
            {
                ItemsListView.Scrolled -= handler;
                tcs.TrySetResult();
            }
        };

        ItemsListView.Scrolled += handler;
        return tcs.Task;
    }

    private async Task FocusLastItemAsync()
    {
        if (_items.Count == 0) return;
        var lastIndex = _items.Count - 1;
        var last = _items[lastIndex];

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            ItemsListView.SelectedItem = last;
            ItemsListView.ScrollTo(lastIndex, position: ScrollToPosition.End, animate: true);
            await WaitScrollAsync(lastIndex, 600);
            // reforço sem animação (Android costuma precisar)
            ItemsListView.ScrollTo(lastIndex, position: ScrollToPosition.End, animate: false);
            await WaitScrollAsync(lastIndex, 400);
        });

        await Task.Delay(120);
    }

    private void OnCancelItemClicked(object sender, EventArgs e)
    {
        if (ItemsListView.SelectedItem is ConsumerSaleItem selectedItem)
        {
            _items.Remove(selectedItem);
            UpdateTotal();
        }
    }


    private async void OnEditQuant(object sender, EventArgs e)
    {
        // 1) item alvo: selecionado ou último da fonte
        var item = ItemsListView.SelectedItem as ConsumerSaleItem;
        if (item == null && ItemsListView.ItemsSource is IEnumerable<ConsumerSaleItem> src)
            item = src.LastOrDefault();

        if (item == null) return;

        // 2) seleciona e rola para garantir materialização do template
        ItemsListView.SelectedItem = item;
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            ItemsListView.ScrollTo(item, position: ScrollToPosition.MakeVisible, animate: false);
            await Task.Delay(120); // dá tempo de materializar
        });

        // 3) tenta localizar o Entry e focar
        if (!TryFocusQtyEntry(item))
        {
            // fallback: tenta de novo um “tic” depois, se ainda não materializou
            await Task.Delay(150);
            TryFocusQtyEntry(item);
        }
    }

    private bool TryFocusQtyEntry(ConsumerSaleItem target)
    {
        foreach (var v in GetVisualDescendants(ItemsListView))
        {
            if (v is Entry entry &&
                entry.AutomationId == "QtyEntry" &&
                ReferenceEquals(entry.BindingContext, target))
            {
                FocusQuantityEntry(entry);
                return true;
            }
        }
        return false;
    }



    private void FocusQuantityEntry(Entry entry)
    {
        entry.Focus();
        var txt = entry.Text ?? string.Empty;
        entry.CursorPosition = 0;
        entry.SelectionLength = txt.Length; // seleciona tudo p/ digitar por cima
    }

    private IEnumerable<VisualElement> GetVisualDescendants(Element root)
    {
        if (root is VisualElement ve)
            yield return ve;

        if (root is IElementController ctrl)
        {
            foreach (var child in ctrl.LogicalChildren)
                foreach (var d in GetVisualDescendants(child))
                    if (d is VisualElement dve)
                        yield return dve;
        }
    }


    private void OnDiscountClicked(object sender, EventArgs e)
    {
        UpdateTotal();
    }

    private async void OnCancelSaleClicked(object sender, EventArgs e)
    {
        if (await DisplayAlert("Cancelar Venda", "Deseja cancelar a venda atual?", "Sim", "Não"))
        {
            _items.Clear();
            UpdateTotal();
        }
    }

    private bool _isFinalizingSale;

    private async void OnFinalizeSaleClicked(object sender, EventArgs e)
    {

        if (_isFinalizingSale)
            return; // já está em execução

        _isFinalizingSale = true;

        try
        {

           IsWorking = true;

            if (_items.Count == 0)
            {
                await _toastService.ShowToastAsync("Não há itens na venda para finalizar");
                return;
            }

            var body = new InvoicePostDto
            {
                CompanyId = _branche.CompanyId,
                BrancheId = _branche.Id,
                InvoiceTypeId = "01",
                Serie = userConfig.SeriePDV,
                InvoiceItems = _items.Select((item, index) => new InvoiceItemPostDto
                {
                    Id = (item.Id),
                    MaterialId = (item.MaterialId),
                    MaterialName = (item.Description),
                    Quantity = item.Quantity,
                    UnitPrice = item.Price,
                    TotalAmount = item.Total,
                    DiscountAmount = 0,
                    UnitId = "UN",
                    NCM = item.NCM,
                }).ToList()
            };

            var NCFE = new InvoiceService(_db, body, branche: _branche);
            var post = await NCFE.CreateAsync();
            if (!post.Sucess)
            {
                await _toastService.ShowToastAsync(NCFE.ErrorMessage);
                return;
            }

            // CPF no XML: se o campo estiver preenchido e válido, grava no Invoice
            var cpfDigits = OnlyDigits(entryCustomerCpf.Text?.Trim() ?? "");
            if (cpfDigits.Length == 11 && IsValidCpf(cpfDigits))
            {
                post.Invoice.Customer ??= new Customer();
                post.Invoice.Customer.Document = cpfDigits;            // usado pelo NFeXmlBuilder para <dest><CPF>
                if (string.IsNullOrWhiteSpace(post.Invoice.Customer.Name))
                    post.Invoice.Customer.Name = "CONSUMIDOR";
                // persiste no LiteDB antes da montagem/assinatura, por segurança
                var invCol = _db.GetCollection<Invoice>("invoice");
                invCol.Update(post.Invoice);
            }

            // CALCULA IBPT AQUI, ANTES DE ASSINAR/ENVIAR
            var ibpt = TaxUtils.CalculateApproximateTaxes(post.Invoice, _db, assumeImported: false);
            post.Invoice.AdditionalInfo = TaxUtils.BuildIbptObservation(ibpt);

            var send = await EnviarNotaParaSefaz(invoice: post.Invoice);
            if (!send.Success)
            {

                if (!send.TransportOk)
                {
                    await _toastService.ShowToastAsync("Erro ao enviar nota fiscal para SEFAZ. Emitindo em CONTINGÊNCIA...");
                    await EmitirEmContingenciaAsync(post.Invoice, motivoFalha: "Falha de comunicação com a SEFAZ", qrCodeUrl: send.QrCode);
                    _items.Clear();
                    entryQuantity.Text = "1";
                    entryCustomerCpf.Text = string.Empty;
                    MainThread.BeginInvokeOnMainThread(() => entryCustomerCpf.Focus());
                    UpdateTotal();

                    await _toastService.ShowToastAsync("Venda finalizada (contingência). Enviaremos à SEFAZ quando voltar.");

                }
                else
                {
                    NCFE.DeleteInvoiceCascade(post.Invoice.InvoiceId, allowAuthorizedDeletion: true);
                    await _toastService.ShowToastAsync("SEFAZ rejeitou a nota fiscal com erro :" + send.StatusCode);
                }

                return; // <<< IMPORTANTE: não envia para retaguarda

            }
            else
            {

                Task printed = ImprimirCupomViaEscPosAsync(invoice: post.Invoice, send.QrCode);
                await printed;
                if (printed.IsFaulted)
                {
                    await _toastService.ShowToastAsync("Erro ao imprimir o cupom.");
                }
                else
                {
                    var invoiceCollection = _db.GetCollection<Invoice>("invoice");
                    post.Invoice.Printed = true;
                    invoiceCollection.Update(post.Invoice);
                }

            }

            var backofficeReq = BuildBackofficeRequestFromInvoice(post.Invoice);
            var bo = await SendInvoiceToBackofficeAsync(backofficeReq);
            if (!bo)
            {
                // Mostra mensagem amigável + log opcional com bo.RawBody
                await _toastService.ShowToastAsync($"Falha ao enviar para retaguarda");
                // opcional: Console.WriteLine(bo.RawBody);
            }
            else
            {
                await _toastService.ShowToastAsync("Retaguarda sincronizada.");
                var invoiceCollection = _db.GetCollection<Invoice>("invoice");
                post.Invoice.Send = true;
                invoiceCollection.Update(post.Invoice);
            }

            _items.Clear();

            entryQuantity.Text = "1";
            entryCustomerCpf.Text = string.Empty;

            _ = Task.Delay(100); // pequeno delay ajuda em dispositivos físicos
            MainThread.BeginInvokeOnMainThread(() =>
            {
                entryCustomerCpf.Focus();

            });

            UpdateTotal();

            await _toastService.ShowToastAsync("Venda finalizada com sucesso.");


        }
        catch (Exception)
        {

            throw;
        }

        finally
        {
            // 🔓 Esconder overlay
            IsWorking = false;
            _isFinalizingSale = false;
        }



    }
    private void OnQuantityFocused(object sender, FocusEventArgs e)
    {
        if (sender is Entry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Text))
            {
                entry.Text = "1";
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                entry.CursorPosition = 0;
                entry.SelectionLength = entry.Text.Length;
            });
        }
    }


    private BackofficeInvoiceRequest BuildBackofficeRequestFromInvoice(Invoice inv)
    {
        if (inv is null) throw new ArgumentNullException(nameof(inv));

        // customerId: se não houver numérico, fica 0 (consumidor final)

        var items = (inv.Items ?? new List<InvoiceItem>()).Select(it =>
        {
            var qty = it.Quantity <= 0 ? 1 : it.Quantity;
            var unitPrice = it.UnitPrice > 0
                ? it.UnitPrice
                : (it.TotalAmount > 0 ? it.TotalAmount / qty : 0m);

            var total = it.TotalAmount > 0
                ? it.TotalAmount
                : unitPrice * qty;

            return new BackofficeInvoiceItem
            {
                ItemNumber = it.ItemNumber,
                MaterialId = it.MaterialId,
                Quantity = qty,
                UnitPrice = unitPrice,
                DiscountAmount = it.DiscountAmount is decimal d ? d : 0m,
                TotalAmount = total,
                UnitId = string.IsNullOrWhiteSpace(it.UnitId) ? "UN" : it.UnitId,
                CFOPId = it.CfopId,
            };
        }).ToList();

        // Calcula o total da nota somando os itens
        var totalAmount = items.Sum(x => x.TotalAmount);

        var req = new BackofficeInvoiceRequest
        {
            InvoiceTypeId = string.IsNullOrWhiteSpace(inv.InvoiceTypeId) ? "01" : inv.InvoiceTypeId,
            CompanyId = string.IsNullOrWhiteSpace(inv.CompanyId) ? _branche.CompanyId : inv.CompanyId,
            BrancheId = string.IsNullOrWhiteSpace(inv.BrancheId) ? _branche.Id : inv.BrancheId,
            CustomerId = inv.Customer.Id,
            NFKey = inv.NfKey,
            NFe = inv.Nfe,
            IssueDate = inv.IssueDate,
            AuthorizedXml = string.IsNullOrWhiteSpace(inv.AuthorizedXml) ? null : Convert.FromBase64String(inv.AuthorizedXml),
            // usa a série da própria invoice; se vier vazia, cai para a config do PDV
            Serie = inv.Serie,
            // total calculado no cabeçalho
            TotalAmount = totalAmount,

            // itens
            InvoiceItems = items
        };

        return req;
    }


    private async Task<bool> SendInvoiceToBackofficeAsync(BackofficeInvoiceRequest req, CancellationToken ct = default)
    {
        try
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(req);

            using var httpReq = new HttpRequestMessage(HttpMethod.Post, "invoices")
            {
                Content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json")
            };
            httpReq.Headers.Accept.Clear();
            httpReq.Headers.Accept.ParseAdd("*/*");

            using var resp = await _http.SendAsync(httpReq, ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                Console.WriteLine($"[Backoffice] FAIL -> Status: {(int)resp.StatusCode} {resp.ReasonPhrase}");
                Console.WriteLine($"[Backoffice] Body: {body}");
                return false;
            }

            return true;
        }
        catch (TaskCanceledException tex) when (!ct.IsCancellationRequested)
        {
            Console.WriteLine($"[Backoffice] TIMEOUT: {tex.Message}");
            return false;
        }
        catch (HttpRequestException hex)
        {
            Console.WriteLine($"[Backoffice] NETWORK ERROR: {hex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Backoffice] UNEXPECTED ERROR: {ex}");
            return false;
        }
    }


    private async Task CheckAndSendAuthorizedInvoicesAsync()
    {
        try
        {
            var invoiceCol = _db.GetCollection<Invoice>("invoice");

            // busca notas AUTORIZADA que não foram enviadas (Send == false)
            var pendentes = invoiceCol.Find(i =>
                i.ClientId == _branche.ClientId &&
                i.CompanyId == _branche.CompanyId &&
                i.BrancheId == _branche.Id &&
                i.NfeStatus == "AUTORIZADA" &&
                (i.Send == false)
            ).ToList();

            if (pendentes == null || pendentes.Count == 0) return;

            bool enviarAgora = await DisplayAlert(
                "Notas autorizadas pendentes",
                $"Existem {pendentes.Count} nota(s) autorizada(s) e não enviadas para a retaguarda.\nDeseja enviar agora?",
                "Sim", "Não");

            if (!enviarAgora) return;

            int successCount = 0;
            int failCount = 0;

            foreach (var inv in pendentes)
            {
                try
                {

                    inv.Items = _db.GetCollection<InvoiceItem>("invoiceitem")
                        .Find(ii => ii.ClientId == inv.ClientId && ii.InvoiceId == inv.InvoiceId)
                        .ToList();
                    var backReq = BuildBackofficeRequestFromInvoice(inv);

                    var ok = await SendInvoiceToBackofficeAsync(backReq);

                    if (ok)
                    {
                        // só marca como enviado se o backoffice confirmou (ou se a chamada HTTP foi 2xx)
                        inv.Send = true;

                        invoiceCol.Update(inv);
                        successCount++;
                    }

                }
                catch (Exception exItem)
                {
                    Console.WriteLine($"[Backoffice] Erro ao enviar invoice {inv.InvoiceId}: {exItem}");

                    failCount++;
                }
            }

            await _toastService.ShowToastAsync($"Envio concluído. Sucesso: {successCount}, Falhas: {failCount}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao checar/enviar notas pendentes: {ex}");
            await _toastService.ShowToastAsync("Erro ao verificar notas pendentes.");
        }
    }

    #region Impressão NFC-e via ESC/POS

    const int DIREITA = 2;
    const int CENTRO = 1;
    const int ESQUERDA = 0;
    // Helper: escrever linha ESC/POS
    static void EscPos_SetLineSpacing(Stream s, byte dots) => s.Write(new byte[] { 0x1B, 0x33, dots }, 0, 3);  // ESC 3 n
    static void EscPos_Reset(Stream s) => s.Write(new byte[] { 0x1B, 0x40 });                         // ESC @
    static void EscPos_Align(Stream s, byte n) => s.Write(new byte[] { 0x1B, 0x61, n });                      // 0=left 1=center 2=right
    static void EscPos_Bold(Stream s, bool on) => s.Write(new byte[] { 0x1B, 0x45, (byte)(on ? 1 : 0) });     // ESC E n
    static void EscPos_Feed(Stream s, byte n) => s.Write(new byte[] { 0x1B, 0x64, n });                       // ESC d n
    static void EscPos_Cut(Stream s) => s.Write(new byte[] { 0x1D, 0x56, 0x42, 0x00 });             // GS V B 0

    // Encoding atual usada para converter string -> bytes
    static Encoding CurrentEnc = CodePagesEncodingProvider.Instance.GetEncoding(1252); // default

    static void SetEncoding(Encoding enc) => CurrentEnc = enc ?? Encoding.UTF8;

    static void EscPos_FontB(Stream s) => s.Write(new byte[] { 0x1B, 0x4D, 0x01 }, 0, 3);
    static void EscPos_FontA(Stream s) => s.Write(new byte[] { 0x1B, 0x4D, 0x00 }, 0, 3);
    static void EscPos_SetCodePage(Stream s, byte escT, int dotnetCodePage)
    {
        // ESC t n  → seleciona “character code table” na impressora
        s.Write(new byte[] { 0x1B, 0x74, escT }, 0, 3);
        SetEncoding(CodePagesEncodingProvider.Instance.GetEncoding(dotnetCodePage));
    }

    enum Align { Left, Right, Center }
       

    static string FitCell(string text, int width, Align align)
    {
        text ??= "";
        text = ToAsciiEscPos(text);
        if (text.Length > width) return text.Substring(0, width);

        return align switch
        {
            Align.Right => text.PadLeft(width),
            Align.Center => (text.Length >= width) ? text
                            : new string(' ', (width - text.Length) / 2)
                              + text
                              + new string(' ', width - text.Length - (width - text.Length) / 2),
            _ => text.PadRight(width),
        };
    }

    public async Task ImprimirCupomViaEscPosAsync(Invoice invoice, string qrCodeUrl)
    {
        try
        {
            const int COLS = 48;
            using var ms = new MemoryStream();
            EscPos_Reset(ms);
            EscPos_SetCodePage(ms, 0x00, 437);

            // ----- Cabeçalho -----
            EscPos_Bold(ms, true);
            EscPos_Align(ms, CENTRO);
            Writeln(ms, "Documento Auxiliar da NFC-e", COLS);
            EscPos_Bold(ms, false);
            Writeln(ms, $"{_company?.Description}", COLS);
            Writeln(ms, $"{_branche?.Street}, {_branche?.HouseNumber} - {_branche?.Neighborhood} - {_branche?.CityId} - {_branche?.StateId} - CEP: {_branche?.PostalCode}", 50);
            EscPos_Align(ms, 1);
            Writeln(ms, $"CNPJ: {_branche?.Cnpj} " + $"IE: {_branche?.StateRegistration}", COLS);
            EscPos_Align(ms, 0);

            EscPos_FontB(ms);

            EscPos_Align(ms, ESQUERDA);

            WriteLineColumns(ms, new[] { "Item", "Codigo", "Descricao", "Qtde", "Un", "Valor", "Total" }, new[] { 5, 8, 20, 6, 3, 8, 8 });

            int seq = 1;
            foreach (var it in invoice.Items)
            {
                var code = !string.IsNullOrWhiteSpace(it.MaterialId) ? it.MaterialId : (it.MaterialId ?? "");
                var desc = string.IsNullOrWhiteSpace(it.MaterialName) ? code : $"{it.MaterialName}";
                EscPos_Align(ms, ESQUERDA);
                Writeln(ms, $"{seq:000} {code} {desc}");

                var un = string.IsNullOrWhiteSpace(it.UnitId) ? "UN" : it.UnitId.ToUpperInvariant();
                var q = it.Quantity;
                var unit = it.UnitPrice;
                var tot = unit * q;
                EscPos_Align(ms, 2);
                Writeln(ms, $"{q:0.##} {un} x R$ {unit:N2} = R$ {tot:N2}", COLS);
                seq++;
            }

            EscPos_Reset(ms);
            Writeln(ms, "");
            EscPos_Align(ms, DIREITA);
            //Line(ms, COLS);
            Write(ms, "Qtde. total de itens " + $"{invoice.Items.Sum(i => i.Quantity):0.##}");
            EscPos_Align(ms, ESQUERDA);
            Writeln(ms, "");

            // ----- Totais -----
            var vSub = invoice.Items.Sum(i => i.UnitPrice * i.Quantity);
            var vDesc = 0m; // ajuste se houver
            var vAcres = 0m; // ajuste se houver
            var vTotal = vSub - vDesc + vAcres;

            if (vDesc > 0) LeftRight(ms, "Descontos", $"R$ {vDesc:N2}", COLS);
            if (vAcres > 0) LeftRight(ms, "Acréscimos", $"R$ {vAcres:N2}", COLS);

            EscPos_Bold(ms, true);
            EscPos_Align(ms, DIREITA);
            Write(ms, "VALOR TOTAL R$" + $"{vTotal:N2}", COLS);
            Writeln(ms, "");
            EscPos_Bold(ms, false);

            EscPos_Align(ms, ESQUERDA);

            // ----- Info Complementares / IBPT -----
            if (!string.IsNullOrWhiteSpace(invoice.AdditionalInfo))
            {
                const int INFO_COLS = 42;
                //Line(ms, COLS);
                EscPos_Bold(ms, true);
                Writeln(ms, "INFORMACOES COMPLEMENTARES", COLS);
                EscPos_Bold(ms, false);
                foreach (var line in Wrap(invoice.AdditionalInfo, INFO_COLS))
                    Writeln(ms, line, INFO_COLS);
            }

            // ----- Pagamentos -----
            if (invoice.InvoicePayments?.Any() == true)
            {
                Writeln(ms, "FORMA DE PAGAMENTO", COLS);
                foreach (var p in invoice.InvoicePayments)
                    LeftRight(ms, p.PaymentId.ToString(), $"{p.Amount:N2}", COLS);
            }

            //Line(ms, COLS);

            EscPos_FontB(ms);
            EscPos_SetLineSpacing(ms, 6);

            // ----- Consulta -----
            Writeln(ms, "Consulte pela Chave de Acesso em", COLS);
            Writeln(ms, "https://www.nfce.fazenda.sp.gov.br/consulta", COLS);
            if (!string.IsNullOrWhiteSpace(invoice.NfKey))
                Writeln(ms, Regex.Replace(invoice.NfKey, ".{4}", "$0 ").Trim(), COLS);

            // ----- Consumidor -----
            if (!string.IsNullOrWhiteSpace(invoice.Customer?.Document))
                Writeln(ms, $"CONSUMIDOR - CPF {CpfMask((invoice.Customer.Document))}", COLS);

            var dataAut = invoice.AuthorizationDate?.ToString("dd/MM/yyyy HH:mm:ss");
            var dataEmi = invoice.IssueDate.ToString("dd/MM/yyyy HH:mm:ss");
            Writeln(ms, $"NFC-e  {invoice.Nfe}  Série {invoice.Serie}");
            Writeln(ms, $"Data de Emissão {dataEmi}");
            if (!string.IsNullOrWhiteSpace(invoice.AuthorizationProtocol))
                Writeln(ms, $"Protocolo de Autorização: {invoice.AuthorizationProtocol}", COLS);
            if (!string.IsNullOrWhiteSpace(dataAut))
                Writeln(ms, $"Data de Autorização: {dataAut}", COLS);
              
            // ----- QR como RASTER (compatível com seu emulador e com impressoras reais) -----
            EscPos_Align(ms, 1);
            var qrRaster = BuildQrRasterEscPosMaui(qrCodeUrl, sizePx: 256); // 256 ~ bom para 58/80mm
            ms.Write(qrRaster, 0, qrRaster.Length);
            EscPos_Align(ms, 0);


            EscPos_Feed(ms, 3);
            EscPos_Cut(ms);

            // ===== payload final =====
            var payload = ms.ToArray();

            // ===== prefixo/header com nome da impressora (se seu emulador espera isso) =====
            var header = Encoding.UTF8.GetBytes(((userConfig.PrinterName ?? DefaultPrinterName) + "\n"));
            var fullPayload = Combine(header, payload);

            // ===== (B) ENVIO TCP (como já fazia) =====
            using var client = new TcpClient();
            var connectTask = userConfig.PrinterPort;
            await client.ConnectAsync(userConfig.PrinterIp, connectTask).ConfigureAwait(false);
            using var stream = client.GetStream();

            await stream.WriteAsync(fullPayload, 0, fullPayload.Length).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _toastService.ShowToastAsync($"Falha ao gerar/enviar impressão: {ex.Message}");
        }
    }

    static void WriteLineColumns(Stream s, string[] texts, int[] widths)
    {
        if (texts.Length != widths.Length)
            throw new ArgumentException("Texts e widths devem ter o mesmo comprimento.");

        var sb = new StringBuilder();
        for (int i = 0; i < texts.Length; i++)
        {
            var txt = ToAsciiEscPos(texts[i] ?? "");
            // Garante que o texto cabe na largura da coluna
            if (txt.Length > widths[i])
                txt = txt.Substring(0, widths[i]);
            sb.Append(txt.PadRight(widths[i]));
        }

        var bytes = Enc1252.GetBytes(sb.ToString());
        s.Write(bytes, 0, bytes.Length);
        //s.WriteByte(0x0A); // Apenas uma quebra de linha ao final
    }



    // ===== Helpers =====
    private static byte[] Combine(byte[] a, byte[] b)
    {
        var r = new byte[a.Length + b.Length];
        Buffer.BlockCopy(a, 0, r, 0, a.Length);
        Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
        return r;
    }



    private static readonly Encoding Enc1252 =
     CodePagesEncodingProvider.Instance.GetEncoding(1252); // ou Encoding.GetEncoding(1252)

    static void Writeln(Stream s, string text, int width = 48)
    {
        // Sanitiza para ASCII antes de imprimir
        text = ToAsciiEscPos(text);

        var bytes = Enc1252.GetBytes(text ?? "");
        s.Write(bytes, 0, bytes.Length);
        s.Write(new byte[] { 0x1B, 0x33, 6, 0x0A }, 0, 4); // bem compacto

    }

    static void Write(Stream s, string text, int width = 48)
    {
        text = ToAsciiEscPos(text);

        var bytes = Enc1252.GetBytes(text ?? "");
        s.Write(bytes, 0, bytes.Length);

        // apenas define espaçamento compacto, sem enviar LF
        s.Write(new byte[] { 0x1B, 0x33, 6 }, 0, 3);
    }


    static string ToAsciiEscPos(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // 1) Normaliza e remove diacríticos (acentos)
        string norm = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(norm.Length);

        foreach (var ch in norm)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark)
                continue; // descarta acento
            sb.Append(ch);
        }

        // 2) Recompõe e aplica mapeamentos comuns para ESC/POS
        string s = sb.ToString().Normalize(NormalizationForm.FormC)
            .Replace('’', '\'')
            .Replace('‘', '\'')
            .Replace('“', '"')
            .Replace('”', '"')
            .Replace('–', '-')  // en dash
            .Replace('—', '-')  // em dash
            .Replace('•', '*')
            .Replace('º', 'o')
            .Replace('ª', 'a')
            // Alguns modelos “comem” o símbolo de moeda; se preferir, troque por “R$”
            .Replace('€', 'E')
            .Replace('£', 'L')
            .Replace('¥', 'Y')
            .Replace('₩', 'W');

        // 3) Filtra para ASCII imprimível (32..126). Troca fora disso por espaço
        var outSb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c >= 32 && c <= 126) outSb.Append(c);
            else outSb.Append(' ');
        }
        return outSb.ToString();
    }



    static IEnumerable<string> Wrap(string text, int width)
    {
        if (string.IsNullOrEmpty(text)) { yield return ""; yield break; }

        var words = Regex.Split(text, @"\s+");
        var line = new StringBuilder();
        foreach (var w in words)
        {
            if (line.Length == 0) { line.Append(w); continue; }
            if (line.Length + 1 + w.Length <= width) line.Append(' ').Append(w);
            else { yield return line.ToString(); line.Clear(); line.Append(w); }
        }
        if (line.Length > 0) yield return line.ToString();
    }

    static void LeftRight(Stream s, string left, string right, int width = 48)
    {
        left ??= "";
        right ??= "";
        if (left.Length + right.Length > width)
        {
            // quebra o lado esquerdo e imprime o lado direito na última linha
            var leftLines = Wrap(left, width - (right.Length + 1)).ToList();
            for (int i = 0; i < leftLines.Count; i++)
            {
                if (i == leftLines.Count - 1)
                {
                    var pad = width - (leftLines[i].Length + right.Length);
                    Writeln(s, leftLines[i] + new string(' ', Math.Max(1, pad)) + right, width);
                }
                else Writeln(s, leftLines[i], width);
            }
        }
        else
        {
            var pad = width - (left.Length + right.Length);
            Writeln(s, left + new string(' ', pad) + right, width);
        }
    }

    static void Line(Stream s, int width = 48) => Writeln(s, new string('-', width), width);

    static string CpfMask(string? cpf)
    {
        cpf = cpf?.Trim().Replace(".", "").Replace("-", "");
        if (string.IsNullOrEmpty(cpf) || cpf.Length != 11) return cpf ?? "";
        return Convert.ToUInt64(cpf).ToString(@"000\.000\.000\-00");
    }

    static byte[] BuildQrCodeEscPos(string data)
    {
        // QR Model 2, ECC M, tamanho ~ 6
        var enc = Encoding.UTF8;
        byte[] model = { 0x1D, 0x28, 0x6B, 0x04, 0x00, 0x31, 0x41, 0x32, 0x00 };               // model 2
        byte[] ecc = { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x45, 0x32 };                      // ECC M
        byte[] size = { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x43, 0x06 };                      // module size 6

        var payload = enc.GetBytes(data ?? "");
        int pL = (payload.Length + 3) & 0xFF;
        int pH = (payload.Length + 3) >> 8;
        byte[] store = { 0x1D, 0x28, 0x6B, (byte)pL, (byte)pH, 0x31, 0x50, 0x30 };
        byte[] print = { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x51, 0x30 };

        using var ms = new MemoryStream();
        ms.Write(model); ms.Write(ecc); ms.Write(size); ms.Write(store); ms.Write(payload); ms.Write(print);
        return ms.ToArray();
    }

    static byte[] BuildQrRasterEscPosMaui(string data, int sizePx = 256)
    {
        if (string.IsNullOrWhiteSpace(data)) data = " ";

        // 1) Gera a imagem do QR como RGBA (sem System.Drawing)
        var writer = new ZXing.BarcodeWriterPixelData
        {
            Format = ZXing.BarcodeFormat.QR_CODE,
            Options = new EncodingOptions
            {
                Width = sizePx,
                Height = sizePx,
                Margin = 0,        // sem quiet zones extras (ajuste se quiser)
                PureBarcode = true
            }
        };

        var pixelData = writer.Write(data); // pixelData.Pixels = RGBA (4 bytes por pixel)

        int srcWidth = pixelData.Width;
        int srcHeight = pixelData.Height;

        // 2) Ajusta largura para múltiplo de 8 (requisito ESC/POS raster)
        int width = ((srcWidth + 7) / 8) * 8;
        int height = srcHeight;

        // Se precisar padding horizontal, cria um buffer RGBA mais largo (fundo branco)
        var rgba = pixelData.Pixels;
        if (width != srcWidth)
        {
            var padded = new byte[width * height * 4];
            // preenche branco
            for (int i = 0; i < padded.Length; i += 4)
            {
                padded[i + 0] = 0xFF; // R
                padded[i + 1] = 0xFF; // G
                padded[i + 2] = 0xFF; // B
                padded[i + 3] = 0xFF; // A
            }
            // copia cada linha no canto esquerdo
            for (int y = 0; y < height; y++)
            {
                Buffer.BlockCopy(rgba, y * srcWidth * 4, padded, y * width * 4, srcWidth * 4);
            }
            rgba = padded;
        }

        // 3) Converte RGBA para 1bpp (preto=1, branco=0), MSB->LSB
        int bytesPerRow = width / 8;
        var mono = new byte[bytesPerRow * height];

        for (int y = 0; y < height; y++)
        {
            int rowOfs = y * bytesPerRow;
            int rgbaOfs = y * width * 4;

            for (int x = 0; x < width; x++)
            {
                int p = rgbaOfs + (x * 4);
                byte r = rgba[p + 0];
                byte g = rgba[p + 1];
                byte b = rgba[p + 2];

                // limiar: abaixo de 128 é preto
                int lum = (r * 299 + g * 587 + b * 114) / 1000;
                if (lum < 128)
                {
                    mono[rowOfs + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
        }

        // 4) Monta comando ESC/POS: GS v 0 m xL xH yL yH [data]
        byte m = 0x00;                                        // modo normal
        byte xL = (byte)(bytesPerRow & 0xFF);
        byte xH = (byte)((bytesPerRow >> 8) & 0xFF);
        byte yL = (byte)(height & 0xFF);
        byte yH = (byte)((height >> 8) & 0xFF);

        using var ms = new MemoryStream();
        ms.WriteByte(0x1D); ms.WriteByte(0x76); ms.WriteByte(0x30); ms.WriteByte(m);
        ms.WriteByte(xL); ms.WriteByte(xH); ms.WriteByte(yL); ms.WriteByte(yH);
        ms.Write(mono, 0, mono.Length);

        // LF após a imagem (ajuda alguns parsers/emuladores)
        ms.WriteByte(0x0A);

        return ms.ToArray();
    }


    #endregion  Impressão NFC-e via ESC/POS

    private async void OnCadastrarClienteClicked(object sender, EventArgs e)
    {
        _fromButton = true; // marca que o foco saiu por causa do clique

        string cpf = entryCustomerCpf.Text?.Trim();

        if (string.IsNullOrWhiteSpace(cpf))
        {
            await _toastService.ShowToastAsync("Informe um CPF para cadastrar.");
            return;
        }


        if (!IsValidCpf(cpf))
        {
            await DisplayAlert("CPF inválido", "Por favor, informe um CPF válido.", "OK");
            return;
        }

        // aqui você chama sua lógica de cadastro
        // por exemplo: abre uma nova página ou envia para API
        await Navigation.PushAsync(new CustomerRegistrationPage(cpf, _db));
    }

    private void OnCpfCompleted(object sender, EventArgs e)
    {
        validarCPF();
    }
    private bool _fromButton = false;
    private void OnCpfUnfocused(object sender, FocusEventArgs e)
    {
        validarCPF();
    }

    private async void validarCPF()
    {
        var cpf = entryCustomerCpf.Text?.Trim();
        if (string.IsNullOrEmpty(cpf))
        {
            entryProductCode.Focus(); // não valida se estiver vazio
            return;
        }

        if (!IsValidCpf(cpf))
        {
            // limpa o campo para não ficar preso em loop
            entryCustomerCpf.Text = string.Empty;

            await DisplayAlert("CPF inválido", "Por favor, informe um CPF válido.", "OK");
            entryCustomerCpf.Focus();
        }
        else
        {
            // se válido, formata
            entryCustomerCpf.Text = MaskCpf(cpf);


            // joga o foco no código do produto
            MainThread.BeginInvokeOnMainThread(() =>
            {
                entryProductCode.Focus();
            });
        }
    }


    private static string OnlyDigits(string input) =>
        new string(input.Where(char.IsDigit).ToArray());

    private static bool IsValidCpf(string cpf)
    {
        cpf = OnlyDigits(cpf);
        if (cpf.Length != 11) return false;
        if (cpf.Distinct().Count() == 1) return false; // todos iguais

        int[] mult1 = { 10, 9, 8, 7, 6, 5, 4, 3, 2 };
        int[] mult2 = { 11, 10, 9, 8, 7, 6, 5, 4, 3, 2 };

        string temp = cpf.Substring(0, 9);
        int sum = 0;
        for (int i = 0; i < 9; i++) sum += int.Parse(temp[i].ToString()) * mult1[i];
        int resto = sum % 11;
        int dig1 = resto < 2 ? 0 : 11 - resto;

        temp += dig1;
        sum = 0;
        for (int i = 0; i < 10; i++) sum += int.Parse(temp[i].ToString()) * mult2[i];
        resto = sum % 11;
        int dig2 = resto < 2 ? 0 : 11 - resto;

        return cpf.EndsWith(dig1.ToString() + dig2.ToString());
    }

    private static string MaskCpf(string cpf)
    {
        cpf = OnlyDigits(cpf);
        return Convert.ToUInt64(cpf).ToString(@"000\.000\.000\-00");
    }


    private static readonly TimeZoneInfo TzSp = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private static DateTime NowSp() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TzSp);

    /// Força DateTime “parede de SP” (sem Kind para não haver conversão automática)
    private static DateTime SpWall(DateTime dt) =>
        DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);


    private async Task EmitirEmContingenciaAsync(Invoice invoice, string motivoFalha, string? qrCodeUrl = null)
    {
        // (re)carrega itens + tributos (igual você já faz antes do builder)
        var itemCollection = _db.GetCollection<InvoiceItem>("invoiceitem");
        var items = itemCollection.Find(i => i.ClientId == invoice.ClientId && i.InvoiceId == invoice.InvoiceId).ToList();

        var taxCollection = _db.GetCollection<InvoiceItemTax>("invoiceitemtax");
        foreach (var it in items)
            it.Taxes = taxCollection.Find(t => t.ClientId == invoice.ClientId && t.InvoiceId == invoice.InvoiceId && t.ItemNumber == it.ItemNumber).ToList();

        invoice.Items = items;

        

        // Monta XML "normal" e converte para contingência (tpEmis=9)
        var builder = new NFeXmlBuilder(invoice, _client.EnvironmentSefaz, _db, _branche, _company, _x509Certificate2).Build();
        if (builder.Error != null) throw builder.Error;

        var xmlDoc = builder.Xml; // XmlDocument assinado do seu builder
        var agoraSp = NowSp();

        // Ajusta campos de contingência no XML
        ForcarContingenciaNoXml(xmlDoc, agoraSp, motivoFalha);

        // Atualiza dados da invoice (status e datas em hora de SP “plana”)
        invoice.NfeStatus = "NO_SEND";               // pendente de envio
        invoice.Contingency = true;
        invoice.IssueDate = SpWall(agoraSp);
        invoice.AuthorizationDate = null;
        invoice.Protocol = null;

        // guarda XML assinado pós-ajuste
        var xmlAssinado = xmlDoc.InnerXml;
        invoice.AuthorizedXml = Convert.ToBase64String(Encoding.UTF8.GetBytes(xmlAssinado));

        // QR Code: use do builder se já veio, senão gere/retente
        var qr = qrCodeUrl ?? builder.QrCode ?? "";

        // Persiste
        _db.GetCollection<Invoice>("invoice").Update(invoice);

        // Imprime DANFE contingência
        await ImprimirCupomContingenciaAsync(invoice, qr);

        await _toastService.ShowToastAsync("NFC-e emitida em CONTINGÊNCIA. Enviaremos à SEFAZ quando a conexão voltar.");
    }


    /// Ajusta o XML para contingência: tpEmis=9 + dhCont + xJust
    private static void ForcarContingenciaNoXml(System.Xml.XmlDocument xml, DateTime dhContSp, string xJust)
    {
        var ns = new XmlNamespaceManager(xml.NameTable);
        ns.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");

        var ide = xml.SelectSingleNode("//nfe:infNFe/nfe:ide", ns) as System.Xml.XmlElement;
        if (ide == null) throw new InvalidOperationException("Nós <ide> não encontrado no XML.");

        void Upsert(string name, string value)
        {
            var n = ide[name];
            if (n == null) { n = xml.CreateElement(name, ide.NamespaceURI); ide.AppendChild(n); }
            n.InnerText = value;
        }

        Upsert("tpEmis", "9");
        Upsert("dhCont", dhContSp.ToString("yyyy-MM-dd'T'HH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture)); // exige offset
        Upsert("xJust", string.IsNullOrWhiteSpace(xJust) ? "Falha de comunicação com a SEFAZ" : xJust);
    }

    private async Task ImprimirCupomContingenciaAsync(Invoice invoice, string qrCodeUrl)
    {
        try
        {
            const int COLS = 42;
            using var ms = new MemoryStream();
            EscPos_Reset(ms);
            EscPos_SetCodePage(ms, 0x00, 437);

            // Faixa obrigatória
            EscPos_Align(ms, 1);
            EscPos_Bold(ms, true);
            Writeln(ms, "EMITIDA EM CONTINGENCIA", COLS);
            Writeln(ms, "Aguardando autorizacao da SEFAZ", COLS);
            EscPos_Bold(ms, false);
            EscPos_Align(ms, 0);

            // Cabeçalho resumido (reaproveita o mesmo padrão do seu ImprimirCupomViaEscPosAsync)
            Writeln(ms, $"{_company?.Description}", COLS);
            Writeln(ms, $"CNPJ: {_branche?.Cnpj}", COLS);

            // Itens, totais, etc. (pode reaproveitar as mesmas funções que você já usa)
            Line(ms, COLS);
            LeftRight(ms, "Qtde. total de itens", $"{invoice.Items.Sum(i => i.Quantity):0.##}", COLS);

            var vTotal = invoice.Items.Sum(i => i.UnitPrice * i.Quantity);
            EscPos_Bold(ms, true);
            LeftRight(ms, "VALOR TOTAL R$", $"{vTotal:N2}", COLS);
            EscPos_Bold(ms, false);

            Line(ms, COLS);
            var dataEmi = invoice.IssueDate.ToString("dd/MM/yyyy HH:mm:ss");
            Writeln(ms, $"Data de Emissao {dataEmi}", COLS);
            if (!string.IsNullOrWhiteSpace(invoice.NfKey))
                Writeln(ms, $"Chave: {Regex.Replace(invoice.NfKey, ".{4}", "$0 ").Trim()}", COLS);

            // QR Code
            EscPos_Align(ms, 1);
            var qr = BuildQrCodeEscPos(qrCodeUrl ?? "");
            ms.Write(qr, 0, qr.Length);
            EscPos_Align(ms, 0);

            EscPos_Feed(ms, 3);
            EscPos_Cut(ms);

            var payload = ms.ToArray();

            using var client = new TcpClient();
            await client.ConnectAsync(userConfig.PrinterIp, userConfig.PrinterPort).ConfigureAwait(false);
            using var stream = client.GetStream();

            var header = Encoding.UTF8.GetBytes((userConfig.PrinterName ?? DefaultPrinterName) + "\n");
            await stream.WriteAsync(header, 0, header.Length).ConfigureAwait(false);
            await stream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _toastService.ShowToastAsync($"Falha ao imprimir contingencia: {ex.Message}");
        }
    }

    private async Task CheckAndSendContingencyAsync()
    {
        var col = _db.GetCollection<Invoice>("invoice");
        var pendentes = col.Find(i =>
            i.ClientId == _branche.ClientId &&
            i.CompanyId == _branche.CompanyId &&
            i.BrancheId == _branche.Id &&
            i.NfeStatus == "NO_SEND" &&
            i.Contingency == true
        ).ToList();

        if (pendentes.Count == 0) return;

        bool enviarAgora = await DisplayAlert(
            "Notas em contingencia",
            $"Existem {pendentes.Count} NFC-e(s) emitidas em contingencia. Deseja enviar agora?",
            "Sim", "Não");

        if (!enviarAgora) return;

        int ok = 0, fail = 0;
        foreach (var inv in pendentes)
        {
            try
            {
                // Usa o AuthorizedXml como fonte (já é o XML assinado com tpEmis=9)
                var xmlAssinado = Encoding.UTF8.GetString(Convert.FromBase64String(inv.AuthorizedXml));
                var sefazService = new SefazService(_db, _company);

                

                var result = await sefazService.SendToSefazAsync(inv.InvoiceId, xmlAssinado, inv, _client.EnvironmentSefaz, _x509Certificate2);
                if (result.Success)
                {
                    // Atualiza dados de autorização
                    var serializer = new XmlSerializer(typeof(ProtNFe));
                    using var reader = new StringReader(result.ProtocolXml);
                    var p = (ProtNFe)serializer.Deserialize(reader);

                    inv.NfeStatus = "AUTORIZADA";
                    inv.AuthorizationDate = p.InfProt.DhRecbto;
                    inv.Protocol = p.InfProt.NProt;
                    inv.Contingency = false;
                    inv.LastUpdate = DateTime.UtcNow;

                    col.Update(inv);
                    ok++;
                }
                else { fail++; }
            }
            catch { fail++; }
        }

        await _toastService.ShowToastAsync($"Contingencia -> SEFAZ concluido. Sucesso: {ok}, Falhas: {fail}.");
    }

    private const int SuggestLimit = 7;
    private int _querySeq = 0; // evita race conditions entre buscas

    private async Task SearchProductsAsync(string term)
    {
        if (_suppressTextChanged) return;

        // cancela a busca anterior E dispose
        _typeDebounceCts?.Cancel();
        _typeDebounceCts?.Dispose();
        _typeDebounceCts = new CancellationTokenSource();
        var token = _typeDebounceCts.Token;

        term = (term ?? "").Trim();
        if (string.IsNullOrWhiteSpace(term))
        {
            ProductSuggestions.Clear();
            entryProductCode.ItemsSource = null;
            return;
        }

        var mySeq = ++_querySeq;

        try
        {
            await Task.Delay(DebounceMs, token);

            var hasDigit = term.Any(char.IsDigit);
            var hasLetter = term.Any(char.IsLetter);
            bool preferCode = (hasDigit && !hasLetter && term.Length >= 3) || (hasDigit && hasLetter);

            // opcional: também limitar por tempo total
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

            var results = await _products.FindAsync(
                term: term,
                limit: SuggestLimit,
                preferCode: preferCode,
                clientId: userConfig.ClientId,
                ct: linked.Token);

            if (mySeq != _querySeq) return; // resposta antiga → ignora

            ProductSuggestions.Clear();
            foreach (var p in results)
                ProductSuggestions.Add(new ProductSuggestion { Id = p.Id, Name = p.Name, Price = p.Price , PriceUnit = p.PriceUnit });
            entryProductCode.ItemsSource = null;
            entryProductCode.ItemsSource =  ProductSuggestions;
        }
        catch (TaskCanceledException) { /* digitação contínua → esperado */ }
        catch (OperationCanceledException) { /* idem */ }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Busca falhou: {ex}");
            ProductSuggestions.Clear();
        }
    }

    bool _navigating = false;
    DateTime _lastNavigate = DateTime.MinValue;

    private void OnProductCodeUnfocused(object sender, FocusEventArgs e)
    {
        //if (_lastChosen != null)
        //{
        //    // limpa sugestão destacada

        //    var z = sender as zoft.MauiExtensions.Controls.AutoCompleteEntry;
        //    if (z.SelectedSuggestion == null) return;

        //    if (z.SelectedSuggestion == _lastChosen)
        //    {
        //        //entryProductCode.Text = _lastChosen.Id;
        //        OnProductCodeEntered(sender, e);
        //    }
        //    _lastChosen = null;
        //}
    }
    private void OnItemTapped(object sender, TappedEventArgs e)
    {
        if (sender is Element el && el.BindingContext is ConsumerSaleItem item)
        {
            // opcional: marcar seleção visual
            ItemsListView.SelectedItem = item;


            // faça sua ação de “clique” aqui:
            // ex: abrir edição de quantidade, remover, etc.
            // await EditarItemAsync(item);
        }
    }

    private void OnItemSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var item = e.CurrentSelection?.FirstOrDefault() as ConsumerSaleItem;
        // habilite/desabilite botões, etc.
    }

    private ProductSuggestion? _lastChosen;
   


    [RelayCommand] // ou manualmente se não usar CommunityToolkit
    public async Task OnEditQuantityCommand(ConsumerSaleItem item)
    {
        if (item == null)
            return;

        string result = await DisplayPromptAsync(
            "Alterar quantidade",
            $"Produto: {item.Description}\nQuantidade atual: {item.Quantity}",
            "OK", "Cancelar",
            "Nova quantidade",
            maxLength: 6,
            keyboard: Keyboard.Numeric);

        if (int.TryParse(result, out int novaQtd) && novaQtd > 0)
        {
            item.Quantity = novaQtd;
            // Atualiza a CollectionView (se estiver usando ObservableCollection, ela se atualiza sozinha)
        }
    }

    private void Entry_Completed(object sender, EventArgs e)
    {
        entryProductCode.Focus();
    }
}


