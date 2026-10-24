using SmartScout.Application.DTOs;

namespace SmartScout.Application.Interfaces;

public interface IScoutingReportGenerator
{
    byte[] GenerateReportPdf(PlayerScoutingReportDto model);
}
