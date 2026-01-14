using Fcg.Contracts.Games;
using Fcg.Games.Api.Repositories;
using MassTransit;

namespace Fcg.Games.Api.Consumers;

public class GamePurchaseRequestedConsumer : IConsumer<GamePurchaseRequestedEvent>
{
    private readonly GameRepository _games;

    public GamePurchaseRequestedConsumer(GameRepository games)
    {
        _games = games;
    }

    public async Task Consume(ConsumeContext<GamePurchaseRequestedEvent> context)
    {
        var (game, promo) = await _games.GetByIdWithPromotionAsync(context.Message.GameId);

        if (game is null)
        {
            await context.Publish(new GamePurchaseRejectedEvent(
                context.Message.CorrelationId,
                context.Message.UserId,
                context.Message.GameId,
                "Game não encontrado.",
                DateTime.UtcNow));
            return;
        }

        var price = game.Price;

        if (promo is not null && promo.IsActive)
        {
            price = Math.Round(game.Price * (1 - promo.DiscountPercentage / 100), 2);
        }

        await context.Publish(new GamePurchaseValidatedEvent(
            context.Message.CorrelationId,
            context.Message.UserId,
            context.Message.GameId,
            price,
            DateTime.UtcNow));
    }
}
