using System;
using System.Numerics;
using System.Threading;

namespace Underthrone.Server
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            int port = 7777;
            for (int argIndex = 0; argIndex < args.Length - 1; argIndex++)
            {
                if (args[argIndex] == "--port" && int.TryParse(args[argIndex + 1], out int parsed))
                {
                    port = parsed;
                }
            }

            var world = new World();
            world.SpawnGhoul(new Vector3(0f, 0f, 8f));
            world.SpawnGhoul(new Vector3(-6f, 0f, 12f));
            world.SpawnGhoul(new Vector3(7f, 0f, 14f));

            using var server = new GameServer(world, message => Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] {message}"));
            if (!server.Start(port))
            {
                Console.Error.WriteLine($"Could not bind UDP port {port}");
                return 1;
            }

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };
            server.Run(cts.Token);
            Console.WriteLine("Server stopped.");
            return 0;
        }
    }
}
