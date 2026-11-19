using System;
using System.Linq.Expressions;
using SmartScout.Domain.Entities;

namespace SmartScout.Application.Common.Queries;

public static class InjuryPredicates
{
    public static readonly Expression<Func<PlayerInjury, bool>> IsActive =
        i => i.Status != "Recovered" && i.Status != "Healthy" && i.Status != "Cleared";
}
