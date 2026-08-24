using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Principal;
using System.Windows.Forms;

namespace PayCalcDXProInstaller
{
    /// <summary>
    /// 給与計算DXPro ブラウザ版（スタンドアロン・self-contained）の GUI セットアップ。
    /// 勤怠管理DXPro の Setup.exe を参考に作成。ただし本アプリは .NET 同梱のため Node.js 等の
    /// ダウンロードは不要＝完全オフラインで導入できる。インストールはユーザー領域のため管理者権限も不要。
    ///
    /// Setup.exe と同じフォルダに「app\」（発行済みアプリ本体）と「migrator\」（移行ツール）が
    /// 置かれている前提で、それらを %LOCALAPPDATA%\Programs\PayCalcDXPro へ配置する。
    /// </summary>
    public class SetupForm : Form
    {
        private Label titleLabel;
        private Label statusLabel;
        private ProgressBar progressBar;
        private CheckBox chkDesktop;
        private CheckBox chkAutoStart;
        private CheckBox chkLaunch;
        private Button installButton;
        private Button closeButton;
        private Panel headerPanel;
        private TextBox txtBase;             // 配置先の基点フォルダ（ユーザー変更可）
        private Button browseButton;
        private Label fullPathLabel;         // 実際の配置先（基点＋固定サフィックス）を表示

        private readonly string srcDir;      // Setup.exe の置かれたフォルダ（app\ / migrator\ を含む）
        private string installDir;           // アプリ本体の配置先（インストール実行時に基点から確定）
        private readonly string dataDir;     // 給与データ（暗号化DB＋鍵）の格納先
        private readonly string desktop;
        private readonly string startMenu;   // ユーザーのスタートメニュー内プログラムグループ
        private readonly string startup;     // ユーザーのスタートアップ（自動起動）フォルダ

        private BackgroundWorker worker;

        private const string AppName = "給与計算DXPro";
        private const string AppVersion = "26.4.0.0";
        private const string AppYear = "2026";  // 年度版。リリースで年が変わったらここを更新する（例：2027）。
        private const string InstallBaseDefault = @"C:\syuhousystem";     // 基点の既定（ユーザー変更可）
        private const string InstallSuffix = @"PayCalcDXPro\browser";     // 基点直下に必ず付く固定部
        private const string ExeName = "PayCalcDXPro.Web.exe";
        private const string ProcName = "PayCalcDXPro.Web";
        private const string Url = "http://localhost:5080";

        public SetupForm()
        {
            srcDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            // 給与データは従来どおりユーザー領域（DPAPIのユーザー単位暗号化に整合）。
            dataDir = Path.Combine(local, "PayCalcDXPro", "data");
            desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName);
            startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);

            InitializeComponent();

            worker = new BackgroundWorker { WorkerReportsProgress = true };
            worker.DoWork += Worker_DoWork;
            worker.ProgressChanged += Worker_ProgressChanged;
            worker.RunWorkerCompleted += Worker_RunWorkerCompleted;
        }

        private void InitializeComponent()
        {
            Text = AppName + " セットアップ";
            Size = new Size(560, 500);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            Font = new Font("Yu Gothic UI", 9F);

            // ── ヘッダー（アプリと同系の白基調＋インディゴのアクセント）──
            headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 72,
                BackColor = Color.FromArgb(57, 73, 171) // #3949AB（アプリの Primary）
            };
            titleLabel = new Label
            {
                Text = AppName,
                ForeColor = Color.White,
                Font = new Font("Yu Gothic UI", 16F, FontStyle.Bold),
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(20, 0, 0, 0)
            };
            var subLabel = new Label
            {
                Text = "ブラウザ版セットアップ",
                ForeColor = Color.FromArgb(220, 224, 245),
                Font = new Font("Yu Gothic UI", 9F),
                AutoSize = false,
                Dock = DockStyle.Bottom,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(22, 0, 0, 6)
            };
            headerPanel.Controls.Add(titleLabel);
            headerPanel.Controls.Add(subLabel);

            var descLabel = new Label
            {
                Text =
                    "このPCに " + AppName + " をインストールします。\r\n" +
                    "・.NET のインストールは不要です（同梱・オフラインで導入できます）。\r\n" +
                    "・環境により管理者権限が必要な場合があります（求められたら許可してください）。",
                Location = new Point(20, 84),
                Size = new Size(510, 58),
                AutoSize = false
            };

            // ── インストール先（基点フォルダ）：ユーザー変更可。直下に PayCalcDXPro\browser\<年度> を自動付与 ──
            var baseLabel = new Label
            {
                Text = "インストール先（基点フォルダ）",
                Location = new Point(20, 146),
                Size = new Size(300, 18)
            };
            txtBase = new TextBox
            {
                Text = InstallBaseDefault,
                Location = new Point(20, 166),
                Size = new Size(400, 24)
            };
            txtBase.TextChanged += (s, e) => UpdateFullPath();
            browseButton = new Button
            {
                Text = "参照...",
                Location = new Point(428, 165),
                Size = new Size(102, 26),
                FlatStyle = FlatStyle.System
            };
            browseButton.Click += BrowseButton_Click;
            fullPathLabel = new Label
            {
                Location = new Point(20, 196),
                Size = new Size(510, 38),
                ForeColor = Color.FromArgb(57, 73, 171)
            };

            chkDesktop = new CheckBox
            {
                Text = "デスクトップにショートカットを作成する",
                Checked = true,
                Location = new Point(20, 240),
                Size = new Size(400, 24)
            };
            chkAutoStart = new CheckBox
            {
                Text = "Windows ログオン時に自動起動する",
                Checked = false,
                Location = new Point(20, 266),
                Size = new Size(400, 24)
            };
            chkLaunch = new CheckBox
            {
                Text = "インストール後に " + AppName + " を起動する",
                Checked = true,
                Location = new Point(20, 292),
                Size = new Size(400, 24)
            };

            progressBar = new ProgressBar
            {
                Location = new Point(20, 330),
                Size = new Size(510, 20),
                Minimum = 0,
                Maximum = 100
            };
            statusLabel = new Label
            {
                Text = "「インストール」を押すと開始します。",
                Location = new Point(20, 354),
                Size = new Size(510, 34),
                ForeColor = Color.FromArgb(90, 90, 90)
            };

            installButton = new Button
            {
                Text = "インストール",
                Location = new Point(300, 396),
                Size = new Size(130, 32),
                BackColor = Color.FromArgb(57, 73, 171),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            installButton.FlatAppearance.BorderSize = 0;
            installButton.Click += InstallButton_Click;

            closeButton = new Button
            {
                Text = "閉じる",
                Location = new Point(440, 396),
                Size = new Size(90, 32),
                FlatStyle = FlatStyle.System
            };
            closeButton.Click += (s, e) => Close();

            Controls.Add(descLabel);
            Controls.Add(baseLabel);
            Controls.Add(txtBase);
            Controls.Add(browseButton);
            Controls.Add(fullPathLabel);
            Controls.Add(chkDesktop);
            Controls.Add(chkAutoStart);
            Controls.Add(chkLaunch);
            Controls.Add(progressBar);
            Controls.Add(statusLabel);
            Controls.Add(installButton);
            Controls.Add(closeButton);
            Controls.Add(headerPanel);

            UpdateFullPath();
        }

        /// <summary>入力中の基点から実際の配置先（基点＋固定サフィックス＋年度）を計算する。</summary>
        private string ComputeInstallDir()
        {
            string b = (txtBase.Text ?? "").Trim().TrimEnd('\\');
            return Path.Combine(b, InstallSuffix, AppYear);
        }

        /// <summary>配置先プレビューとデータ格納先をラベルへ反映する。</summary>
        private void UpdateFullPath()
        {
            fullPathLabel.Text = "配置先：" + ComputeInstallDir() + "\r\nデータ：" + dataDir;
        }

        private void BrowseButton_Click(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "インストール先の基点フォルダを選択してください（直下に " + InstallSuffix + "\\" + AppYear + " を作成します）。";
                dlg.ShowNewFolderButton = true;
                string cur = (txtBase.Text ?? "").Trim();
                if (Directory.Exists(cur)) dlg.SelectedPath = cur;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    txtBase.Text = dlg.SelectedPath;
                }
            }
        }

        private void InstallButton_Click(object sender, EventArgs e)
        {
            // 配布物の妥当性チェック（app\PayCalcDXPro.Web.exe が隣にあるか）。
            string appExe = Path.Combine(srcDir, "app", ExeName);
            if (!File.Exists(appExe))
            {
                MessageBox.Show(
                    "配布ファイルが見つかりません。\r\n\r\n" +
                    "Setup.exe と同じ場所に「app」フォルダ（" + ExeName + " を含む）が必要です。\r\n" +
                    "配布フォルダ（またはZipを展開したフォルダ）ごとご利用ください。",
                    "セットアップ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 基点フォルダの妥当性チェック（ドライブ付きの絶対パス）。
            string baseDir = (txtBase.Text ?? "").Trim().TrimEnd('\\');
            if (baseDir.Length == 0 || !Path.IsPathRooted(baseDir) ||
                baseDir.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                MessageBox.Show(
                    "インストール先の基点フォルダを、ドライブから始まる正しいパスで指定してください。\r\n" +
                    "例：C:\\syuhousystem",
                    "セットアップ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 実際の配置先を確定（この後の処理はこの値を使う）。
            installDir = ComputeInstallDir();

            installButton.Enabled = false;
            closeButton.Enabled = false;
            chkDesktop.Enabled = chkAutoStart.Enabled = chkLaunch.Enabled = false;
            txtBase.Enabled = browseButton.Enabled = false;
            worker.RunWorkerAsync();
        }

        private void Worker_DoWork(object sender, DoWorkEventArgs e)
        {
            var w = (BackgroundWorker)sender;

            w.ReportProgress(3, "起動中のアプリを停止しています...");
            StopRunning();

            w.ReportProgress(8, "フォルダを準備しています...");
            Directory.CreateDirectory(installDir);
            Directory.CreateDirectory(dataDir);

            w.ReportProgress(15, "アプリ本体をコピーしています...");
            CopyTree(Path.Combine(srcDir, "app"), Path.Combine(installDir, "app"), w, 15, 65);

            string migSrc = Path.Combine(srcDir, "migrator");
            if (Directory.Exists(migSrc))
            {
                w.ReportProgress(66, "移行ツールをコピーしています...");
                CopyTree(migSrc, Path.Combine(installDir, "migrator"), w, 66, 74);
            }

            // マニュアル等（PDF）を同梱していれば配置する。
            string docSrc = Path.Combine(srcDir, "Documents");
            if (Directory.Exists(docSrc))
            {
                w.ReportProgress(75, "マニュアルをコピーしています...");
                CopyTree(docSrc, Path.Combine(installDir, "Documents"), w, 75, 76);
            }

            // ブランドアイコン（あれば）をインストール先へ配置し、ショートカットで使う。
            string icoSrc = Path.Combine(srcDir, "PayCalcDXPro.ico");
            if (File.Exists(icoSrc))
            {
                try { File.Copy(icoSrc, Path.Combine(installDir, "PayCalcDXPro.ico"), true); } catch { }
            }

            w.ReportProgress(76, "起動用ファイルを作成しています...");
            WriteLaunchers();

            w.ReportProgress(84, "ショートカットを作成しています...");
            CreateShortcuts();

            w.ReportProgress(90, "自動起動を設定しています...");
            ConfigureAutoStart();

            w.ReportProgress(95, "登録情報を書き込んでいます...");
            RegisterUninstall();

            w.ReportProgress(100, "完了しました。");
        }

        private void Worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            progressBar.Value = Math.Min(100, e.ProgressPercentage);
            string s = e.UserState as string;
            if (s != null) statusLabel.Text = s;
        }

        private void Worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                statusLabel.Text = "エラーが発生しました。";

                // 配置先が C:\ 配下のため、環境によっては書き込みに管理者権限が要ることがある。
                // 権限エラーなら「管理者として再実行」を促す。
                bool permission = e.Error is UnauthorizedAccessException
                    || (e.Error is IOException && !Program.IsAdmin());
                if (permission && !Program.IsAdmin())
                {
                    if (MessageBox.Show(
                        "インストール先（" + installDir + "）に書き込めませんでした。\r\n\r\n" +
                        "管理者として実行し直しますか？",
                        "セットアップ", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    {
                        Program.RestartAsAdmin();
                        Close();
                        return;
                    }
                }
                else
                {
                    MessageBox.Show("インストールに失敗しました。\r\n\r\n" + e.Error.Message,
                        "セットアップ", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                installButton.Enabled = true;
                closeButton.Enabled = true;
                chkDesktop.Enabled = chkAutoStart.Enabled = chkLaunch.Enabled = true;
                txtBase.Enabled = browseButton.Enabled = true;
                return;
            }

            statusLabel.Text = "インストールが完了しました。";
            closeButton.Enabled = true;
            closeButton.Text = "完了";

            if (chkLaunch.Checked) LaunchApp();

            MessageBox.Show(
                AppName + " のインストールが完了しました。\r\n\r\n" +
                "デスクトップ／スタートメニューの「" + AppName + "」から起動できます。\r\n" +
                "起動するとローカルでサーバが立ち上がり、既定ブラウザで画面（" + Url + "）が開きます。",
                "セットアップ", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ── 実処理 ────────────────────────────────────────────────

        /// <summary>起動中の給与計算DXPro（サーバ）を停止する。上書きコピーのロック回避。</summary>
        private void StopRunning()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(ProcName))
                {
                    try { p.Kill(); p.WaitForExit(3000); } catch { }
                }
            }
            catch { }
        }

        /// <summary>ディレクトリを再帰コピー（進捗を from〜to の範囲で報告）。</summary>
        private void CopyTree(string source, string dest, BackgroundWorker w, int from, int to)
        {
            var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
            int total = Math.Max(1, files.Length);
            Directory.CreateDirectory(dest);
            for (int i = 0; i < files.Length; i++)
            {
                string rel = files[i].Substring(source.Length).TrimStart('\\');
                string target = Path.Combine(dest, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(files[i], target, true);
                int pct = from + (int)((to - from) * (i + 1L) / total);
                w.ReportProgress(pct);
            }
        }

        /// <summary>隠し起動VBS（サーバをコンソール非表示で起動）と停止バッチを作成する。</summary>
        private void WriteLaunchers()
        {
            string appExe = Path.Combine(installDir, "app", ExeName);
            string appDir = Path.Combine(installDir, "app");

            // 起動：コンソール窓を出さずにサーバを起動（ブラウザはアプリ側が自動で開く）。
            string vbs =
                "' " + AppName + " 起動（コンソール非表示）\r\n" +
                "Set sh = CreateObject(\"WScript.Shell\")\r\n" +
                "sh.CurrentDirectory = \"" + appDir + "\"\r\n" +
                "sh.Run \"\"\"" + appExe + "\"\"\", 0, False\r\n";
            File.WriteAllText(Path.Combine(installDir, "起動.vbs"), vbs, System.Text.Encoding.Unicode);

            // 停止：サーバプロセスを終了する。
            string stop =
                "@echo off\r\n" +
                "taskkill /IM \"" + ExeName + "\" /F >nul 2>&1\r\n";
            File.WriteAllText(Path.Combine(installDir, "停止.bat"), stop, System.Text.Encoding.GetEncoding(932));
        }

        private void CreateShortcuts()
        {
            string vbsPath = Path.Combine(installDir, "起動.vbs");
            string iconPath = Path.Combine(installDir, "app", ExeName) + ",0";
            string icoFile = Path.Combine(installDir, "PayCalcDXPro.ico");
            if (File.Exists(icoFile)) iconPath = icoFile + ",0";

            Directory.CreateDirectory(startMenu);

            // デスクトップ（任意）
            if (chkDesktop.Checked)
                MakeShortcut(Path.Combine(desktop, AppName + ".lnk"), "wscript.exe",
                    "//nologo \"" + vbsPath + "\"", installDir, iconPath, AppName + " ブラウザ版");

            // スタートメニュー：起動・停止・アンインストール
            MakeShortcut(Path.Combine(startMenu, AppName + ".lnk"), "wscript.exe",
                "//nologo \"" + vbsPath + "\"", installDir, iconPath, AppName + " ブラウザ版");
            MakeShortcut(Path.Combine(startMenu, AppName + " を停止.lnk"), "cmd.exe",
                "/c \"" + Path.Combine(installDir, "停止.bat") + "\"", installDir, iconPath, "サーバを停止します");

            // マニュアル（同梱PDF）をスタートメニューから開けるようにする。
            string docDir = Path.Combine(installDir, "Documents");
            if (Directory.Exists(docDir))
            {
                foreach (string pdf in Directory.GetFiles(docDir, "*.pdf"))
                {
                    string name = Path.GetFileNameWithoutExtension(pdf);
                    // 既定のPDFビューアで開く（TargetPath に PDF を直接指定）。アイコンはブランドアイコンを使う。
                    MakeShortcut(Path.Combine(startMenu, name + ".lnk"), pdf, "", docDir, iconPath,
                        AppName + " " + name);
                }
            }

            CreateUninstaller();
            MakeShortcut(Path.Combine(startMenu, "アンインストール.lnk"), "powershell.exe",
                "-ExecutionPolicy Bypass -File \"" + Path.Combine(installDir, "Uninstall.ps1") + "\"",
                installDir, iconPath, AppName + " をアンインストールします");
        }

        /// <summary>WScript.Shell（COM）で .lnk を作成する。csc の参照を増やさずに済む。</summary>
        private void MakeShortcut(string lnkPath, string target, string args, string workDir, string icon, string desc)
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            try
            {
                dynamic sc = shell.CreateShortcut(lnkPath);
                sc.TargetPath = target;
                sc.Arguments = args;
                sc.WorkingDirectory = workDir;
                sc.IconLocation = icon;
                sc.Description = desc;
                sc.WindowStyle = 7; // 最小化（wscript 起動なので実質非表示）
                sc.Save();
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
            }
        }

        /// <summary>スタートアップ フォルダへの .lnk 配置で自動起動を設定/解除（管理者不要・ユーザー単位）。</summary>
        private void ConfigureAutoStart()
        {
            string lnk = Path.Combine(startup, AppName + ".lnk");
            try
            {
                if (chkAutoStart.Checked)
                {
                    string vbsPath = Path.Combine(installDir, "起動.vbs");
                    string iconPath = Path.Combine(installDir, "app", ExeName) + ",0";
                    MakeShortcut(lnk, "wscript.exe", "//nologo \"" + vbsPath + "\"", installDir, iconPath,
                        AppName + " を自動起動");
                }
                else if (File.Exists(lnk))
                {
                    File.Delete(lnk);
                }
            }
            catch { /* 自動起動の設定失敗は致命的ではない */ }
        }

        private void CreateUninstaller()
        {
            // 単一引用符のPowerShell文字列はバックスラッシュを素で扱えるので、パスは '' でクォートし ' のみエスケープする。
            string dataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PayCalcDXPro");
            string desktopLnk = Path.Combine(desktop, AppName + ".lnk");
            string startupLnk = Path.Combine(startup, AppName + ".lnk");

            string ps =
                "param([switch]$RemoveData)\r\n" +   // param はスクリプト先頭に置く必要がある。
                "$ErrorActionPreference = 'SilentlyContinue'\r\n" +
                "# 実行中のサーバを停止\r\n" +
                "Get-Process -Name '" + ProcName + "' | Stop-Process -Force\r\n" +
                "Start-Sleep -Milliseconds 800\r\n" +
                "# ショートカット類\r\n" +
                "Remove-Item -LiteralPath '" + Esc(desktopLnk) + "' -Force\r\n" +
                "Remove-Item -LiteralPath '" + Esc(startupLnk) + "' -Force\r\n" +
                "Remove-Item -LiteralPath '" + Esc(startMenu) + "' -Recurse -Force\r\n" +
                "# 登録情報（HKCU）\r\n" +
                "Remove-Item -LiteralPath 'HKCU:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\PayCalcDXPro' -Recurse -Force\r\n" +
                "# アプリ本体（給与データは既定で残し、-RemoveData 指定時のみ削除）\r\n" +
                "Remove-Item -LiteralPath '" + Esc(installDir) + "' -Recurse -Force\r\n" +
                "if ($RemoveData) { Remove-Item -LiteralPath '" + Esc(dataRoot) + "' -Recurse -Force }\r\n" +
                "Write-Host '" + AppName + " をアンインストールしました。' -ForegroundColor Green\r\n" +
                "Start-Sleep -Seconds 2\r\n";
            File.WriteAllText(Path.Combine(installDir, "Uninstall.ps1"), ps, new System.Text.UTF8Encoding(true));
        }

        /// <summary>PowerShell 単一引用符文字列用のエスケープ（' → ''）。</summary>
        private static string Esc(string s) { return s.Replace("'", "''"); }

        /// <summary>「プログラムの追加と削除」に表示するための HKCU 登録（管理者不要）。</summary>
        private void RegisterUninstall()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PayCalcDXPro"))
                {
                    if (key == null) return;
                    string uninstall = "powershell.exe -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" +
                                       Path.Combine(installDir, "Uninstall.ps1") + "\"";
                    key.SetValue("DisplayName", AppName);
                    key.SetValue("DisplayVersion", AppVersion);
                    key.SetValue("Publisher", "周豊システム");
                    key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                    key.SetValue("InstallLocation", installDir);
                    key.SetValue("UninstallString", uninstall);
                    key.SetValue("QuietUninstallString", uninstall);
                    key.SetValue("DisplayIcon", Path.Combine(installDir, "app", ExeName) + ",0");
                    key.SetValue("NoModify", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("NoRepair", 1, Microsoft.Win32.RegistryValueKind.DWord);
                }
            }
            catch { /* 登録失敗はインストール自体を妨げない */ }
        }

        private void LaunchApp()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "wscript.exe",
                    Arguments = "//nologo \"" + Path.Combine(installDir, "起動.vbs") + "\"",
                    UseShellExecute = false,
                    WorkingDirectory = installDir
                });
            }
            catch { }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }

        public static bool IsAdmin()
        {
            try
            {
                return new WindowsPrincipal(WindowsIdentity.GetCurrent())
                    .IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public static void RestartAsAdmin()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    UseShellExecute = true,
                    Verb = "runas"   // UAC 昇格
                });
            }
            catch { /* ユーザーが昇格をキャンセルした等 */ }
        }
    }
}
