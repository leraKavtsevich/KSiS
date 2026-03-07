using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace MYTRACERT
{
    internal class MyTracert
    {
        static void Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("Использование: TRACERT.exe <IP-адрес>");
                return;
            }

            string target = args[0];
            IPAddress destinationIP = IPAddress.Parse(target);

            Console.WriteLine($"Трассировка маршрута к {destinationIP}");
            Console.WriteLine("с максимальным числом прыжков 30:\n");

            const int maxHops = 30;
            const int packetsPerHop = 3;
            const int timeoutMs = 3000;

            ushort sequence = 1;

            for (int ttl = 1; ttl <= maxHops; ttl++)
            {
                Console.Write($"{ttl,2}  ");

                IPAddress currentHopIP = null;
                long[] responseTimes = new long[packetsPerHop];
                bool destinationReached = false;

                for (int packetNum = 0; packetNum < packetsPerHop; packetNum++)
                {
                    try
                    {
                        using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Icmp))
                        {
                            socket.ReceiveTimeout = timeoutMs;
                            socket.Ttl = (short)ttl;

                            byte[] icmpPacket = CreateIcmpEchoRequest(sequence++);

                            IPEndPoint remoteEndPoint = new IPEndPoint(destinationIP, 0);
                            EndPoint senderEndPoint = new IPEndPoint(IPAddress.Any, 0);

                            DateTime sendTime = DateTime.Now;

                            socket.SendTo(icmpPacket, remoteEndPoint);

                            byte[] receiveBuffer = new byte[1024];
                            int receivedBytes = socket.ReceiveFrom(receiveBuffer, ref senderEndPoint);

                            TimeSpan responseTime = DateTime.Now - sendTime;
                            IPAddress responderIP = ((IPEndPoint)senderEndPoint).Address;

                            if (currentHopIP == null)
                                currentHopIP = responderIP;

                            if (responseTime.TotalMilliseconds < timeoutMs)
                                responseTimes[packetNum] = (long)responseTime.TotalMilliseconds;
                            else
                                responseTimes[packetNum] = 0;

                            if (receivedBytes >= 28)
                            {
                                int icmpType = receiveBuffer[20];

                                if (icmpType == 0)
                                {
                                    destinationReached = true;
                                }
                                else if (icmpType == 11)
                                {
                                    destinationReached = false;
                                }
                            }
                        }

                        Thread.Sleep(100);
                    }
                    catch (SocketException)
                    {
                        responseTimes[packetNum] = 0;
                    }
                }

                for (int i = 0; i < packetsPerHop; i++)
                {
                    if (responseTimes[i] > 0)
                        Console.Write($"{responseTimes[i],4} ms  ");
                    else
                        Console.Write("   *   ");
                }

                if (currentHopIP != null)
                {
                    Console.Write($"{currentHopIP,-16}");
                    Console.WriteLine();
                }
                else
                {
                    Console.WriteLine("   *   ");
                }

                if (destinationReached || (currentHopIP != null && currentHopIP.Equals(destinationIP)))
                {
                    Console.WriteLine("Трассировка завершена.");
                    return;
                }
            }
        }

        static byte[] CreateIcmpEchoRequest(ushort sequence)
        {
            byte[] packet = new byte[40];

            packet[0] = 8;
            packet[1] = 0;
            packet[2] = 0;
            packet[3] = 0;

            ushort id = (ushort)(System.Diagnostics.Process.GetCurrentProcess().Id % 65535);

            packet[4] = (byte)(id >> 8);
            packet[5] = (byte)(id & 0xFF);

            packet[6] = (byte)(sequence >> 8);
            packet[7] = (byte)(sequence & 0xFF);

            for (int i = 8; i < packet.Length; i++)
            {
                packet[i] = (byte)i;
            }

            ushort checksum = CalculateChecksum(packet);

            packet[2] = (byte)(checksum >> 8);
            packet[3] = (byte)(checksum & 0xFF);

            return packet;
        }

        static ushort CalculateChecksum(byte[] data)
        {
            long sum = 0;

            for (int i = 0; i < data.Length; i += 2)
            {
                if (i + 1 < data.Length)
                    sum += (data[i] << 8) + data[i + 1];
                else
                    sum += (data[i] << 8);
            }

            while ((sum >> 16) != 0)
                sum = (sum & 0xFFFF) + (sum >> 16);

            return (ushort)~sum;
        }
    }
}