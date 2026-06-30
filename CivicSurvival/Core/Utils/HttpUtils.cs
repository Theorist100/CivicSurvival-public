using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// HTTP utilities with timeout and exponential backoff retry.
    /// Thread-safe, stateless utilities for network operations.
    /// </summary>
#pragma warning disable CA1054 // URI parameters should not be strings - design choice for simpler API
#pragma warning disable CA2234 // Pass System.Uri instead of string - design choice for simpler API
    public static class HttpUtils
    {
        private static readonly LogContext Log = new("HttpUtils");

        // ===== HTTP Status Ranges =====
        private const int SUCCESS_MIN = 200;
        private const int SUCCESS_MAX_EXCLUSIVE = 300;
        private const int REDIRECT_MIN = 300;
        private const int REDIRECT_MAX_EXCLUSIVE = 400;
        private const int CLIENT_ERROR_MIN = 400;
        private const int SERVER_ERROR_MIN = 500;
        private const int RETRY_SLEEP_SLICE_MS = 250;

        // ===== Backoff Constants =====
        private const int MAX_SHIFT_AMOUNT = 20;
        private const double JITTER_FACTOR = 0.25;

        /// <summary>
        /// Default timeout for HTTP requests in milliseconds.
        /// </summary>
        public const int DEFAULT_TIMEOUT_MS = 10000; // 10 seconds

        /// <summary>
        /// Default maximum retry attempts.
        /// </summary>
        public const int DEFAULT_MAX_RETRIES = 3;

        /// <summary>
        /// Base delay for exponential backoff in milliseconds.
        /// </summary>
        public const int BASE_BACKOFF_MS = 1000; // 1 second

        /// <summary>
        /// Maximum backoff delay in milliseconds.
        /// </summary>
        public const int MAX_BACKOFF_MS = 30000; // 30 seconds

        /// <summary>
        /// Maximum response body size in bytes (5 MB). Prevents OOM from malicious/corrupt responses.
        /// </summary>
        public const int MAX_RESPONSE_SIZE = 5 * 1024 * 1024;

        /// <summary>
        /// FIX M20: Read response stream with size limit. Prevents OOM on chunked transfer
        /// encoding where ContentLength=-1 and ReadToEnd reads unbounded.
        /// </summary>
        private static string ReadResponseBounded(Stream stream, int maxSize)
        {
            const int READ_BUFFER_SIZE = 8192;
            var buffer = new byte[READ_BUFFER_SIZE];
            using var ms = new MemoryStream();
            int totalRead = 0;
            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                totalRead += bytesRead;
                if (totalRead > maxSize)
                    throw new InvalidOperationException($"Response exceeds {maxSize} bytes limit");
                ms.Write(buffer, 0, bytesRead);
            }
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        /// <summary>
        /// Result of an HTTP operation.
        /// </summary>
        public sealed class HttpResult
        {
            public bool Success { get; }
            public string Response { get; }
            public int StatusCode { get; }
            public string ErrorMessage { get; }
            public int AttemptsUsed { get; }

            private HttpResult(bool success, string response, int statusCode, string errorMessage, int attemptsUsed)
            {
                Success = success;
                Response = response;
                StatusCode = statusCode;
                ErrorMessage = errorMessage;
                AttemptsUsed = attemptsUsed;
            }

            public static HttpResult Ok(string response, int statusCode, int attempts)
                => IsSuccessStatus(statusCode)
                    ? new(true, response, statusCode, "", attempts)
                    : Fail($"HTTP {statusCode}: non-success response", statusCode, attempts);

            public static HttpResult Fail(string error, int statusCode, int attempts)
                => new(false, "", statusCode, error, attempts);

            // Failure that preserves the server's error response body so callers can read a
            // machine-readable error code (e.g. a nickname rejection reason) on a 4xx response.
            public static HttpResult Fail(string error, int statusCode, int attempts, string body)
                => new(false, body ?? "", statusCode, error, attempts);
        }

        public sealed class TypedHttpResult<T>
        {
            public bool Success { get; }
            public T? Parsed { get; }
            public int StatusCode { get; }
            public string ErrorMessage { get; }
            public int AttemptsUsed { get; }

            public TypedHttpResult(bool success, T? parsed, int statusCode, string errorMessage, int attemptsUsed)
            {
                Success = success;
                Parsed = parsed;
                StatusCode = statusCode;
                ErrorMessage = errorMessage;
                AttemptsUsed = attemptsUsed;
            }
        }

        /// <summary>
        /// POST request with timeout and optional retry with exponential backoff.
        /// </summary>
        /// <param name="url">Target URL</param>
        /// <param name="json">JSON payload</param>
        /// <param name="authToken">Optional Bearer token</param>
        /// <param name="timeoutMs">Request timeout in milliseconds</param>
        /// <param name="maxRetries">Maximum retry attempts (0 = no retry)</param>
        /// <returns>HttpResult with response or error</returns>
        public static HttpResult Post(
            string url,
            string json,
            string authToken = "",
            int timeoutMs = DEFAULT_TIMEOUT_MS,
            int maxRetries = 0)
        {
            var attempts = 0;
            var lastError = "";
            var lastStatusCode = 0;

            while (attempts <= maxRetries)
            {
                attempts++;

                try
                {
                    var request = CreateRequest(url, "POST", authToken, timeoutMs);

                    // Write body
                    var bodyBytes = Encoding.UTF8.GetBytes(json);
                    request.ContentLength = bodyBytes.Length;

                    using (var requestStream = request.GetRequestStream())
                    {
                        requestStream.Write(bodyBytes, 0, bodyBytes.Length);
                    }

                    // Get response
                    using var response = (HttpWebResponse)request.GetResponse();
                    if (response.ContentLength > MAX_RESPONSE_SIZE)
                        return HttpResult.Fail($"Response too large: {response.ContentLength} bytes", 0, attempts);
                    // FIX M20: Bounded read — prevents OOM on chunked transfer encoding
                    var responseBody = ReadResponseBounded(response.GetResponseStream(), MAX_RESPONSE_SIZE);

                    int statusCode = (int)response.StatusCode;
                    if (!IsSuccessStatus(statusCode))
                        return HttpResult.Fail(CreateNonSuccessMessage(response), statusCode, attempts);

                    return HttpResult.Ok(responseBody, statusCode, attempts);
                }
                catch (WebException ex) when (ex.Response is HttpWebResponse errorResponse)
                {
                    // WARN-2-1 fix: dispose response from exception
                    using (errorResponse)
                    {
                        lastStatusCode = (int)errorResponse.StatusCode;
                        lastError = $"HTTP {lastStatusCode}: {ex.Message}";
                        if (Log.IsDebugEnabled) Log.Debug($"POST attempt {attempts}: {lastError}");

                        // Capture the error response body (bounded) so callers can read a
                        // machine-readable error code (e.g. the nickname rejection reason).
                        // Best-effort: a body-read failure must not mask the original HTTP error.
                        var errorBody = "";
                        try
                        {
                            var errorStream = errorResponse.GetResponseStream();
                            if (errorStream != null)
                                errorBody = ReadResponseBounded(errorStream, MAX_RESPONSE_SIZE);
                        }
                        catch (Exception bodyEx)
                        {
                            if (Log.IsDebugEnabled) Log.Debug($"POST error-body read failed: {bodyEx.Message}");
                        }

                        // Don't retry client errors (4xx)
                        if (lastStatusCode >= CLIENT_ERROR_MIN && lastStatusCode < SERVER_ERROR_MIN)
                        {
                            return HttpResult.Fail(lastError, lastStatusCode, attempts, errorBody);
                        }
                    }
                }
                catch (WebException ex)
                {
                    #pragma warning disable CIVIC019 // WebExceptionStatus is an external .NET enum with 20+ values
                    lastError = ex.Status switch
                    {
                        WebExceptionStatus.Timeout => "Request timed out",
                        WebExceptionStatus.ConnectFailure => "Connection failed",
                        WebExceptionStatus.NameResolutionFailure => "DNS resolution failed",
                        _ => ex.Message
                    };
                    #pragma warning restore CIVIC019
                    lastStatusCode = 0;
                    if (Log.IsDebugEnabled) Log.Debug($"POST attempt {attempts}: {lastError}");
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    lastStatusCode = 0;
                    if (Log.IsDebugEnabled) Log.Debug($"POST attempt {attempts}: {lastError}");
                }

                // Exponential backoff before retry
                // NOTE: Using Thread.Sleep instead of Task.Delay because:
                // 1. This is synchronous API (matches Unity's threading model)
                // 2. Retries are rare (network errors only, not normal operation)
                // 3. Max backoff is bounded (see CalculateBackoff)
                // If this becomes a hot path, consider async/await with Task.Delay.
                if (attempts <= maxRetries)
                {
                    if (Mod.IsUnloading) return HttpResult.Fail("Mod unloading", 0, attempts);
                    var delay = CalculateBackoff(attempts);
                    if (!SleepBeforeRetry(delay))
                        return HttpResult.Fail("Mod unloading", 0, attempts);
                }
            }

            return HttpResult.Fail(lastError, lastStatusCode, attempts);
        }

        /// <summary>
        /// GET request with timeout and optional retry with exponential backoff.
        /// </summary>
        public static HttpResult Get(
            string url,
            string authToken = "",
            int timeoutMs = DEFAULT_TIMEOUT_MS,
            int maxRetries = 0)
        {
            var attempts = 0;
            var lastError = "";
            var lastStatusCode = 0;

            while (attempts <= maxRetries)
            {
                attempts++;

                try
                {
                    var request = CreateRequest(url, "GET", authToken, timeoutMs);

                    using var response = (HttpWebResponse)request.GetResponse();
                    if (response.ContentLength > MAX_RESPONSE_SIZE)
                        return HttpResult.Fail($"Response too large ({response.ContentLength} bytes)", (int)response.StatusCode, attempts);
                    // FIX M20: Bounded read — prevents OOM on chunked transfer encoding
                    var responseBody = ReadResponseBounded(response.GetResponseStream(), MAX_RESPONSE_SIZE);

                    int statusCode = (int)response.StatusCode;
                    if (!IsSuccessStatus(statusCode))
                        return HttpResult.Fail(CreateNonSuccessMessage(response), statusCode, attempts);

                    return HttpResult.Ok(responseBody, statusCode, attempts);
                }
                catch (WebException ex) when (ex.Response is HttpWebResponse)
                {
                    using var errorResponse = (HttpWebResponse)ex.Response;
                    lastStatusCode = (int)errorResponse.StatusCode;
                    lastError = $"HTTP {lastStatusCode}: {ex.Message}";
                    if (Log.IsDebugEnabled) Log.Debug($"GET attempt {attempts}: {lastError}");

                    if (lastStatusCode >= CLIENT_ERROR_MIN && lastStatusCode < SERVER_ERROR_MIN)
                    {
                        return HttpResult.Fail(lastError, lastStatusCode, attempts);
                    }
                }
                catch (WebException ex)
                {
                    #pragma warning disable CIVIC019 // WebExceptionStatus is an external .NET enum with 20+ values
                    lastError = ex.Status switch
                    {
                        WebExceptionStatus.Timeout => "Request timed out",
                        WebExceptionStatus.ConnectFailure => "Connection failed",
                        WebExceptionStatus.NameResolutionFailure => "DNS resolution failed",
                        _ => ex.Message
                    };
                    #pragma warning restore CIVIC019
                    lastStatusCode = 0;
                    if (Log.IsDebugEnabled) Log.Debug($"GET attempt {attempts}: {lastError}");
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    lastStatusCode = 0;
                    if (Log.IsDebugEnabled) Log.Debug($"GET attempt {attempts}: {lastError}");
                }

                if (attempts <= maxRetries)
                {
                    if (Mod.IsUnloading) return HttpResult.Fail("Mod unloading", 0, attempts);
                    var delay = CalculateBackoff(attempts);
                    if (!SleepBeforeRetry(delay))
                        return HttpResult.Fail("Mod unloading", 0, attempts);
                }
            }

            return HttpResult.Fail(lastError, lastStatusCode, attempts);
        }

        /// <summary>
        /// Async POST on ThreadPool. Calls onComplete when done.
        /// </summary>
        /// <remarks>onComplete is invoked on a ThreadPool thread, not the main thread.
        /// Do not call EventBus.Publish or access Unity APIs from the callback.</remarks>
        public static void PostAsync(
            string url,
            string json,
            string authToken,
            int timeoutMs,
            int maxRetries,
            Action<HttpResult> onComplete,
            Action? onFinished = null)
        {
#pragma warning disable CIVIC029 // Guarded by Mod.IsUnloading
            ThreadPool.QueueUserWorkItem(_ =>
#pragma warning restore CIVIC029
            {
                try
                {
                    if (Mod.IsUnloading) return;
                    var result = Post(url, json, authToken, timeoutMs, maxRetries);
                    if (Mod.IsUnloading) return;
                    try
                    {
                        onComplete?.Invoke(result);
                    }
                    catch (Exception ex)
                    {
                        Log.WarnException("PostAsync completion callback failed", ex);
                    }
                }
                finally
                {
                    try
                    {
                        onFinished?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Log.WarnException("PostAsync finished callback failed", ex);
                    }
                }
            });
        }

        public static void PostAsync<TResponse>(
            string url,
            string json,
            string authToken,
            int timeoutMs,
            int maxRetries,
            Func<string, TResponse> parser,
            Action<TypedHttpResult<TResponse>> onComplete,
            Action? onFinished = null)
        {
            if (parser == null) throw new ArgumentNullException(nameof(parser));
#pragma warning disable CIVIC029 // Guarded by Mod.IsUnloading
            ThreadPool.QueueUserWorkItem(_ =>
#pragma warning restore CIVIC029
            {
                try
                {
                    if (Mod.IsUnloading) return;
                    var result = Post(url, json, authToken, timeoutMs, maxRetries);
                    if (Mod.IsUnloading) return;
                    TypedHttpResult<TResponse> typed;
                    if (!result.Success)
                    {
                        typed = new TypedHttpResult<TResponse>(false, default, result.StatusCode, result.ErrorMessage, result.AttemptsUsed);
                    }
                    else
                    {
                        try
                        {
                            typed = new TypedHttpResult<TResponse>(true, parser(result.Response), result.StatusCode, "", result.AttemptsUsed);
                        }
                        catch (Exception ex)
                        {
                            Log.WarnException("PostAsync response parse failed", ex);
                            typed = new TypedHttpResult<TResponse>(false, default, result.StatusCode, $"Response parse failed: {ex.Message}", result.AttemptsUsed);
                        }
                    }

                    try
                    {
                        onComplete?.Invoke(typed);
                    }
                    catch (Exception ex)
                    {
                        Log.WarnException("PostAsync completion callback failed", ex);
                    }
                }
                finally
                {
                    try
                    {
                        onFinished?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Log.WarnException("PostAsync finished callback failed", ex);
                    }
                }
            });
        }

        /// <summary>
        /// Maximum upload size for a multipart file POST (crash-dump zip). 60 MB ceiling — above
        /// the Discord L2 50 MB cap so the server, not the client, owns the final size verdict.
        /// </summary>
        public const int MAX_UPLOAD_BYTES = 60 * 1024 * 1024;

        // 80 KB stream-copy buffer for the multipart file upload (Stream.CopyTo default).
        private const int UploadStreamBufferBytes = 81920;

        /// <summary>
        /// Synchronous multipart/form-data POST that streams a file part from disk (chunked, so the
        /// whole body is never held in memory). For the crash-dump upload: the caller runs this on a
        /// background <c>Task</c> (off the main thread — read+compress+send is hundreds of ms..s and
        /// would ANR the game on the UI thread). Never call from the main thread.
        /// </summary>
        public static HttpResult PostMultipartFile(
            string url,
            string authToken,
            string filePath,
            string fileFieldName,
            string fileName,
            string fileContentType,
            System.Collections.Generic.IReadOnlyDictionary<string, string>? formFields,
            int timeoutMs = DEFAULT_TIMEOUT_MS)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists)
                    return HttpResult.Fail("Upload file missing", 0, 1);
                if (fileInfo.Length > MAX_UPLOAD_BYTES)
                    return HttpResult.Fail($"Upload too large: {fileInfo.Length} bytes", 0, 1);

#if !DEBUG
                if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Refusing non-https upload URL");
#endif

                string boundary = "----CivicDump" + Guid.NewGuid().ToString("N");
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.Timeout = timeoutMs;
                request.ReadWriteTimeout = timeoutMs;
                request.ContentType = "multipart/form-data; boundary=" + boundary;
                request.AllowAutoRedirect = false;
                request.KeepAlive = false;
                request.ServicePoint.Expect100Continue = false;
                request.SendChunked = true; // stream the body; no up-front ContentLength for a large file
                if (!string.IsNullOrEmpty(authToken))
                    request.Headers[HttpRequestHeader.Authorization] = $"Bearer {authToken}";

                using (var requestStream = request.GetRequestStream())
                {
                    var head = new StringBuilder();
                    if (formFields != null)
                    {
                        foreach (var field in formFields)
                        {
                            head.Append("--").Append(boundary).Append("\r\n");
                            head.Append("Content-Disposition: form-data; name=\"").Append(field.Key).Append("\"\r\n\r\n");
                            head.Append(field.Value ?? string.Empty).Append("\r\n");
                        }
                    }
                    head.Append("--").Append(boundary).Append("\r\n");
                    head.Append("Content-Disposition: form-data; name=\"").Append(fileFieldName)
                        .Append("\"; filename=\"").Append(fileName).Append("\"\r\n");
                    head.Append("Content-Type: ").Append(fileContentType).Append("\r\n\r\n");

                    var headBytes = Encoding.UTF8.GetBytes(head.ToString());
                    requestStream.Write(headBytes, 0, headBytes.Length);

                    using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        var buffer = new byte[UploadStreamBufferBytes];
                        int read;
                        while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            if (Mod.IsUnloading) return HttpResult.Fail("Mod unloading", 0, 1);
                            requestStream.Write(buffer, 0, read);
                        }
                    }

                    var tail = Encoding.UTF8.GetBytes("\r\n--" + boundary + "--\r\n");
                    requestStream.Write(tail, 0, tail.Length);
                }

                using var response = (HttpWebResponse)request.GetResponse();
                int statusCode = (int)response.StatusCode;
                var body = ReadResponseBounded(response.GetResponseStream(), MAX_RESPONSE_SIZE);
                return IsSuccessStatus(statusCode)
                    ? HttpResult.Ok(body, statusCode, 1)
                    : HttpResult.Fail(CreateNonSuccessMessage(response), statusCode, 1);
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse errorResponse)
            {
                using (errorResponse)
                {
                    if (Log.IsDebugEnabled) Log.Debug($"Multipart POST failed: HTTP {(int)errorResponse.StatusCode}: {ex.Message}");
                    return HttpResult.Fail($"HTTP {(int)errorResponse.StatusCode}: {ex.Message}", (int)errorResponse.StatusCode, 1);
                }
            }
            catch (Exception ex)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Multipart POST failed: {ex.Message}");
                return HttpResult.Fail(ex.Message, 0, 1);
            }
        }

        private static HttpWebRequest CreateRequest(string url, string method, string authToken, int timeoutMs)
        {
#if !DEBUG
            // TLS-enforcement guard, defense in depth. TelemetryConfig.NormalizeServerUrl
            // already forces https at every config site; this catches any callsite that builds a
            // request from an unnormalized URL, so a non-https URL can never carry
            // auth_token / player_id. DEBUG keeps http loopback for local dev servers.
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing non-https telemetry request URL");
#endif

            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            request.ContentType = "application/json";
            request.Accept = "application/json";

            // Disable automatic redirect following for security
            request.AllowAutoRedirect = false;

            // Connection settings
            request.KeepAlive = false;
            request.ServicePoint.Expect100Continue = false;

            if (!string.IsNullOrEmpty(authToken))
            {
                request.Headers[HttpRequestHeader.Authorization] = $"Bearer {authToken}";
            }

            return request;
        }

        /// <summary>
        /// Calculate exponential backoff delay with jitter.
        /// </summary>
        private static int CalculateBackoff(int attempt)
        {
            // Exponential: 1s, 2s, 4s, 8s, ... (clamp shift to prevent overflow)
            var shiftAmount = Math.Min(attempt - 1, MAX_SHIFT_AMOUNT); // 2^20 = 1M, safe with BASE_BACKOFF_MS
            var exponentialDelay = (long)BASE_BACKOFF_MS * (1 << shiftAmount);

            // Cap at max (cast back to int — MAX_BACKOFF_MS fits int)
            var cappedDelay = (int)Math.Min(exponentialDelay, MAX_BACKOFF_MS);

            // Add jitter (±25%) using centralized ThreadSafeRandom
            var jitter = (int)Math.Round(cappedDelay * JITTER_FACTOR * (ThreadSafeRandom.NextDouble() * 2 - 1));

            return cappedDelay + jitter;
        }

        private static bool IsSuccessStatus(int statusCode) =>
            statusCode >= SUCCESS_MIN && statusCode < SUCCESS_MAX_EXCLUSIVE;

        private static string CreateNonSuccessMessage(HttpWebResponse response)
        {
            int statusCode = (int)response.StatusCode;
            string location = response.Headers[HttpResponseHeader.Location];
            if (statusCode >= REDIRECT_MIN && statusCode < REDIRECT_MAX_EXCLUSIVE && !string.IsNullOrEmpty(location))
                return $"HTTP {statusCode}: redirect to {location}";
            return $"HTTP {statusCode}: {response.StatusDescription}";
        }

        private static bool SleepBeforeRetry(int delayMs)
        {
            int remaining = delayMs;
            while (remaining > 0)
            {
                if (Mod.IsUnloading) return false;
                int slice = Math.Min(remaining, RETRY_SLEEP_SLICE_MS);
                Thread.Sleep(slice);
                remaining -= slice;
            }
            return !Mod.IsUnloading;
        }
    }
}
