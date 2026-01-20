using System.Text.Json;
using System.Threading.Channels;
using Lingarr.Server.Providers;
using Microsoft.AspNetCore.Mvc;

namespace Lingarr.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LogsController : ControllerBase
    {
        [HttpGet("stream")]
        public async Task GetLogStreamAsync(CancellationToken cancellationToken)
        {
            Response.Headers.Append("Content-Type", "text/event-stream");
            Response.Headers.Append("Cache-Control", "no-cache");
            Response.Headers.Append("Connection", "keep-alive");
            
            // Send initial recent logs
            foreach (var log in InMemoryLogSink.GetRecentLogs(400))
            {
                string json = JsonSerializer.Serialize(log);
                await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
            }
            await Response.Body.FlushAsync(cancellationToken);
            
            var channel = Channel.CreateUnbounded<LogEntry>();
            
            void Handler(object? sender, LogEntry log) 
            {
                channel.Writer.TryWrite(log);
            }
            
            InMemoryLogSink.OnLogAdded += Handler;
            
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(15));
                    
                    try 
                    {
                        var log = await channel.Reader.ReadAsync(cts.Token);
                        string json = JsonSerializer.Serialize(log);
                        await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                        await Response.Body.FlushAsync(cancellationToken);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        await Response.WriteAsync(": heartbeat\n\n", cancellationToken);
                        await Response.Body.FlushAsync(cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                InMemoryLogSink.OnLogAdded -= Handler;
            }
        }
    }
}