using System.Net;

namespace LBLocker.Infrastructure.Http;

public sealed class LockerApiException : HttpRequestException
{
    public LockerApiException(
        string operation,
        HttpStatusCode? statusCode,
        string? reasonPhrase,
        string responseBody)
        : base(
            $"{operation} failed. HTTP {(int?)statusCode} ({statusCode?.ToString() ?? "no status"}). " +
            $"{reasonPhrase ?? "No reason phrase"}. Response: {responseBody}",
            null,
            statusCode)
    {
        Operation = operation;
        ReasonPhrase = reasonPhrase;
        ResponseBody = responseBody;
    }

    public string Operation { get; }
    public string? ReasonPhrase { get; }
    public string ResponseBody { get; }
}
