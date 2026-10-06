using MediatR;

namespace SmartScout.Application.UseCases.Players;

public class SyncPlayersCommand : IRequest<int>
{
    // E.g., we could pass a cursor or page number here, but let's keep it simple
    public int Cursor { get; set; } = 0;
}
