using System;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using MediatR;
using SmartScout.Application.Interfaces;

namespace SmartScout.Application.UseCases.Analytics;

public sealed record ComparisonPdfResultDto(byte[] PdfBytes, string FileName);

public record GetComparisonReportPdfQuery(
    Guid PlayerAId,
    int SeasonA,
    Guid PlayerBId,
    int SeasonB) : IRequest<ComparisonPdfResultDto>;

public class GetComparisonReportPdfQueryValidator : AbstractValidator<GetComparisonReportPdfQuery>
{
    public GetComparisonReportPdfQueryValidator()
    {
        RuleFor(x => x.PlayerAId).NotEmpty();
        RuleFor(x => x.PlayerBId).NotEmpty();
        RuleFor(x => x.SeasonA).InclusiveBetween(1946, 2100);
        RuleFor(x => x.SeasonB).InclusiveBetween(1946, 2100);
    }
}

public class GetComparisonReportPdfQueryHandler : IRequestHandler<GetComparisonReportPdfQuery, ComparisonPdfResultDto>
{
    private readonly IMediator _mediator;
    private readonly IScoutingReportGenerator _reportGenerator;

    public GetComparisonReportPdfQueryHandler(IMediator mediator, IScoutingReportGenerator reportGenerator)
    {
        _mediator = mediator;
        _reportGenerator = reportGenerator;
    }

    public async Task<ComparisonPdfResultDto> Handle(GetComparisonReportPdfQuery request, CancellationToken cancellationToken)
    {
        var comparison = await _mediator.Send(new ComparePlayersQuery
        {
            PlayerAId = request.PlayerAId,
            SeasonA = request.SeasonA,
            PlayerBId = request.PlayerBId,
            SeasonB = request.SeasonB
        }, cancellationToken);

        byte[] pdfBytes = _reportGenerator.GenerateComparisonPdf(comparison);
        string safeNameA = (comparison.PlayerA.FullName ?? "PlayerA").Replace(" ", "_");
        string safeNameB = (comparison.PlayerB.FullName ?? "PlayerB").Replace(" ", "_");
        return new ComparisonPdfResultDto(pdfBytes, $"SmartScout_Compare_{safeNameA}_vs_{safeNameB}.pdf");
    }
}
