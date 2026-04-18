using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WishHub.Core.Entities;
using WishHub.Core.Interfaces;
using WishHub.Core.Services;
using WishHub.Infrastructure.Data;

namespace WishHub.Infrastructure.BackgroundJobs;

/// <summary>
/// Фоновый сервис для парсинга продуктов с retry логикой
/// </summary>
public class BackgroundParsingService : IBackgroundParsingService
{
    private readonly IBackgroundJobClient _backgroundJobs;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BackgroundParsingService> _logger;
    private const int MaxRetries = 5;
    private const int RetryDelayMinutes = 2;

    public BackgroundParsingService(
        IBackgroundJobClient backgroundJobs,
        IServiceProvider serviceProvider,
        ILogger<BackgroundParsingService> logger)
    {
        _backgroundJobs = backgroundJobs;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public void EnqueueParsing(Guid productId, string url)
    {
        _logger.LogInformation("Enqueuing background parsing for product {ProductId}", productId);
        _backgroundJobs.Enqueue(() => ParseProductAsync(productId, url, CancellationToken.None));
    }

    public async Task ParseProductAsync(Guid productId, string url, CancellationToken ct = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var parserFactory = scope.ServiceProvider.GetRequiredService<ParserFactory>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<BackgroundParsingService>>();

        try
        {
            var product = await dbContext.Products.FindAsync(new object[] { productId }, ct);
            if (product == null)
            {
                logger.LogWarning("Product {ProductId} not found for parsing", productId);
                return;
            }

            // Если уже обработан — пропускаем
            if (product.ParsingStatus == ParsingStatus.Completed)
            {
                logger.LogInformation("Product {ProductId} already parsed", productId);
                return;
            }

            // Отмечаем что начали обработку
            product.ParsingStatus = ParsingStatus.Processing;
            await dbContext.SaveChangesAsync(ct);

            logger.LogInformation(
                "Starting background parse for product {ProductId}, attempt {Attempt}",
                productId, product.ParseAttempts + 1);

            // Парсим
            var parser = parserFactory.GetParser(url);
            if (parser == null)
            {
                throw new InvalidOperationException($"No parser available for URL: {url}");
            }

            var parseResult = await parser.ParseAsync(url, ct);

            if (parseResult.Success)
            {
                // Успех — обновляем продукт
                product.Name = parseResult.Name ?? product.Name;
                product.ImageUrl = parseResult.ImageUrl ?? product.ImageUrl;
                product.Price = parseResult.Price ?? product.Price;
                product.Currency = parseResult.Currency ?? product.Currency;
                product.Source = parseResult.Source;
                product.LastParsedAt = DateTime.UtcNow;
                product.ParsingStatus = ParsingStatus.Completed;
                product.ParsingError = null;
                product.ParseAttempts++;

                logger.LogInformation(
                    "Successfully parsed product {ProductId}: Name={Name}, Price={Price}",
                    productId, product.Name, product.Price);

                await dbContext.SaveChangesAsync(ct);
            }
            else
            {
                // Неудача — увеличиваем счётчик попыток
                product.ParseAttempts++;
                product.ParsingError = parseResult.ErrorMessage;

                if (product.ParseAttempts >= MaxRetries)
                {
                    // Все попытки исчерпаны
                    product.ParsingStatus = ParsingStatus.Failed;
                    logger.LogWarning(
                        "Product {ProductId} failed parsing after {MaxRetries} attempts. Last error: {Error}",
                        productId, MaxRetries, parseResult.ErrorMessage);
                }
                else
                {
                    // Планируем retry
                    product.ParsingStatus = ParsingStatus.Pending;
                    var nextAttempt = product.ParseAttempts;
                    var delay = RetryDelayMinutes * nextAttempt; // Увеличиваем задержку

                    logger.LogInformation(
                        "Scheduling retry {Attempt}/{MaxRetries} for product {ProductId} in {Delay} minutes",
                        nextAttempt + 1, MaxRetries, productId, delay);

                    await dbContext.SaveChangesAsync(ct);

                    // Запланировать повторную попытку
                    _backgroundJobs.Schedule(
                        () => ParseProductAsync(productId, url, CancellationToken.None),
                        TimeSpan.FromMinutes(delay));
                    return; // Не сохраняем ещё раз
                }

                await dbContext.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during background parsing of product {ProductId}", productId);

            // Пытаемся сохранить ошибку
            try
            {
                dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var product = await dbContext.Products.FindAsync(new object[] { productId }, ct);
                if (product != null)
                {
                    product.ParseAttempts++;
                    product.ParsingError = ex.Message;

                    if (product.ParseAttempts >= MaxRetries)
                    {
                        product.ParsingStatus = ParsingStatus.Failed;
                    }
                    else
                    {
                        // Планируем retry
                        product.ParsingStatus = ParsingStatus.Pending;
                        var delay = RetryDelayMinutes * product.ParseAttempts;

                        await dbContext.SaveChangesAsync(ct);

                        _backgroundJobs.Schedule(
                            () => ParseProductAsync(productId, url, CancellationToken.None),
                            TimeSpan.FromMinutes(delay));
                        return;
                    }

                    await dbContext.SaveChangesAsync(ct);
                }
            }
            catch (Exception saveEx)
            {
                logger.LogError(saveEx, "Failed to save error status for product {ProductId}", productId);
            }
        }
    }
}
