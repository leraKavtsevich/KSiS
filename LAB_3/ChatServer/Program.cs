using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ChatServer
{
    class Program
    {
        static TcpListener tcpListener;
        static List<TcpClient> clients = new List<TcpClient>();
        static List<ClientInfo> clientInfos = new List<ClientInfo>();
        static bool isRunning = true;
        static int tcpPort;
        static IPAddress serverIp;

        class ClientInfo
        {
            public IPAddress IpAddress { get; set; }
            public int TcpPort { get; set; }
            public string Name { get; set; }
            public TcpClient TcpClient { get; set; }
        }

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            Console.WriteLine("     ЧАТ СЕРВЕР     ");
            Console.WriteLine("=====================");

            Console.Write("Введите IP сервера: ");
            string ipInput = Console.ReadLine().Trim();

            while (!IPAddress.TryParse(ipInput, out serverIp))
            {
                Console.Write("Неверный формат IP. Введите корректный IP: ");
                ipInput = Console.ReadLine().Trim();
            }

            Console.Write("Введите TCP порт сервера: ");
            while (!int.TryParse(Console.ReadLine().Trim(), out tcpPort) || tcpPort < 1 || tcpPort > 65535)
            {
                Console.Write("Неверный порт. Введите число от 1 до 65535: ");
            }

            if (!IsPortAvailable(tcpPort))
            {
                Console.WriteLine($"\nОшибка: TCP порт {tcpPort} уже используется!");
                Console.ReadLine();
                return;
            }

            try
            {
                tcpListener = new TcpListener(serverIp, tcpPort);
                tcpListener.Start();

                Console.WriteLine($"\nСЕРВЕР ЗАПУЩЕН:");
                Console.WriteLine($"IP адрес: {serverIp}");
                Console.WriteLine($"TCP порт: {tcpPort}");
                Console.WriteLine($"Время запуска: {DateTime.Now:HH:mm:ss}");
                Console.WriteLine($"\nОжидание подключений клиентов...\n");

                Console.WriteLine("ДОСТУПНЫЕ КОМАНДЫ:");
                Console.WriteLine("/exit - остановка сервера");
                Console.WriteLine("----------------------------------------");

                _ = Task.Run(AcceptClients);

                while (isRunning)
                {
                    string command = Console.ReadLine().Trim().ToLower();

                    if (command == "/exit")
                    {
                        isRunning = false;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nОшибка: {ex.Message}");
                Console.ReadLine();
            }
            finally
            {
                tcpListener?.Stop();
                foreach (var c in clients) c?.Close();
                Console.WriteLine($"\nСервер остановлен в {DateTime.Now:HH:mm:ss}");
                Console.ReadLine();
            }
        }

        static bool IsPortAvailable(int port)
        {
            try
            {
                var props = IPGlobalProperties.GetIPGlobalProperties();

                if (props.GetActiveTcpListeners().Any(ep => ep.Port == port))
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return true;
            }
        }

        static bool IsClientUnique(IPAddress ip, int clientTcpPort)
        {
            if (clientTcpPort == tcpPort)
            {
                return false;
            }

            if (clientInfos.Any(c => c.IpAddress.Equals(ip)))
            {
                return false;
            }

            if (clientInfos.Any(c => c.TcpPort == clientTcpPort))
            {
                return false;
            }

            return true;
        }

        static async Task AcceptClients()
        {
            while (isRunning)
            {
                try
                {
                    TcpClient client = await tcpListener.AcceptTcpClientAsync();

                    IPEndPoint clientEp = (IPEndPoint)client.Client.RemoteEndPoint;

                    NetworkStream tempStream = client.GetStream();
                    byte[] buffer = new byte[4096];
                    int bytesRead = await tempStream.ReadAsync(buffer, 0, buffer.Length);

                    if (bytesRead == 0)
                    {
                        client.Close();
                        continue;
                    }

                    string clientName = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                    IPAddress clientIp = clientEp.Address;
                    int clientTcpPort = clientEp.Port;

                    Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] Попытка подключения:");
                    Console.WriteLine($"IP клиента: {clientIp}");
                    Console.WriteLine($"TCP порт клиента: {clientTcpPort}");
                    Console.WriteLine($"Имя клиента: {clientName}");

                    if (clientIp.Equals(serverIp))
                    {
                        Console.WriteLine($"Отказ: IP клиента совпадает с IP сервера");
                        byte[] rejectData = Encoding.UTF8.GetBytes("ERROR:IP_CONFLICT_SERVER");
                        await tempStream.WriteAsync(rejectData, 0, rejectData.Length);
                        client.Close();
                        continue;
                    }

                    if (!IsClientUnique(clientIp, clientTcpPort))
                    {
                        Console.WriteLine($"Отказ: Конфликт с существующим клиентом");

                        string errorReason = "CLIENT_CONFLICT";
                        if (clientTcpPort == tcpPort)
                        {
                            errorReason = "PORT_CONFLICT_WITH_SERVER";
                        }

                        byte[] rejectData = Encoding.UTF8.GetBytes($"ERROR:{errorReason}");
                        await tempStream.WriteAsync(rejectData, 0, rejectData.Length);
                        client.Close();
                        continue;
                    }

                    Console.WriteLine($"Клиент принят: Все проверки пройдены");

                    clients.Add(client);

                    ClientInfo clientInfo = new ClientInfo
                    {
                        IpAddress = clientIp,
                        TcpPort = clientTcpPort,
                        Name = clientName,
                        TcpClient = client
                    };
                    clientInfos.Add(clientInfo);

                    byte[] confirmData = Encoding.UTF8.GetBytes("CONNECTED");
                    await tempStream.WriteAsync(confirmData, 0, confirmData.Length);

                    string joinMsg = $"{clientName} присоединился к чату";
                    await BroadcastMessage(joinMsg, client);

                    _ = Task.Run(() => HandleClient(clientInfo));
                }
                catch (Exception ex)
                {
                    if (isRunning)
                        Console.WriteLine($"Ошибка при принятии клиента: {ex.Message}");
                }
            }
        }

        static async Task HandleClient(ClientInfo clientInfo)
        {
            TcpClient client = clientInfo.TcpClient;
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[4096];

            try
            {
                while (isRunning && client.Connected)
                {
                    try
                    {
                        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                        if (bytesRead == 0) break;

                        string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {clientInfo.Name}: {message}");

                        string formattedMessage = $"{clientInfo.Name}: {message}";

                        await BroadcastMessage(formattedMessage, client);
                    }
                    catch { break; }
                }
            }
            catch { }
            finally
            {
                clientInfos.Remove(clientInfo);
                clients.Remove(client);
                client.Close();

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Клиент отключился: {clientInfo.Name}");

                string leaveMsg = $"{clientInfo.Name} покинул чат";
                await BroadcastMessage(leaveMsg);
            }
        }

        static async Task BroadcastMessage(string message, TcpClient senderClient = null)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);

            foreach (var client in clientInfos)
            {
                if (senderClient != null && client.TcpClient == senderClient)
                    continue;

                if (client.TcpClient.Connected)
                {
                    try
                    {
                        await client.TcpClient.GetStream().WriteAsync(data, 0, data.Length);
                    }
                    catch { }
                }
            }
        }
    }
}