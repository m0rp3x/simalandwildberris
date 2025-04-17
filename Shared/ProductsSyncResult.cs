namespace Shared;

public record ProductsSyncResult(
    int ProductsCount,
    int RequestsCount,
    int FetchErrorsCount = 0,
    int SaveErrorsCount = 0,
    bool IsFatalError = false,
    string? FatalErrorMessage = null
);