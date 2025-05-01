using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace MediaServer
{
    class Server
    {
        private const int MAXCONNECTIONS = 10;
        private int connections;
        private Socket socServer;
        private string ip;
        private int port;
        private bool running;
        private FileStream? servedFile = null;
        ILogger logger;
        private AvailableMedia availableMedia;
        private Semaphore maxNumberAcceptedClients;
        private IPEndPoint IPE;

        private Server()
        {
            socServer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            ip = string.Empty;
            port = 0;
            logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<Server>();
            availableMedia = new AvailableMedia(string.Empty);
            IPE = new IPEndPoint(IPAddress.Any, 0);
            connections = 0;
            maxNumberAcceptedClients = new Semaphore(MAXCONNECTIONS, MAXCONNECTIONS);
        }

        public Server(string ip, int port, string mediaDir) : this()
        {
            availableMedia = new AvailableMedia(mediaDir);

            using ILoggerFactory factory = LoggerFactory.Create(builder => builder
                .AddFilter("MediaServer.Server", LogLevel.Debug)
                .AddConsole());

            logger = factory.CreateLogger<Server>();
            socServer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            this.ip = ip;
            this.port = port;
            IPE = new IPEndPoint(IPAddress.Parse(this.ip), this.port);
        }

        public void Start()
        {
            socServer.Bind(IPE);
            socServer.Listen(0);
            running = true;
            Thread thread = new Thread(Listen);
            thread.Start();
        }

        public void Stop()
        {
            running = false;
            Thread.Sleep(100);
            if (this.servedFile != null)
            {
                try { servedFile.Close(); } catch {; }
            }
            if (socServer != null && socServer.Connected) socServer.Shutdown(SocketShutdown.Both);
        }

        private void Listen()
        {
            SocketAsyncEventArgs e = new SocketAsyncEventArgs();
            e.Completed += AcceptCallback;

            while (this.running)
            {
                Console.WriteLine("Waiting connection ...");
                maxNumberAcceptedClients.WaitOne();

                e.AcceptSocket = null;
                bool pending = socServer.AcceptAsync(e);

                if (!pending)
                {
                    AcceptCallback(this, e);
                }
            }
        }

        private void AcceptCallback(object? sender, SocketAsyncEventArgs e)
        {
            Thread thread = new Thread(ProcessAccept);
            thread.Start(e);
        }

        private void ProcessAccept(object? eObj)
        {
            if (eObj is not SocketAsyncEventArgs e)
            {
                logger.LogCritical("Invalid argument passed to ProcessAccept.");
                return;
            }
            try
            {
                Interlocked.Increment(ref connections);
                Console.WriteLine("Client connection accepted. There are {0} clients connected to the server", connections);
                if (e.AcceptSocket == null)
                {
                    logger.LogCritical("AcceptSocket is null.");
                    return;
                }
                Socket sock = e.AcceptSocket;
                byte[] buffer = new byte[3000];
                int receivedSize = 0;
                var sb = new StringBuilder();
                MemoryStream ms;

                while (sock.Available > 0)
                {
                    receivedSize = sock.Receive(buffer, SocketFlags.None);
                    ms = new MemoryStream();
                    ms.Write(buffer, 0, receivedSize);
                    string toAdd = UTF8Encoding.UTF8.GetString(ms.ToArray());
                    sb.Append(toAdd);
                    logger.LogDebug("Received {0} bytes", receivedSize);
                }

                string requestData = sb.ToString();
                BusinessLogic(requestData, sock);
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Error processing request");
            }
            finally
            {
                Listen();
            }
        }

        private void CloseClientSocket(Socket socket)
        {
            try { socket.Shutdown(SocketShutdown.Send); } catch (Exception) { }
            socket.Close();
            Interlocked.Decrement(ref connections);
            maxNumberAcceptedClients.Release();
            Console.WriteLine("A client has been disconnected from the server. There are {0} clients connected to the server", connections);
        }

        private void HandleHead(Socket handler, List<KeyValuePair<string, string>> headers, string path)
        {
            try
            {
                string response = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n";
                byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                handler.Send(responseBytes);
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Error handling HEAD request");
            }
            finally
            {
                CloseClientSocket(handler);
            }
        }

        private void HandleGet(Socket handler, List<KeyValuePair<string, string>> headers, string path)
        {
            try
            {
                string response = "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\n\r\nRequested path: " + path;
                byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                handler.Send(responseBytes);
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Error handling GET request");
            }
            finally
            {
                CloseClientSocket(handler);
            }
        }

        private void BusinessLogic(string request, Socket handler)
        {
            string[] requestLines = GetRequestLines(request);
            List<KeyValuePair<string, string>> headers = GetHeaders(requestLines);
            KeyValuePair<string, string> methodAndPath = GetMethodAndPath(requestLines);

            string methPath = string.Format("Method: {0} | path: {1}", methodAndPath.Key, methodAndPath.Value);

            if (methodAndPath.Value.Contains("favicon.ico"))
            {
                CloseClientSocket(handler);
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append(methPath + Environment.NewLine);
            foreach (KeyValuePair<string, string> pair in headers)
            {
                sb.Append(string.Format("{0}:{1}" + Environment.NewLine, pair.Key, pair.Value));
            }
            logger.LogDebug(sb.ToString());

            if (methodAndPath.Key.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
            {
                HandleHead(handler, headers, methodAndPath.Value);
            }
            else if (methodAndPath.Key.Equals("GET", StringComparison.OrdinalIgnoreCase))
            {
                HandleGet(handler, headers, methodAndPath.Value);
            }
            else
            {
                CloseClientSocket(handler);
            }
        }

        private string[] GetRequestLines(string request)
        {
            return request.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        }

        private KeyValuePair<string, string> GetMethodAndPath(string[] requestLines)
        {
            if (requestLines.Length == 0) return new KeyValuePair<string, string>("", "");
            string[] parts = requestLines[0].Split(' ');
            if (parts.Length >= 2)
            {
                return new KeyValuePair<string, string>(parts[0].Trim(), parts[1].Trim());
            }
            return new KeyValuePair<string, string>("", "");
        }

        private List<KeyValuePair<string, string>> GetHeaders(string[] requestLines)
        {
            List<KeyValuePair<string, string>> headers = new List<KeyValuePair<string, string>>();
            try
            {
                for (int i = 1; i < requestLines.Length; i++)
                {
                    string line = requestLines[i];
                    int separator = line.IndexOf(':');
                    if (separator > 0)
                    {
                        string key = line.Substring(0, separator).Trim().ToLower() + ":";
                        string value = line.Substring(separator + 1).Trim();
                        headers.Add(new KeyValuePair<string, string>(key, value));
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Exception getting headers");
            }
            return headers;
        }

    }
}
