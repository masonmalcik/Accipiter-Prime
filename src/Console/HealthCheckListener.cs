using Google.Protobuf.WellKnownTypes;
using Solnet.Programs.TokenSwap.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Console
{
    public static class HealthCheckListener
    {
        public static Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var port = int.Parse(Environment.GetEnvironmentVariable("PORT") ?? "8080");
                var listener = new TcpListener(IPAddress.Any, port);
                listener.Start();

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        using var client = listener.AcceptTcpClient();
                        using var stream = client.GetStream();
                        var response = "HTTP/1.1 200 OK\r\nContent-Length: 7\r\n\r\nhealthy";
                        var bytes = Encoding.ASCII.GetBytes(response);
                        stream.Write(bytes, 0, bytes.Length);
                    }
                    catch { /* ignore individual connection errors */ }
                }

                listener.Stop();
            }, cancellationToken);
        }
    }
}
