using MediatR;

namespace SmartScout.Application.UseCases.Players;

public class SyncPlayersCommand : IRequest<int>
{
    public int Cursor { get; set; } = 0;
    public string? League { get; set; }
    public string? Season { get; set; }
    public bool Force { get; set; } = false;
}
