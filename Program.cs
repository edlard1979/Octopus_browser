using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OctopusBrowser
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            // بررسی و نصب WebView2 در صورت نیاز
            try
            {
                var webView2Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                    "Microsoft", "EdgeUpdate", "ClientState", "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}");
                
                if (!Directory.Exists(webView2Path))
                {
                    MessageBox.Show("در حال نصب WebView2 Runtime... لطفا منتظر بمانید.", "Octopus Browser", 
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    
                    // دانلود و نصب WebView2 Runtime
                    var installerPath = Path.Combine(Path.GetTempPath(), "MicrosoftEdgeWebView2RuntimeInstaller.exe");
                    
                    if (!File.Exists(installerPath))
                    {
                        var client = new System.Net.WebClient();
                        client.DownloadFile(
                            "https://go.microsoft.com/fwlink/p/?LinkId=2124703", 
                            installerPath);
                    }
                    
                    var process = System.Diagnostics.Process.Start(installerPath, "/silent /install");
                    process.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"هشدار: WebView2 Runtime نصب نشده است. برخی ویژگیها ممکن است کار نکنند.\n{ex.Message}", 
                    "Octopus Browser", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            
            Application.Run(new MainForm());
        }
    }
}