using System.Net.Sockets;
using System.Net;
using System.Text;
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
        private FileStream servedFile = null;
        ILogger logger;
        private AvailableMedia availableMedia;
        private Semaphore maxNumberAcceptedClients;
        private IPEndPoint IPE;

        private Server()
        {
            socServer = null;
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
            socServer = null;
            this.ip = ip;
            this.port = port;
            socServer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
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
            { try { servedFile.Close(); } catch {; } }
            if (socServer != null && socServer.Connected) socServer.Shutdown(SocketShutdown.Both);
        }

        private void Listen()
        {
            while (this.running)
            {
                Console.WriteLine("Waiting connection ...");
                maxNumberAcceptedClients.WaitOne();

                SocketAsyncEventArgs e = new SocketAsyncEventArgs();
                e.Completed += AcceptCallback;

                bool pending = socServer.AcceptAsync(e);
                if (!pending)
                {
                    AcceptCallback(this, e);
                }
            }
        }       

        private void AcceptCallback(object sender, SocketAsyncEventArgs e)
        {
            Thread thread = new Thread(ProcessAccept);
            thread.Start(e);
        }

        private void ProcessAccept(object eObj)
        {
            SocketAsyncEventArgs e = (SocketAsyncEventArgs)eObj;
            try
            {
                Interlocked.Increment(ref connections);
                Console.WriteLine("Client connection accepted. There are {0} clients connected to the server", connections);
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
            try
            {
                socket.Shutdown(SocketShutdown.Send);
            }
            catch (Exception) { }
            socket.Close();
            Interlocked.Decrement(ref connections);
            maxNumberAcceptedClients.Release();
            Console.WriteLine("A client has been disconnected from the server. There are {0} clients connected to the server", connections);
        }

        private void BusinessLogic(string request, Socket handler)
        {
            string[] requestLines = GetRequestLines(request);
            List<KeyValuePair<string, string>> headers = GetHeaders(requestLines);
            KeyValuePair<string, string> methodAndPath = GetMethodAndPath(requestLines);

            String methPath = String.Format("Method: {0} | path: {1}", methodAndPath.Key, methodAndPath.Value);

            if (methodAndPath.Value.ToLower().Contains("favicon.ico"))
            {
                CloseClientSocket(handler);
                return;
            }
            else
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(methPath + Environment.NewLine);
                foreach (KeyValuePair<string, string> pair in headers)
                {
                    sb.Append(String.Format("{0}:{1}" + Environment.NewLine, pair.Key, pair.Value));
                }
                logger.LogDebug(sb.ToString());
                if (methodAndPath.Key.Equals("HEAD"))
                {
                    HandleHead(handler, headers, methodAndPath.Value);
                }
                else if (methodAndPath.Key.Equals("GET"))
                {
                    HandleGet(handler, headers, methodAndPath.Value);
                }
                else
                {
                    CloseClientSocket(handler);
                }
            }
        }

        private string[] GetRequestLines(string request)
        {
            return request.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        }

        private KeyValuePair<string, string> GetMethodAndPath(string[] requestLines)
        {
            if (requestLines.Length > 0)
            {
                string[] firstLineParts = requestLines[0].Split(' ');
                if (firstLineParts.Length >= 2)
                {
                    return new KeyValuePair<string, string>(firstLineParts[0], firstLineParts[1]);
                }
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
                    if (string.IsNullOrWhiteSpace(requestLines[i]))
                        break;
                    string[] parts = requestLines[i].Split(new[] { ':' }, 2);
                    if (parts.Length == 2)
                    {
                        headers.Add(new KeyValuePair<string, string>(parts[0].Trim(), parts[1].Trim()));
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Exception getting headers");
            }
            return headers;
        }

        private void HandleHead(Socket handler, List<KeyValuePair<string, string>> headers, string path)
        {
            int index = GetIndexFromPath(path);
            String requestFile = availableMedia.getAbsolutePath(index);

            logger.LogDebug("HEAD requested for file: {0}", requestFile);

            FileInfo fileInfo = new FileInfo(requestFile);
            if (fileInfo.Exists)
            {
                string Reply = "HTTP/1.1 200 OK" + Environment.NewLine +
                    "Content-Length: " + fileInfo.Length + Environment.NewLine +
                    "Content-Type: application/octet-stream" + Environment.NewLine +
                    "Connection: close" + Environment.NewLine + Environment.NewLine;
                handler.Send(Encoding.UTF8.GetBytes(Reply), SocketFlags.None);
            }
            CloseClientSocket(handler);
        }

        private void ReturnList(Socket handler)
        {
            string template = File.ReadAllText("template.txt");
            string media = "";
            string[] files = availableMedia.getAvailableFiles().ToArray();

            for (int i = 0; i < files.Length; i++)
            {
                string fileName = Path.GetFileName(files[i]);
                media += $"<a href=\"/{i}\">{fileName}</a><br/>";
            }

            template = template.Replace("{{MediaList}}", media);

            string ContentType = "text/html";
            string Reply = "HTTP/1.1 200 OK" + Environment.NewLine + "Server: VLC" + Environment.NewLine + "Content-Type: " + ContentType + Environment.NewLine;
            Reply += "Last-Modified: " + GMTTime(DateTime.Now) + Environment.NewLine;
            Reply += "Date: " + GMTTime(DateTime.Now) + Environment.NewLine;
            Reply += "Accept-Ranges: bytes" + Environment.NewLine;
            UTF8Encoding encoding = new UTF8Encoding();
            byte[] bytes = encoding.GetBytes(template);
            long length = bytes.Length;
            Reply += "Content-Length: " + length + Environment.NewLine;
            Reply += "Connection: close" + Environment.NewLine + Environment.NewLine;
            handler.Send(UTF8Encoding.UTF8.GetBytes(Reply), SocketFlags.None);
            handler.Send(bytes);
            CloseClientSocket(handler);
        }

        private string GMTTime(DateTime dt)
        {
            return dt.ToUniversalTime().ToString("r");
        }

        private void HandleGet(Socket handler, List<KeyValuePair<string, string>> headers, string path)
        {
            if (path == "/")
            {
                ReturnList(handler);
            }
            else
            {
                int index = GetIndexFromPath(path);
                string filePath = availableMedia.getAbsolutePath(index);

                logger.LogDebug("GET requested for file: {0}", filePath);

                if (File.Exists(filePath))
                {
                    byte[] fileBytes = File.ReadAllBytes(filePath);
                    string header = "HTTP/1.1 200 OK\r\n" +
                        "Content-Length: " + fileBytes.Length + "\r\n" +
                        "Content-Type: application/octet-stream\r\n" +
                        "Connection: close\r\n\r\n";
                    handler.Send(Encoding.UTF8.GetBytes(header));
                    handler.Send(fileBytes);
                }
                else
                {
                    string notFound = "HTTP/1.1 404 Not Found\r\nConnection: close\r\n\r\n";
                    handler.Send(Encoding.UTF8.GetBytes(notFound));
                }

                CloseClientSocket(handler);
            }
        }

        private int GetIndexFromPath(string path)
        {
            if (int.TryParse(path.TrimStart('/'), out int index))
            {
                return index;
            }
            return -1; // Invalid index
        }
    }
}

