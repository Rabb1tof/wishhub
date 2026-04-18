namespace WishHub.Core.Interfaces;

/// <summary>
/// Сервис для фонового парсинга продуктов
/// </summary>
public interface IBackgroundParsingService
{
    /// <summary>
    /// Запускает фоновый парсинг продукта
    /// </summary>
    /// <param name="productId">ID продукта для парсинга</param>
    /// <param name="url">URL для парсинга</param>
    void EnqueueParsing(Guid productId, string url);

    /// <summary>
    /// Выполняет парсинг продукта (вызывается Hangfire)
    /// </summary>
    Task ParseProductAsync(Guid productId, string url, CancellationToken ct = default);
}
