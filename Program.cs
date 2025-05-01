using System.Text.Json.Nodes;

namespace MediaServer
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("eL33T Media Server");
            string fileName = "properties.json";
            string rawProperties = File.ReadAllText(fileName);

            JsonNode properties = JsonNode.Parse(rawProperties)!;
            string ip = properties["ip"]?.GetValue<string>() ?? throw new InvalidOperationException("The 'ip' property is missing or null.");
            int port = properties["port"]?.GetValue<int>() ?? throw new InvalidOperationException("The 'port' property is missing or null.");
            string mediaDir = properties["mediaDir"]?.GetValue<string>() ?? throw new InvalidOperationException("The 'mediaDir' property is missing or null.");

            Server server = new Server(ip, port, mediaDir);
            Thread thread = new Thread(server.Start);
            Console.WriteLine("Access via link: http://{0}:{1}", ip, port);
            thread.Start();
            Console.WriteLine("Press enter to stop");
            Console.ReadLine();
            server.Stop();
        }
    }
}
