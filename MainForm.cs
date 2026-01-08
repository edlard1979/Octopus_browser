using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace OctopusBrowser
{
    public partial class MainForm : Form
    {
        private TabControl tabControl;
        private ToolStrip toolStrip;
        private StatusStrip statusStrip;
        private Timer memoryTimer;
        private List<string> blockedSites;
        private List<string> proxyList;
        private Random random = new Random();
        
        // ساختار داده برای مدیریت حافظه
        private Dictionary<string, DateTime> lastAccessTime = new Dictionary<string, DateTime>();
        private const int MAX_TABS = 100;
        private const int MEMORY_CLEANUP_INTERVAL = 30000; // 30 ثانیه
        
        public MainForm()
        {
            InitializeComponent();
            InitializeBrowser();
            LoadBlockedSites();
            LoadProxyList();
            StartMemoryOptimization();
        }
        
        private void InitializeComponent()
        {
            this.Text = "Octopus Browser - Ultra Light";
            this.Size = new Size(1200, 800);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Icon = SystemIcons.Application;
            
            // تنظیمات برای کاهش مصرف حافظه
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | 
                         ControlStyles.AllPaintingInWmPaint | 
                         ControlStyles.UserPaint, true);
            
            // ایجاد ToolStrip
            toolStrip = new ToolStrip
            {
                RenderMode = ToolStripRenderMode.System,
                GripStyle = ToolStripGripStyle.Hidden
            };
            
            var btnBack = new ToolStripButton("←") { ToolTipText = "بازگشت" };
            var btnForward = new ToolStripButton("→") { ToolTipText = "جلو" };
            var btnRefresh = new ToolStripButton("↻") { ToolTipText = "تازه‌سازی" };
            var btnNewTab = new ToolStripButton("+") { ToolTipText = "تب جدید" };
            var btnCloseTab = new ToolStripButton("×") { ToolTipText = "بستن تب" };
            var txtUrl = new ToolStripTextBox
            {
                Width = 400,
                Font = new Font("Tahoma", 9)
            };
            
            btnBack.Click += (s, e) => GetCurrentWebView()?.GoBack();
            btnForward.Click += (s, e) => GetCurrentWebView()?.GoForward();
            btnRefresh.Click += (s, e) => GetCurrentWebView()?.Reload();
            btnNewTab.Click += (s, e) => AddNewTab();
            btnCloseTab.Click += (s, e) => CloseCurrentTab();
            txtUrl.KeyDown += (s, e) => 
            {
                if (e.KeyCode == Keys.Enter)
                {
                    NavigateToUrl(txtUrl.Text);
                }
            };
            
            toolStrip.Items.AddRange(new ToolStripItem[] 
            {
                btnBack, btnForward, btnRefresh, new ToolStripSeparator(),
                txtUrl, new ToolStripSeparator(),
                btnNewTab, btnCloseTab
            });
            
            // ایجاد TabControl
            tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                HotTrack = true,
                Font = new Font("Tahoma", 9)
            };
            
            // ایجاد StatusStrip
            statusStrip = new StatusStrip();
            var lblStatus = new ToolStripStatusLabel("آماده");
            var lblMemory = new ToolStripStatusLabel();
            var progressBar = new ToolStripProgressBar { Visible = false };
            
            statusStrip.Items.AddRange(new ToolStripItem[] { lblStatus, progressBar, lblMemory });
            
            // افزودن کنترل‌ها به فرم
            this.Controls.Add(tabControl);
            this.Controls.Add(toolStrip);
            this.Controls.Add(statusStrip);
            
            // تنظیم Dock
            toolStrip.Dock = DockStyle.Top;
            statusStrip.Dock = DockStyle.Bottom;
            tabControl.Dock = DockStyle.Fill;
            
            // رویداد فرم
            this.FormClosing += MainForm_FormClosing;
        }
        
        private void InitializeBrowser()
        {
            AddNewTab("https://www.google.com");
        }
        
        private void AddNewTab(string url = "about:blank")
        {
            if (tabControl.TabCount >= MAX_TABS)
            {
                MessageBox.Show("حداکثر تعداد تب‌ها (100) reached.", "Octopus Browser", 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            var tabPage = new TabPage("در حال بارگذاری...")
            {
                Padding = new Padding(0)
            };
            
            var webView = new WebView2
            {
                Dock = DockStyle.Fill,
                Source = new Uri(url)
            };
            
            // تنظیمات WebView2 برای بهینه‌سازی حافظه
            webView.CreationProperties = new CoreWebView2CreationProperties
            {
                BrowserExecutableFolder = null,
                UserDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                    "OctopusBrowser", "UserData"),
                AdditionalBrowserArguments = "--disable-features=RendererCodeIntegrity --disable-web-security --disable-features=VizDisplayCompositor"
            };
            
            tabPage.Controls.Add(webView);
            tabControl.TabPages.Add(tabPage);
            tabControl.SelectedTab = tabPage;
            
            // رویدادهای WebView2
            webView.NavigationStarting += WebView_NavigationStarting;
            webView.NavigationCompleted += WebView_NavigationCompleted;
            webView.CoreWebView2InitializationCompleted += WebView_CoreWebView2InitializationCompleted;
            
            // به‌روزرسانی زمان دسترسی
            lastAccessTime[tabPage.Text] = DateTime.Now;
        }
        
        private void CloseCurrentTab()
        {
            if (tabControl.TabCount > 1)
            {
                var currentTab = tabControl.SelectedTab;
                tabControl.TabPages.Remove(currentTab);
                lastAccessTime.Remove(currentTab.Text);
                currentTab.Dispose();
            }
        }
        
        private void WebView_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            var webView = sender as WebView2;
            var tabPage = webView.Parent as TabPage;
            
            // بررسی فیلترینگ
            if (IsSiteBlocked(e.Uri))
            {
                e.Cancel = true;
                var proxyUrl = GetProxyUrl(e.Uri);
                if (!string.IsNullOrEmpty(proxyUrl))
                {
                    webView.CoreWebView2.Navigate(proxyUrl);
                }
                else
                {
                    MessageBox.Show($"سایت {new Uri(e.Uri).Host} فیلتر شده است. در حال تلاش برای دور زدن فیلتر...", 
                        "Octopus Browser", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }
            
            tabPage.Text = "در حال بارگذاری...";
            UpdateStatus("در حال بارگذاری: " + e.Uri);
        }
        
        private void WebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            var webView = sender as WebView2;
            var tabPage = webView.Parent as TabPage;
            
            if (e.IsSuccess)
            {
                tabPage.Text = webView.CoreWebView2.DocumentTitle;
                if (string.IsNullOrEmpty(tabPage.Text))
                    tabPage.Text = new Uri(webView.Source.ToString()).Host;
                
                UpdateUrlBar(webView.Source.ToString());
                UpdateStatus("بارگذاری کامل شد");
                
                // به‌روزرسانی زمان دسترسی
                lastAccessTime[tabPage.Text] = DateTime.Now;
            }
            else
            {
                tabPage.Text = "خطا در بارگذاری";
                UpdateStatus($"خطا: {e.WebErrorStatus}");
            }
        }
        
        private void WebView_CoreWebView2InitializationCompleted(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                var webView = sender as WebView2;
                
                // تنظیمات امنیتی و حریم خصوصی
                webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                webView.CoreWebView2.Settings.IsBuiltInErrorPageEnabled = false;
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                webView.CoreWebView2.Settings.IsWebMessageEnabled = false;
                webView.CoreWebView2.Settings.IsScriptEnabled = true;
                
                // تنظیمات برای باز کردن لینک‌ها در تب جدید
                webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
                
                // تنظیم User-Agent برای عبور از فیلترها
                webView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Octopus/1.0";
            }
        }
        
        private void CoreWebView2_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            AddNewTab(e.Uri);
        }
        
        private bool IsSiteBlocked(string url)
        {
            try
            {
                var uri = new Uri(url);
                var host = uri.Host.ToLower();
                
                // بررسی لیست سایت‌های فیلتر شده
                return blockedSites.Any(site => host.Contains(site.ToLower()));
            }
            catch
            {
                return false;
            }
        }
        
        private string GetProxyUrl(string originalUrl)
        {
            try
            {
                // استفاده از پراکسی‌های رایگان
                if (proxyList.Count > 0)
                {
                    var proxy = proxyList[random.Next(proxyList.Count)];
                    return $"{proxy}/{originalUrl}";
                }
                
                // استفاده از Wayback Machine به عنوان fallback
                return $"https://web.archive.org/web/{DateTime.Now.Year}/{originalUrl}";
            }
            catch
            {
                return null;
            }
        }
        
        private void LoadBlockedSites()
        {
            blockedSites = new List<string>
            {
                "youtube.com", "facebook.com", "twitter.com", "telegram.org", 
                "instagram.com", "netflix.com", "spotify.com"
            };
            
            // بارگذاری لیست فیلتر شده از فایل
            try
            {
                var blockedFile = Path.Combine(Application.StartupPath, "blocked_sites.txt");
                if (File.Exists(blockedFile))
                {
                    blockedSites.AddRange(File.ReadAllLines(blockedFile));
                }
            }
            catch { }
        }
        
        private void LoadProxyList()
        {
            proxyList = new List<string>
            {
                "https://proxy1.octopusbrowser.com",
                "https://proxy2.octopusbrowser.com",
                "https://proxy3.octopusbrowser.com"
            };
            
            // بارگذاری پراکسی‌ها از فایل
            try
            {
                var proxyFile = Path.Combine(Application.StartupPath, "proxies.txt");
                if (File.Exists(proxyFile))
                {
                    proxyList.AddRange(File.ReadAllLines(proxyFile));
                }
            }
            catch { }
        }
        
        private void StartMemoryOptimization()
        {
            memoryTimer = new Timer
            {
                Interval = MEMORY_CLEANUP_INTERVAL
            };
            
            memoryTimer.Tick += (s, e) =>
            {
                OptimizeMemory();
                UpdateMemoryUsage();
            };
            
            memoryTimer.Start();
        }
        
        private void OptimizeMemory()
        {
            try
            {
                // حذف تب‌هایی که بیش از 10 دقیقه استفاده نشده‌اند
                var now = DateTime.Now;
                var tabsToRemove = new List<TabPage>();
                
                foreach (TabPage tab in tabControl.TabPages)
                {
                    if (lastAccessTime.ContainsKey(tab.Text))
                    {
                        var lastAccess = lastAccessTime[tab.Text];
                        if ((now - lastAccess).TotalMinutes > 10 && tabControl.TabCount > 1)
                        {
                            tabsToRemove.Add(tab);
                        }
                    }
                }
                
                foreach (var tab in tabsToRemove)
                {
                    tabControl.TabPages.Remove(tab);
                    lastAccessTime.Remove(tab.Text);
                    tab.Dispose();
                }
                
                // فراخوانی Garbage Collector
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            catch { }
        }
        
        private void UpdateMemoryUsage()
        {
            try
            {
                var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                var memoryMB = currentProcess.WorkingSet64 / (1024 * 1024);
                var statusLabel = statusStrip.Items[2] as ToolStripStatusLabel;
                statusLabel.Text = $"حافظه: {memoryMB} MB";
            }
            catch { }
        }
        
        private WebView2 GetCurrentWebView()
        {
            if (tabControl.SelectedTab != null)
            {
                return tabControl.SelectedTab.Controls.OfType<WebView2>().FirstOrDefault();
            }
            return null;
        }
        
        private void NavigateToUrl(string url)
        {
            var webView = GetCurrentWebView();
            if (webView != null)
            {
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                {
                    url = "https://" + url;
                }
                webView.CoreWebView2.Navigate(url);
            }
        }
        
        private void UpdateUrlBar(string url)
        {
            var urlTextBox = toolStrip.Items[4] as ToolStripTextBox;
            urlTextBox.Text = url;
        }
        
        private void UpdateStatus(string message)
        {
            var statusLabel = statusStrip.Items[0] as ToolStripStatusLabel;
            statusLabel.Text = message;
        }
        
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            memoryTimer?.Stop();
            
            try
            {
                // ذخیره تنظیمات
                var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                    "OctopusBrowser", "config.ini");
                
                var configDir = Path.GetDirectoryName(configPath);
                if (!Directory.Exists(configDir))
                    Directory.CreateDirectory(configDir);
                
                var config = new List<string>
                {
                    $"LastSessionTabs={tabControl.TabCount}",
                    $"LastActiveTab={tabControl.SelectedIndex}"
                };
                
                File.WriteAllLines(configPath, config);
            }
            catch { }
        }
    }
}