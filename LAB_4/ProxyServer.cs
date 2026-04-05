using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace HttpProxyServer
{
    class ProxyServer
    {
        private const string ProxyIp = "127.0.0.2";
        private const int ProxyPort = 8888;

        static async Task Main(string[] args)
        {
            Console.WriteLine($"Прокси-сервер запущен на {ProxyIp}:{ProxyPort}");
            Console.WriteLine(new string('-', 60));

            var listener = new TcpListener(IPAddress.Parse(ProxyIp), ProxyPort);
            listener.Start();

            try
            {
                while (true)
                {
                    var client = await listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleClientAsync(client));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
            }
            finally
            {
                listener.Stop();
            }
        }

        private static async Task HandleClientAsync(TcpClient clientSocket)
        {
            using (clientSocket)
            using (var clientStream = clientSocket.GetStream())
            {
                try
                {
                    var buffer = new byte[8192];
                    int bytesRead = await clientStream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) return;

                    string requestString = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    string requestLine = requestString.Split(new[] { "\r\n" }, StringSplitOptions.None)[0];
                    string[] requestParts = requestLine.Split(' ');

                    if (requestParts.Length < 3) return;

                    string method = requestParts[0];
                    string fullUrl = requestParts[1];
                    string httpVersion = requestParts[2];

                    if (!TryParseFullUrl(fullUrl, out string host, out int port, out string path))
                        return;

                    string modifiedRequestLine = $"{method} {path} {httpVersion}";
                    string modifiedRequest = modifiedRequestLine + requestString.Substring(requestLine.Length);
                    byte[] modifiedRequestBytes = Encoding.ASCII.GetBytes(modifiedRequest);

                    using (var targetClient = new TcpClient())
                    {
                        await targetClient.ConnectAsync(host, port);
                        using (var targetStream = targetClient.GetStream())
                        {
                            await targetStream.WriteAsync(modifiedRequestBytes, 0, modifiedRequestBytes.Length);
                            await targetStream.FlushAsync();

                            var responseBuffer = new byte[8192];
                            int responseBytesRead;
                            bool isFirstChunk = true;
                            int statusCode = 0;

                            while ((responseBytesRead = await targetStream.ReadAsync(responseBuffer, 0, responseBuffer.Length)) > 0)
                            {
                                if (isFirstChunk && responseBytesRead > 0)
                                {
                                    string responseString = Encoding.ASCII.GetString(responseBuffer, 0, Math.Min(1024, responseBytesRead));

                                    int newLineIndex = responseString.IndexOf('\n');
                                    if (newLineIndex > 0)
                                    {
                                        string responseFirstLine = responseString.Substring(0, newLineIndex).Trim();
                                        string[] responseParts = responseFirstLine.Split(' ');
                                        if (responseParts.Length >= 2)
                                        {
                                            int.TryParse(responseParts[1], out statusCode);
                                        }
                                    }
                                    else
                                    {
                                        string[] responseParts = responseString.Split(' ');
                                        if (responseParts.Length >= 2 && responseParts[0].StartsWith("HTTP/"))
                                        {
                                            int.TryParse(responseParts[1], out statusCode);
                                        }
                                    }

                                    Console.WriteLine($"{fullUrl} - {statusCode}");
                                    isFirstChunk = false;
                                }

                                await clientStream.WriteAsync(responseBuffer, 0, responseBytesRead);
                                await clientStream.FlushAsync();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка: {ex.Message}");
                    try
                    {
                        string errorResponse = "HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\n\r\n";
                        byte[] errorBytes = Encoding.ASCII.GetBytes(errorResponse);
                        await clientStream.WriteAsync(errorBytes, 0, errorBytes.Length);
                        await clientStream.FlushAsync();
                    }
                    catch { }
                }
            }
        }

        private static bool TryParseFullUrl(string url, out string host, out int port, out string path)
        {
            host = null;
            port = 80;
            path = "/";

            try
            {
                string urlWithoutScheme = url;
                if (urlWithoutScheme.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    urlWithoutScheme = urlWithoutScheme.Substring(7);
                }

                int slashIndex = urlWithoutScheme.IndexOf('/');
                string hostPort;
                if (slashIndex >= 0)
                {
                    hostPort = urlWithoutScheme.Substring(0, slashIndex);
                    path = urlWithoutScheme.Substring(slashIndex);
                    if (string.IsNullOrEmpty(path)) path = "/";
                }
                else
                {
                    hostPort = urlWithoutScheme;
                    path = "/";
                }

                int colonIndex = hostPort.IndexOf(':');
                if (colonIndex >= 0)
                {
                    host = hostPort.Substring(0, colonIndex);
                    if (!int.TryParse(hostPort.Substring(colonIndex + 1), out port))
                    {
                        port = 80;
                    }
                }
                else
                {
                    host = hostPort;
                    port = 80;
                }

                return !string.IsNullOrEmpty(host);
            }
            catch
            {
                return false;
            }
        }
    }
}