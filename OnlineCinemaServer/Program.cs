using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OnlineCinemaServer
{
    class Program
    {
        static Random random = new Random();
        public class Client
        {
            public string chatId = "";
            public string name = "";
            public long id = 0;
            public IPEndPoint ip = new IPEndPoint(IPAddress.Any, 0);
        }

        public class Chat
        {
            public string id = "";
            public List<long> clients = new List<long>();
        }

        public static Dictionary<long, Client> clients = new Dictionary<long, Client>();
        public static Dictionary<string, Chat> chats = new Dictionary<string, Chat>();
        static UdpClient udp = new UdpClient(50567);
        static TcpListener tcpListener = new TcpListener(IPAddress.Any, 50567);

        static void Main()
        {
            Chat chat = new Chat();
            chat.id = "Kotya";
            chats.Add(chat.id, chat);

            tcpListener.Start();

            new Thread(UdpReceiveLoop).Start();
            new Thread(TcpReceiveLoop).Start();

            Console.WriteLine("OnlineCinema server started");
        }
        static void UdpReceiveLoop()
        {
            while (true)
            {
                IPEndPoint remoteIp = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = udp.Receive(ref remoteIp);
                byte[] response = new byte[100];
                switch (data[0])
                {
                    //register ip
                    //1 {id 8 bytes}
                    //1 {port 2 bytes} {ip}
                    case 1: 
                        long userId = BitConverter.ToInt64(data, 1);
                        byte[] ipBytes = remoteIp.Address.GetAddressBytes();
                        byte[] portBytes = BitConverter.GetBytes((short)remoteIp.Port);
                        response[0] = 1;
                        portBytes.CopyTo(response, 1);
                        ipBytes.CopyTo(response, 1 + 2);
                        int responseLength = 1 + portBytes.Length + ipBytes.Length;
                        clients[userId].ip = remoteIp;
                        udp.Send(response, responseLength, remoteIp);
                        Console.WriteLine($"{userId} has ip {remoteIp}");
                        break;
                }
            }
        }

        static async Task processTcp(TcpClient tcp)
        {
            NetworkStream stream = tcp.GetStream();

            int read = 0;
            int length = 1;
            byte[] buffer = new byte[1000];
            while (read < length)
            {
                read += await stream.ReadAsync(buffer, read, length - read);
                length = buffer[0];
            }

            byte command = buffer[1];
            switch (command)
            {
                // create id
                // len 1 {name UTF8}
                // 'OK' {id 8 bytes}
                case 1: 
                    {
                        long id = random.NextInt64();
                        string name = Encoding.UTF8.GetString(buffer, 2, length - 2);
                        Client client = new Client();
                        client.name = name;
                        client.id = id;
                        clients.Add(id, client);
                        byte[] response = new byte[10];
                        response[0] = (byte)'O';
                        response[1] = (byte)'K';
                        BitConverter.GetBytes(id).CopyTo(response, 2);
                        await stream.WriteAsync(response);
                        Console.WriteLine($"client {name} got id {id}");
                    }
                    break;
                // join chat
                // len 2 {id 8 bytes} {chat id}
                // 'OK' clientCount {clientIds n*8 bytes}
                case 2: 
                    {
                        long id = BitConverter.ToInt64(buffer, 2);
                        string chatId = Encoding.UTF8.GetString(buffer, 10, length - 10);
                        clients[id].chatId = chatId;
                        if (!chats[chatId].clients.Contains(id))
                        {
                            chats[chatId].clients.Add(id);
                        }
                        int clientCount = chats[chatId].clients.Count;
                        byte[] clientIds = new byte[clientCount * 8];
                        for (int i = 0; i < clientCount; i++)
                        {
                            BitConverter.GetBytes(chats[chatId].clients[i]).CopyTo(clientIds, i * 8);
                        }
                        byte[] response = new byte[3 + clientIds.Length];
                        response[0] = (byte)'O';
                        response[1] = (byte)'K';
                        response[2] = (byte)clientCount;
                        clientIds.CopyTo(response, 3);
                        await stream.WriteAsync(response);
                        Console.WriteLine($"{id} joined chat {chatId}");
                    }
                    break;
                // leave chat
                // len 3 {id 8 bytes}
                // 'OK'
                case 3:
                    {
                        long id = BitConverter.ToInt64(buffer, 2);
                        chats[clients[id].chatId].clients.Remove(id);
                        clients[id].chatId = "";
                        await stream.WriteAsync(Encoding.UTF8.GetBytes("OK"));
                        Console.WriteLine($"{id} left chat");
                    }
                    break;
                // get name
                // len 4 {id 8 bytes}
                // 'OK' {name}
                case 4:
                    {
                        long id = BitConverter.ToInt64(buffer, 2);
                        byte[] nameBytes = Encoding.UTF8.GetBytes(clients[id].name);
                        byte[] response = new byte[2 + nameBytes.Length];
                        response[0] = (byte)'O';
                        response[1] = (byte)'K';
                        nameBytes.CopyTo(response, 2);
                        await stream.WriteAsync(response);
                        Console.WriteLine($"requested name for {id}: {clients[id].name}");
                    }
                    break;
                // get ip
                // len 5 {id 8 bytes}
                // 'OK' {port 2 bytes} {ip}
                case 5:
                    {
                        long id = BitConverter.ToInt64(buffer, 2);
                        byte[] portBytes = BitConverter.GetBytes((ushort)clients[id].ip.Port);
                        byte[] ipBytes = clients[id].ip.Address.GetAddressBytes();
                        byte[] response = new byte[2 + 2+ipBytes.Length];
                        response[0] = (byte)'O';
                        response[1] = (byte)'K';
                        portBytes.CopyTo(response, 2);
                        ipBytes.CopyTo(response, 4);
                        await stream.WriteAsync(response);
                        Console.WriteLine($"requested ip for {id}: {clients[id].ip}");
                    }
                    break;
            }

            tcp.Close();
        }
        static void TcpReceiveLoop()
        {
            while (true)
            {
                TcpClient tcp = tcpListener.AcceptTcpClient();
                _ = processTcp(tcp);
            }
        }
    }
}