using System;
using System.IO;
using System.Net;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;

class NyaSyncCore
{
    private const string BAK_EXT = ".bak";
    private const string MD5_EXT = ".md5";

    private const string NUL_MD5 = "null";
    private const string DIR_MD5 = "dir";

    private static string[] GetFilesR(string path)
    {
        if (Directory.Exists(path))
        {
            List<string> list = new List<string>();
            string[] subdirs = Directory.GetDirectories(path);
            if (subdirs.Length != 0)
            {
                foreach (string i in subdirs)
                    list.AddRange(GetFilesR(i));
            }
            list.AddRange(Directory.GetFiles(path));
            return list.ToArray();
        }
        return new string[] { };
    }

    private static string[] GetDirsR(string path)
    {
        if (Directory.Exists(path))
        {
            List<string> list = new List<string>();
            string[] subdirs = Directory.GetDirectories(path);
            if (subdirs.Length != 0)
            {
                foreach (string i in subdirs)
                    list.AddRange(GetDirsR(i));
            }
            list.Add(Path.GetDirectoryName(path));
            return list.ToArray();
        }
        return new string[] { };
    }

    private static string GetFileMD5(string file)
    {
        if (File.Exists(file))
        {
            FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            MD5CryptoServiceProvider md5 = new MD5CryptoServiceProvider();
            byte[] hash = md5.ComputeHash(stream);
            stream.Close();
            string s = BitConverter.ToString(hash);
            s = s.Replace("-", "");
            return s.ToLower();
        }
        return NUL_MD5;
    }

    private static bool DownloadFile(string url, string target)
    {
        try
        {
            using (var client = new WebClient())
            {
                string dir = Path.GetDirectoryName(target);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                client.DownloadFile(url, target);
            }
        }
        catch (Exception)
        {
            return false;
        }
        return true;
    }

    private static void ReadIndexFile(string file, ref Dictionary<string, string> indexes)
    {
        string[] lines = File.ReadAllText(file).Split('\n');
        foreach (string i in lines)
        {
            var part = i.Split('\t');
            if (part.Length == 2)
            {
                if (part[0] == "" || part[1] == "")
                    continue;
                indexes[part[0]] = part[1];
            }
        }
    }

    /// 服务端，扫描目录，获得文件路径+文件名和哈希的键值对。
    /// 文件变更后，扫描目录，原表所有哈希置-1，计算新的哈希和添加新的键值对。
    /// 哈希为-1代表文件删除
    /// 服务端除了有表文件，还应有表文件的哈希值文件
    /// 
    /// 表文件、哈希值文件和程序所在的位置为服务端根目录
    /// 配置文件不需要按上述要求，但是其内部指定的目录或文件应为相对路径
    /// 每行一个目录或文件，不以下划线开头（如 .minecraft/servers.dat）

    /// <summary>
    /// server side stuff, read config file and generate index file
    /// when index file is generated, a md5 file will also generate which called [index].md5
    /// </summary>
    /// <param name="config">config file name</param>
    /// <param name="index">index file name</param>
    public static void DoServerStuff(string config, string index)
    {
        index = Path.GetFileName(index);

        if (!File.Exists(config))
        {
            Console.WriteLine("[ERROR] config file not found.");
            return;
        }
        Console.WriteLine("[INFO] loading config file...");
        string cfg = File.ReadAllText(config, Encoding.UTF8);
        string[] includes = cfg.Replace("\n\r", "\n").Split('\n');

        Dictionary<string, string> indexes = new Dictionary<string, string>(); // Dict<path, md5>
        if (File.Exists(index))
        {
            ReadIndexFile(index, ref indexes);
            string[] keys = new string[indexes.Keys.Count];
            indexes.Keys.CopyTo(keys, 0);
            foreach (var i in keys)
                indexes[i] = NUL_MD5;
        }

        Console.WriteLine("[INFO] scanning files...");
        foreach (string i in includes)
        {
            if (Directory.Exists(i))
            {
                Console.WriteLine("[WORK] scanning dir: " + i);
                string[] files = GetFilesR(i);
                foreach (string f in files)
                {
                    indexes[f] = GetFileMD5(f);
                }

                string[] dirs = GetDirsR(i);
                foreach (string d in dirs)
                {
                    if (Directory.Exists(d))
                    {
                        indexes[d] = DIR_MD5;
                    } 
                }
            }
            else if (File.Exists(i))
            {
                indexes[i] = GetFileMD5(i);
            }
        }
        Console.WriteLine("[INFO] writing index file...");
        FileStream stream = File.Create(index);
        StreamWriter writer = new StreamWriter(stream);
        foreach (var i in indexes)
            writer.Write(i.Key + "\t" + i.Value + "\n");
        writer.Flush(); writer.Close();
        string indexMD5 = GetFileMD5(index);
        File.WriteAllText(index + MD5_EXT, indexMD5);
        Console.WriteLine("[DONE] index file at: " + index);
    }

    /// 客户端，执行后下载表，按照表比对客户端文件，不一致的进行覆盖或删除
    /// 客户端留存表文件，每次执行前先验证表是否有变更
    /// 
    /// server参数需指定为表文件url，因为表文件决定了服务端根目录位置
    
    /// <summary>
    /// client side stuff, download index and md5 file to cache, update target dir
    /// </summary>
    /// <param name="server">url of server side</param>
    /// <param name="target">target dir</param>
    /// <param name="cache">cache dir</param>
    public static void DoClientStuff(string server, string target, string cache, int retryCount = 3)
    {
        if (!Directory.Exists(cache))
            Directory.CreateDirectory(cache);

        if (!target.EndsWith("/") && !target.EndsWith("\\"))
            target += "/";
        if (!cache.EndsWith("/") && !cache.EndsWith("\\"))
            cache += "/";

        bool shouldSync = false;

        string indexName = Path.GetFileName(server);
        string url = server.Replace(indexName, ""); // already has slash

        string localIndex = cache + indexName;
        Console.WriteLine("[INFO] fetching index file...");
        if (File.Exists(localIndex))
        {
            if (DownloadFile(server + MD5_EXT, localIndex + MD5_EXT))
            {
                string remoteMd5 = File.ReadAllText(localIndex + MD5_EXT);
                string localMd5 = GetFileMD5(localIndex);
                if (remoteMd5 != localMd5)
                {
                    localIndex += BAK_EXT;
                    shouldSync = DownloadFile(server, localIndex);
                }
            }
        }
        else
        {
            localIndex += BAK_EXT;
            shouldSync = DownloadFile(server, localIndex);
        }

        if (shouldSync)
        {
            Console.WriteLine("[INFO] reading index file...");
            Dictionary<string, string> indexes = new Dictionary<string, string>(); // Dict<path, md5>
            ReadIndexFile(localIndex, ref indexes);

            foreach (var i in indexes)
            {
                string localPath = target + i.Key;

                if (Directory.Exists(localPath) || i.Value == DIR_MD5)
                {
                    if (i.Value != DIR_MD5)
                    {
                        Console.WriteLine("[WORK] del dir: " + i.Key);
                        Directory.Delete(localPath, true);
                    }
                    continue;
                }

                if (GetFileMD5(localPath) != i.Value)
                {
                    if (i.Value == NUL_MD5)
                    {
                        if (File.Exists(localPath))
                        {
                            Console.WriteLine("[WORK] del file: " + i.Key);
                            File.Delete(localPath);
                        }
                        continue;
                    }

                    Console.Write("[WORK] syncing: " + i.Key);
                    int retry = retryCount;
                    while (!DownloadFile(url + i.Key, localPath))
                    {
                        retry -= 1;
                        if (retry == 0)
                        {
                            Console.WriteLine(", failed");
                            break;
                        }
                    }
                    if (retry != 0)
                        Console.WriteLine(", ok");
                }
            }

            Console.WriteLine("[DONE] target at: " + target);
            if (File.Exists(localIndex.Replace(BAK_EXT, "")))
                File.Delete(localIndex.Replace(BAK_EXT, ""));
            File.Move(localIndex, localIndex.Replace(BAK_EXT, ""));
        }
        else
        {
            Console.WriteLine("[DONE] no need to sync");
        }
    }
}
