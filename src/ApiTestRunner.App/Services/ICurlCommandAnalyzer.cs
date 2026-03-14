using ApiTestRunner.App.Models;

namespace ApiTestRunner.App.Services;

public interface ICurlCommandAnalyzer
{
    Task<CurlAnalyzeResponse> AnalyzeAsync(CurlAnalyzeRequest request, CancellationToken cancellationToken = default);
}
