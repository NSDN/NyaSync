using System;
using System.IO;
using System.Windows;
using System.Threading;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Media.Imaging;

using dotNSASM;

namespace NyaSync
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        const double Scale = 3;
        const int launchTimeout = 3000;
        const int fileBlockSize = 0;

        const string logoName = "logo.png";
        const string confName = "conf.ns";

        class ConfLoader : NSASM
        {
            public string Server
            {
                protected set;
                get;
            }
            public string Cache
            {
                protected set;
                get;
            }
            public string Target
            {
                protected set;
                get;
            }
            public string After
            {
                protected set;
                get;
            }

            public ConfLoader(string[][] code) : base(64, 64, 16, code)
            {
                Server = "";
                Cache = "";
                Target = "";
                After = "";
            }

            protected override void LoadFuncList()
            {
                base.LoadFuncList();

                funcList.Add("server", (dst, src, ext) =>
                {
                    if (dst == null) return Result.ERR;
                    if (src != null) return Result.ERR;
                    if (dst.type != RegType.STR) return Result.ERR;

                    Server = dst.data.ToString();

                    return Result.OK;
                });

                funcList.Add("cache", (dst, src, ext) =>
                {
                    if (dst == null) return Result.ERR;
                    if (src != null) return Result.ERR;
                    if (dst.type != RegType.STR) return Result.ERR;

                    Cache = dst.data.ToString();

                    return Result.OK;
                });

                funcList.Add("target", (dst, src, ext) =>
                {
                    if (dst == null) return Result.ERR;
                    if (src != null) return Result.ERR;
                    if (dst.type != RegType.STR) return Result.ERR;

                    Target = dst.data.ToString();

                    return Result.OK;
                });

                funcList.Add("after", (dst, src, ext) =>
                {
                    if (dst == null) return Result.ERR;
                    if (src != null) return Result.ERR;
                    if (dst.type != RegType.STR) return Result.ERR;

                    After = dst.data.ToString();

                    return Result.OK;
                });
            }
        }

        ConfLoader loader = null;
        Timer theTimer;

        public MainWindow()
        {
            InitializeComponent();

            theTimer = new Timer(new TimerCallback((obj) =>
             {
                 if (loader != null)
                 {
                     bool result = false;
                     if (loader.Server != "" && loader.Cache != "" && loader.Target != "")
                         result = NyaSyncWPF.DoClientStuff(loader.Server, loader.Target, loader.Cache, fileBlockSize, ProBar, ProBarSub, InfoBox);
                     if (loader.After != "" && File.Exists(loader.After) && result)
                     {
                         Process.Start(loader.After);
                         Dispatcher.Invoke(new Action(() => WindowState = WindowState.Minimized));
                         Close();
                     }
                 }
             }), this, Timeout.Infinite, Timeout.Infinite);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Util.Output = (value) => InfoBox.Text = value.ToString();
            Util.Input = () => { return "null"; };
            Util.FileInput = (path) =>
            {
                StreamReader reader = new StreamReader(new FileStream(path, FileMode.Open));
                String var = reader.ReadToEnd();
                reader.Close();
                return var;
            };
            Util.BinaryInput = (path) =>
            {
                BinaryReader reader = new BinaryReader(new FileStream(path, FileMode.Open));
                byte[] bytes = reader.ReadBytes((int)reader.BaseStream.Length);
                reader.Close();
                return bytes;
            };
            Util.BinaryOutput = (path, bytes) =>
            {
                BinaryWriter writer = new BinaryWriter(new FileStream(path, FileMode.OpenOrCreate));
                writer.Write(bytes);
                writer.Flush();
                writer.Close();
            };

            if (File.Exists(logoName))
            {
                TextLogo.Visibility = Visibility.Hidden;
                LogoBox.Source = new BitmapImage(new Uri(AppDomain.CurrentDomain.BaseDirectory + logoName, UriKind.Absolute));

                Width = SystemParameters.PrimaryScreenWidth / Scale;
                Height = Width / LogoBox.Source.Width * LogoBox.Source.Height;
                Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
                Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;
            }

            if (File.Exists(confName))
            {
                var code = Util.GetSegments(Util.Read(confName));
                loader = new ConfLoader(code);
                loader.Run();
                theTimer.Change(launchTimeout, Timeout.Infinite);
            }
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Grid_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                this.DragMove();
        }

        private void TextLogo_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                this.DragMove();
        }
    }
}
