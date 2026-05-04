using Lingarr.Core.Entities;
using Lingarr.Server.Models;

namespace Lingarr.Server.Models.Api;

public class TranslationRequestsOverviewResponse
{
    public int ActiveCount { get; set; }

    public required PagedResult<TranslationRequest> Pending { get; set; }

    public required TranslationRequestSectionResponse Failed { get; set; }

    public required TranslationRequestSectionResponse InProgress { get; set; }
}

public class TranslationRequestSectionResponse
{
    public required List<TranslationRequest> Items { get; set; }

    public int TotalCount { get; set; }
}
