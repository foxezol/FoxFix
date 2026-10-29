using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

namespace FoxFix.Core.Network;

public enum HttpsProbeOutcome
{
    /// <summary>The TLS handshake worked and the server answered (any HTTP status counts).</summary>
    Ok,
    Timeout,
    TlsFailed,
    CertificateError,
    DnsFailed,
    Other,
}

public interface IHttpsProbe
{
    Task<HttpsProbeOutcome> ProbeAsync(Uri url, TimeSpan timeout, CancellationToken ct);
}

public static class HttpsErrorClassifier
{
    public static HttpsProbeOutcome Classify(Exception exception, bool certificateRejected)
    {
        if (certificateRejected)
        {
            return HttpsProbeOutcome.CertificateError;
        }

        if (exception is OperationCanceledException or TimeoutException)
        {
            return HttpsProbeOutcome.Timeout;
        }

        if (exception is HttpRequestException http)
        {
            switch (http.HttpRequestError)
            {
                case HttpRequestError.NameResolutionError:
                    return HttpsProbeOutcome.DnsFailed;
                case HttpRequestError.SecureConnectionError:
                    return HttpsProbeOutcome.TlsFailed;
            }

            return ClassifyInner(http.InnerException);
        }

        return ClassifyInner(exception);
    }

    private static HttpsProbeOutcome ClassifyInner(Exception? inner)
    {
        return inner switch
        {
            null => HttpsProbeOutcome.Other,
            AuthenticationException => HttpsProbeOutcome.TlsFailed,
            TimeoutException or OperationCanceledException => HttpsProbeOutcome.Timeout,
            SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData } =>
                HttpsProbeOutcome.DnsFailed,
            SocketException { SocketErrorCode: SocketError.TimedOut } => HttpsProbeOutcome.Timeout,
            IOException when inner.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset } =>
                HttpsProbeOutcome.TlsFailed,
            _ => ClassifyInner(inner.InnerException),
        };
    }
}

public sealed class SystemHttpsProbe : IHttpsProbe
{
    public async Task<HttpsProbeOutcome> ProbeAsync(Uri url, TimeSpan timeout, CancellationToken ct)
    {
        var certificateRejected = false;

        using var handler = new SocketsHttpHandler
        {
            ConnectTimeout = timeout,
            AllowAutoRedirect = false,
        };
        handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, errors) =>
        {
            if (errors == SslPolicyErrors.None)
            {
                return true;
            }

            certificateRejected = true;
            return false;
        };

        using var client = new HttpClient(handler, disposeHandler: false) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            return HttpsProbeOutcome.Ok;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return HttpsErrorClassifier.Classify(ex, certificateRejected);
        }
    }
}
