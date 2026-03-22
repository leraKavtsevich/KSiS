using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ChatClient
{
    class Program
    {
        static TcpClient tcpClient;
        static NetworkStream stream;
        static string userName;
        static bool isConnected = true;
        static int tcpPort;
        static IPAddress localIp, serverIp;
        static int serverTcpPort;

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            Console.WriteLine("    ЧАТ КЛИЕНТ      ");
            Console.WriteLine("====================");

            Console.Write("Ваше имя: ");
            userName = Console.ReadLine().Trim();

            while (string.IsNullOrWhiteSpace(userName))
            {
                Console.Write("Имя не может быть пустым. Введите имя: ");
                userName = Console.ReadLine().Trim();
            }

            bool connected = false;
            while (!connected)
            {
                try
                {
                    Console.Write("Ваш локальный IP-адрес: ");
                    string localIpInput = Console.ReadLine().Trim();
                    while (!IPAddress.TryParse(localIpInput, out localIp))
                    {
                        Console.Write("Неверный формат IP. Введите корректный IP: ");
                        localIpInput = Console.ReadLine().Trim();
                    }

                    Console.Write("Ваш TCP порт: ");
                    while (!int.TryParse(Console.ReadLine().Trim(), out tcpPort) || tcpPort < 1 || tcpPort > 65535)
                    {
                        Console.Write("Неверный порт. Введите число от 1 до 65535: ");
                    }

                    Console.Write("IP сервера: ");
                    string serverIpInput = Console.ReadLine().Trim();
                    while (!IPAddress.TryParse(serverIpInput, out serverIp))
                    {
                        Console.Write("Неверный формат IP. Введите корректный IP: ");
                        serverIpInput = Console.ReadLine().Trim();
                    }


                    Console.Write("TCP порт сервера: ");
                    while (!int.TryParse(Console.ReadLine().Trim(), out serverTcpPort) || serverTcpPort < 1 || serverTcpPort > 65535)
                    {
                        Console.Write("Неверный порт. Введите число от 1 до 65535: ");
                    }


                    Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] Подключение к серверу {serverIp}:{serverTcpPort}");

                    IPEndPoint localEndPoint = new IPEndPoint(localIp, tcpPort);
                    tcpClient = new TcpClient(localEndPoint);
                    await tcpClient.ConnectAsync(serverIp, serverTcpPort);
                    stream = tcpClient.GetStream();

                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Подключено!");
                    Console.WriteLine($"Локальный адрес: {localIp}:{tcpPort} (TCP)");

                    byte[] nameData = Encoding.UTF8.GetBytes(userName);
                    await stream.WriteAsync(nameData, 0, nameData.Length);
                    await stream.FlushAsync();

                    byte[] buffer = new byte[4096];
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    string serverResponse = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    if (serverResponse.StartsWith("ERROR:"))
                    {
                        string errorMsg = serverResponse.Substring(6);
                        switch (errorMsg)
                        {
                            case "IP_CONFLICT_SERVER":
                                Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] Ошибка: Ваш IP ({localIp}) совпадает с IP сервера!");
                                break;
                            case "PORT_CONFLICT_WITH_SERVER":
                                Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] Ошибка: Ваш TCP порт ({tcpPort}) совпадает с портом сервера!");
                                break;
                            case "CLIENT_CONFLICT":
                                Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] Ошибка: Конфликт с существующим клиентом!");
                                Console.WriteLine("Возможные причины:");
                                Console.WriteLine($"  - Ваш IP {localIp} уже используется другим клиентом");
                                Console.WriteLine($"  - Ваш TCP порт {tcpPort} уже используется другим клиентом");
                                break;
                            default:
                                Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] Ошибка подключения: {errorMsg}");
                                break;
                        }

                        tcpClient.Close();
                        Console.WriteLine("\nХотите попробовать снова? (y/n): ");
                        if (Console.ReadLine().Trim().ToLower() != "y")
                            return;
                        continue;
                    }

                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Подключение подтверждено сервером\n");
                    connected = true;
                }
                catch (SocketException ex)
                {
                    Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] Ошибка подключения: {ex.Message}");
                    Console.WriteLine("Возможные причины:");
                    Console.WriteLine("  - Сервер не запущен");
                    Console.WriteLine("  - Неверный IP или порт сервера");
                    Console.WriteLine("  - Ваш локальный порт уже занят");
                    Console.WriteLine("  - Брандмауэр блокирует соединение");

                    tcpClient?.Close();
                    Console.WriteLine("\nХотите попробовать снова? (y/n): ");
                    if (Console.ReadLine().Trim().ToLower() != "y")
                        return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] Ошибка: {ex.Message}");

                    tcpClient?.Close();
                    Console.WriteLine("\nХотите попробовать снова? (y/n): ");
                    if (Console.ReadLine().Trim().ToLower() != "y")
                        return;
                }
            }

            _ = Task.Run(ReceiveMessages);
            _ = Task.Run(CheckConnection);

            Console.WriteLine("Команда:");
            Console.WriteLine("/exit - выход");
            Console.WriteLine("----------------------------------------");

            while (isConnected && tcpClient.Connected)
            {
                Console.Write("> ");
                string message = Console.ReadLine();

                if (message == "/exit")
                    break;

                if (string.IsNullOrWhiteSpace(message))
                    continue;

                byte[] data = Encoding.UTF8.GetBytes(message);
                await stream.WriteAsync(data, 0, data.Length);
                await stream.FlushAsync();
            }

            isConnected = false;
            tcpClient?.Close();
            Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] Соединение с сервером разорвано");
            Console.WriteLine("Нажмите Enter для выхода...");
            Console.ReadLine();
        }

        static async Task ReceiveMessages()
        {
            byte[] buffer = new byte[4096];

            while (isConnected && tcpClient != null && tcpClient.Connected)
            {
                try
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

                    if (bytesRead == 0)
                    {
                        Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] Сервер закрыл соединение");
                        break;
                    }

                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Console.WriteLine($"\n{message}");
                    Console.Write("> ");
                }
                catch
                {
                    break;
                }
            }
        }

        static async Task CheckConnection()
        {
            while (isConnected)
            {
                try
                {
                    if (tcpClient != null && tcpClient.Client != null)
                    {
                        if (tcpClient.Client.Poll(0, SelectMode.SelectRead))
                        {
                            byte[] buff = new byte[1];
                            if (tcpClient.Client.Receive(buff, SocketFlags.Peek) == 0)
                            {
                                Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] Потеря соединения с сервером");
                                isConnected = false;
                               Environment.Exit(0);
                            }
                        }
                    }
                    await Task.Delay(1000);
                }
                catch
                {
                    if (isConnected)
                    {
                        Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] Потеря соединения с сервером");
                        isConnected = false;
                        Environment.Exit(0);
                    }
                }
            }
        }
    }
}