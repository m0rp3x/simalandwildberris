using MudBlazor;

namespace WBSL.Client.Data.Handlers;

public class SnackbarHttpHandler : DelegatingHandler
{
    private readonly ISnackbar _snackbar;
    private readonly string _loadingMessage;

    public SnackbarHttpHandler(ISnackbar snackbar, string loadingMessage = "Загрузка...")
    {
        _snackbar = snackbar;
        _loadingMessage = loadingMessage;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Создаем Snackbar
        var snackbarRef = _snackbar.Add(
            _loadingMessage,
            Severity.Info,
            config => { config.VisibleStateDuration = int.MaxValue;
                config.CloseAfterNavigation = true;
            });
        
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            
            _snackbar.Remove(snackbarRef);
            
            return response;
        }
        catch (Exception ex)
        {
            // Закрываем snackbar при ошибке
            _snackbar.Remove(snackbarRef);
            _snackbar.Add($"Ошибка: {ex.Message}", Severity.Error);
            throw;
        }
    }
}