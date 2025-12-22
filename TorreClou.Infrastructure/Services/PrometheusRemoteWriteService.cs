using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Snappier;
using System.Buffers.Binary;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace TorreClou.Infrastructure.Services;

/// <summary>
/// Background service that pushes metrics to Grafana Cloud Prometheus via remote write protocol
/// Uses Snappy compression and Prometheus protobuf format
/// </summary>
public class PrometheusRemoteWriteService : BackgroundService
{
    private readonly ILogger<PrometheusRemoteWriteService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _remoteWriteUrl = "";
    private readonly string _localMetricsUrl;
    private readonly bool _enabled;

    public PrometheusRemoteWriteService(
        ILogger<PrometheusRemoteWriteService> logger,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("PrometheusRemoteWrite");
        
        var observability = configuration.GetSection("Observability");
        var baseUrl = observability["PrometheusUrl"] ?? "";
        var username = observability["PrometheusUsername"] ?? "";
        var apiKey = observability["LokiApiKey"] ?? "";
        
        _enabled = !string.IsNullOrEmpty(baseUrl) && 
                   !string.IsNullOrEmpty(username) && 
                   !baseUrl.Contains("localhost");
        
        if (_enabled)
        {
            // Remote write endpoint
            _remoteWriteUrl = baseUrl.TrimEnd('/') + "/push";
            
            // Set Basic Auth
            var authBytes = Encoding.UTF8.GetBytes($"{username}:{apiKey}");
            _httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
            
            _logger.LogInformation("[PROMETHEUS] Remote Write enabled. Target: {Url}", _remoteWriteUrl);
        }
        
        // Local metrics endpoint (from OpenTelemetry Prometheus exporter)
        _localMetricsUrl = "http://localhost:5019/metrics";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogWarning("[PROMETHEUS] Remote Write is disabled. Check Observability config.");
            return;
        }

        // Wait for app to start
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        
        _logger.LogInformation("[PROMETHEUS] Starting Remote Write service");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PushMetricsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PROMETHEUS] Error pushing metrics");
            }
            
            // Push every 15 seconds
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    private async Task PushMetricsAsync(CancellationToken ct)
    {
        try
        {
            // Read metrics from local endpoint
            using var localClient = new HttpClient();
            var metricsResponse = await localClient.GetAsync(_localMetricsUrl, ct);
            
            if (!metricsResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("[PROMETHEUS] Failed to read local metrics: {Status}", metricsResponse.StatusCode);
                return;
            }

            var metricsText = await metricsResponse.Content.ReadAsStringAsync(ct);
            
            // Parse and convert to protobuf format
            var timeseries = ParsePrometheusMetrics(metricsText);
            
            if (timeseries.Count == 0)
            {
                _logger.LogDebug("[PROMETHEUS] No metrics to push");
                return;
            }
            
            // Build WriteRequest protobuf
            var writeRequest = BuildWriteRequest(timeseries);
            
            // Snappy compress
            var compressed = Snappy.CompressToArray(writeRequest);
            
            // Send to Grafana Cloud
            var content = new ByteArrayContent(compressed);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
            content.Headers.Add("Content-Encoding", "snappy");
            content.Headers.Add("X-Prometheus-Remote-Write-Version", "0.1.0");
            
            var response = await _httpClient.PostAsync(_remoteWriteUrl, content, ct);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("[PROMETHEUS] Failed to push: {Status} - {Error}",
                    response.StatusCode, error);
            }
           
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug("[PROMETHEUS] Skipped (endpoint not ready): {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Parse Prometheus text format into timeseries data
    /// </summary>
    private List<TimeSeriesData> ParsePrometheusMetrics(string metricsText)
    {
        var result = new List<TimeSeriesData>();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        
        // Regex to parse Prometheus metrics: metric_name{label="value"} value
        var metricRegex = new Regex(@"^([a-zA-Z_:][a-zA-Z0-9_:]*)\{?([^}]*)\}?\s+([0-9.eE+-]+|NaN|Inf|-Inf)(?:\s+(\d+))?$", 
            RegexOptions.Multiline);
        
        foreach (Match match in metricRegex.Matches(metricsText))
        {
            var metricName = match.Groups[1].Value;
            var labelsStr = match.Groups[2].Value;
            var valueStr = match.Groups[3].Value;
            
            // Skip NaN and Inf values
            if (valueStr == "NaN" || valueStr.Contains("Inf"))
                continue;
                
            if (!double.TryParse(valueStr, out var value))
                continue;
            
            var labels = new Dictionary<string, string> { ["__name__"] = metricName };
            
            // Parse labels
            if (!string.IsNullOrEmpty(labelsStr))
            {
                var labelRegex = new Regex(@"([a-zA-Z_][a-zA-Z0-9_]*)=""([^""]*)""");
                foreach (Match labelMatch in labelRegex.Matches(labelsStr))
                {
                    labels[labelMatch.Groups[1].Value] = labelMatch.Groups[2].Value;
                }
            }
            
            result.Add(new TimeSeriesData
            {
                Labels = labels,
                Value = value,
                Timestamp = timestamp
            });
        }
        
        return result;
    }

    /// <summary>
    /// Build Prometheus WriteRequest protobuf message
    /// </summary>
    private byte[] BuildWriteRequest(List<TimeSeriesData> timeseries)
    {
        using var stream = new MemoryStream();
        
        foreach (var ts in timeseries)
        {
            var tsBytes = BuildTimeSeries(ts);
            // Field 1 (timeseries), wire type 2 (length-delimited)
            WriteVarint(stream, (1 << 3) | 2);
            WriteVarint(stream, tsBytes.Length);
            stream.Write(tsBytes);
        }
        
        return stream.ToArray();
    }

    private byte[] BuildTimeSeries(TimeSeriesData ts)
    {
        using var stream = new MemoryStream();
        
        // Write labels (field 1)
        foreach (var label in ts.Labels.OrderBy(l => l.Key))
        {
            var labelBytes = BuildLabel(label.Key, label.Value);
            WriteVarint(stream, (1 << 3) | 2);
            WriteVarint(stream, labelBytes.Length);
            stream.Write(labelBytes);
        }
        
        // Write sample (field 2)
        var sampleBytes = BuildSample(ts.Value, ts.Timestamp);
        WriteVarint(stream, (2 << 3) | 2);
        WriteVarint(stream, sampleBytes.Length);
        stream.Write(sampleBytes);
        
        return stream.ToArray();
    }

    private byte[] BuildLabel(string name, string value)
    {
        using var stream = new MemoryStream();
        
        // Field 1: name (string)
        var nameBytes = Encoding.UTF8.GetBytes(name);
        WriteVarint(stream, (1 << 3) | 2);
        WriteVarint(stream, nameBytes.Length);
        stream.Write(nameBytes);
        
        // Field 2: value (string)
        var valueBytes = Encoding.UTF8.GetBytes(value);
        WriteVarint(stream, (2 << 3) | 2);
        WriteVarint(stream, valueBytes.Length);
        stream.Write(valueBytes);
        
        return stream.ToArray();
    }

    private byte[] BuildSample(double value, long timestamp)
    {
        using var stream = new MemoryStream();
        
        // Field 1: value (double) - wire type 1 (64-bit)
        WriteVarint(stream, (1 << 3) | 1);
        var doubleBytes = new byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(doubleBytes, value);
        stream.Write(doubleBytes);
        
        // Field 2: timestamp (int64) - wire type 0 (varint)
        WriteVarint(stream, (2 << 3) | 0);
        WriteVarint(stream, timestamp);
        
        return stream.ToArray();
    }

    private void WriteVarint(Stream stream, long value)
    {
        var uvalue = (ulong)value;
        while (uvalue >= 0x80)
        {
            stream.WriteByte((byte)(uvalue | 0x80));
            uvalue >>= 7;
        }
        stream.WriteByte((byte)uvalue);
    }

    private class TimeSeriesData
    {
        public Dictionary<string, string> Labels { get; set; } = new();
        public double Value { get; set; }
        public long Timestamp { get; set; }
    }
}
