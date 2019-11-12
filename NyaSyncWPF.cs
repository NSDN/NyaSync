﻿using System;
using System.IO;
using System.Net;
using System.Threading;
using System.ComponentModel;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace NyaSync
{
    class NyaSyncWPF
    {
        private const string BAK_EXT = ".bak";
        private const string MD5_EXT = ".md5";

        private const string NUL_MD5 = "null";
        private const string DIR_MD5 = "dir";

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

        static object _lock = new object();

        private static bool DownloadFile(string url, string target, int blockSizeKB, ProgressBar bar)
        {
            if (blockSizeKB == 0)
            {
                try
                {
                    using (WebClient client = new WebClient())
                    {
                        client.DownloadProgressChanged += new DownloadProgressChangedEventHandler((obj, e) => {
                            bar.Dispatcher.Invoke(new ThreadStart(() => bar.Value = e.ProgressPercentage));
                        });
                        string dir = Path.GetDirectoryName(target);
                        if (!Directory.Exists(dir))
                            Directory.CreateDirectory(dir);
                        client.DownloadFileAsync(new Uri(url), target);
                        bool ok = false;
                        client.DownloadFileCompleted += new AsyncCompletedEventHandler((obj, e) =>
                        {
                            Monitor.Enter(_lock);
                            ok = true;
                            Monitor.Exit(_lock);
                        });
                        while (true)
                        {
                            Monitor.Enter(_lock);
                            if (ok)
                            {
                                Monitor.Exit(_lock);
                                break;
                            }
                            Monitor.Exit(_lock);
                            Thread.Sleep(1);
                        }
                    }
                }
                catch (Exception)
                {
                    return false;
                }
            }
            else
            {
                try
                {
                    HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
                    Stream stream = request.GetResponse().GetResponseStream();
                    string dir = Path.GetDirectoryName(target);
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    FileStream fs = new FileStream(target, FileMode.Create, FileAccess.Write);
                    byte[] bytes = new byte[1024 * blockSizeKB];
                    int readCount = 0;
                    while (true)
                    {
                        readCount = stream.Read(bytes, 0, bytes.Length);
                        if (readCount <= 0)
                            break;
                        fs.Write(bytes, 0, readCount);
                        fs.Flush();
                    }
                    fs.Close();
                    stream.Close();
                }
                catch (Exception)
                {
                    return false;
                }
            }
            
            return true;
        }

        private static void Print(TextBlock text, String str)
        {
            text.Dispatcher.Invoke(new ThreadStart(() => text.Text = str));
        }

        public static bool DoClientStuff(string server, string target, string cache, int blockSizeKB, ProgressBar bar, ProgressBar sub, TextBlock text, int retryCount = 3)
        {
            bar.Dispatcher.Invoke(new ThreadStart(() => bar.Value = 0));
            sub.Dispatcher.Invoke(new ThreadStart(() => sub.Value = 0));
            text.Dispatcher.Invoke(new ThreadStart(() => text.Text = ""));

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
            Print(text, "[INFO] fetching index file...");
            if (File.Exists(localIndex))
            {
                if (DownloadFile(server + MD5_EXT, localIndex + MD5_EXT, blockSizeKB, sub))
                {
                    string remoteMd5 = File.ReadAllText(localIndex + MD5_EXT);
                    string localMd5 = GetFileMD5(localIndex);
                    if (remoteMd5 != localMd5)
                    {
                        localIndex += BAK_EXT;
                        shouldSync = DownloadFile(server, localIndex, blockSizeKB, sub);
                    }
                }
            }
            else
            {
                localIndex += BAK_EXT;
                shouldSync = DownloadFile(server, localIndex, blockSizeKB, sub);
            }

            if (shouldSync)
            {
                Print(text, "[INFO] reading index file...");
                Dictionary<string, string> indexes = new Dictionary<string, string>(); // Dict<path, md5>
                ReadIndexFile(localIndex, ref indexes);

                int totalWorks = 0;
                foreach (var i in indexes)
                {
                    string localPath = target + i.Key;

                    if (Directory.Exists(localPath) || i.Value == DIR_MD5)
                    {
                        if (i.Value != DIR_MD5)
                        {
                            totalWorks += 1;
                        }
                        continue;
                    }

                    if (GetFileMD5(localPath) != i.Value)
                    {
                        if (i.Value == NUL_MD5)
                        {
                            if (File.Exists(localPath))
                            {
                                totalWorks += 1;
                            }
                            continue;
                        }

                        totalWorks += 1;
                    }
                }

                bar.Dispatcher.Invoke(new ThreadStart(() => {
                    bar.Maximum = totalWorks;
                    bar.Value = 0;
                }));

                int workCount = 0;
                foreach (var i in indexes)
                {
                    string localPath = target + i.Key;

                    if (Directory.Exists(localPath) || i.Value == DIR_MD5)
                    {
                        if (i.Value != DIR_MD5)
                        {
                            Print(text, "[WORK] del dir: " + i.Key);
                            Directory.Delete(localPath, true);
                            bar.Dispatcher.Invoke(new ThreadStart(() => bar.Value += 1));
                            workCount += 1;
                        }
                        continue;
                    }

                    if (GetFileMD5(localPath) != i.Value)
                    {
                        if (i.Value == NUL_MD5)
                        {
                            if (File.Exists(localPath))
                            {
                                Print(text, "[WORK] del file: " + i.Key);
                                File.Delete(localPath);
                                bar.Dispatcher.Invoke(new ThreadStart(() => bar.Value += 1));
                                workCount += 1;
                            }
                            continue;
                        }

                        string str = "[WORK] syncing: " + i.Key;
                        Print(text, str);
                        int retry = retryCount;
                        while (!DownloadFile(url + i.Key, localPath, blockSizeKB, sub))
                        {
                            retry -= 1;
                            if (retry == 0)
                            {
                                Print(text, str + ", failed");
                                break;
                            }
                        }
                        if (retry != 0)
                        {
                            Print(text, str + ", ok");
                            bar.Dispatcher.Invoke(new ThreadStart(() => bar.Value += 1));
                            workCount += 1;
                        }
                    }
                }

                Print(text, "[DONE] target at: " + target);
                if (File.Exists(localIndex.Replace(BAK_EXT, "")))
                    File.Delete(localIndex.Replace(BAK_EXT, ""));
                File.Move(localIndex, localIndex.Replace(BAK_EXT, ""));

                return workCount == totalWorks;
            }
            else
            {
                Print(text, "[DONE] no need to sync");

                return true;
            }
        }
    }
}
