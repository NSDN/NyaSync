using System;
using System.IO;

namespace NyaSyncCLI
{
    class Program
    {
        const string SERVER_ARG = "--server";
        static void Main(string[] args)
        {
            if (args.Length != 3)
            {
                Console.WriteLine("NyaSync CLI | usage:");
                Console.WriteLine("use as server: " + Path.GetFileName(Environment.CommandLine) + " " + SERVER_ARG + " [config file] [index file]");
                Console.WriteLine("use as client: " + Path.GetFileName(Environment.CommandLine) + " [server url] [target dir] [cache dir]");
                Console.WriteLine();
                return;
            }
            else
            {
                if (args[0] == SERVER_ARG)
                {
                    Console.WriteLine("NyaSync CLI | Server Side");
                    string config = args[1];
                    string index = args[2];
                    NyaSyncCore.DoServerStuff(config, index);
                }
                else
                {
                    Console.WriteLine("NyaSync CLI | Client Side");
                    string url = args[0];
                    string target = args[1];
                    string cache = args[2];
                    NyaSyncCore.DoClientStuff(url, target, cache, 512, 3);
                }
            }
        }
    }
}
