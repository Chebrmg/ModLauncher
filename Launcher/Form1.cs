using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Launcher
{
    public partial class Form1 : Form
    {
        string root = AppDomain.CurrentDomain.BaseDirectory;

        // Обновление мода
        private ModUpdater? modUpdater;
        private string serverConfigPath;
        private Label? statusLabel;
        private ProgressBar? downloadProgress;

        // Автосохранения
        private bool autoSaveActive = false;
        private System.Windows.Forms.Timer autoSaveTimer;
        private string autoSaveFolder;
        private int autoSaveCounter = 0;
        private string autoSaveSessionDate;
        private string autoSaveConfigPath;
        private Dictionary<string, DateTime> lastModified = new Dictionary<string, DateTime>();
        private Button btnAutoSave;
        private Label hintLabel;

        public Form1()
        {
            this.DoubleBuffered = true;
            this.SetStyle(
                ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint, true);
            this.UpdateStyles();

            InitializeComponent();
            autoSaveConfigPath = Path.Combine(root, "autosave_status.txt");
            serverConfigPath = Path.Combine(root, "server_config.json");
            InitModUpdater();
        }

        private void InitModUpdater()
        {
            string serverUrl = ReadServerUrl();
            if (!string.IsNullOrEmpty(serverUrl))
            {
                modUpdater = new ModUpdater(serverUrl, root);
            }
        }

        private string ReadServerUrl()
        {
            try
            {
                if (File.Exists(serverConfigPath))
                {
                    string json = File.ReadAllText(serverConfigPath);
                    using var doc = JsonDocument.Parse(json);
                    return doc.RootElement.GetProperty("server_url").GetString() ?? "";
                }
            }
            catch { }
            return "";
        }

        private void btnStartPlain_Click(object sender, EventArgs e)
        {
            // Если игра уже запущена — просто скрываемся/закрываемся
            if (Process.GetProcessesByName("H5_Game").Length > 0)
            {
                if (autoSaveActive)
                    this.Hide();
                else
                    Application.Exit();
                return;
            }

            string binPath = Path.Combine(root, "bin");
            string gameExe = Path.Combine(binPath, "H5_Game.exe");
            StartGame(binPath, gameExe);
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            RunGame(false);
        }

        private void btnStartMod_Click(object sender, EventArgs e)
        {
            RunGame(true);
        }

        void RunGame(bool useMod)
        {
            string dataPath = Path.Combine(root, "data");
            string mapsPath = Path.Combine(root, "maps");
            string binPath = Path.Combine(root, "bin");

            string modePak = Path.Combine(dataPath, "Mode_Modifier.pak");
            string tempPak = Path.Combine(root, "Mode_Modifier.pak");

            string gameExe = Path.Combine(binPath, "H5_Game.exe");

            string modSource = Path.Combine(root, "Chebovka1.5.2.pak");
            string modTarget = Path.Combine(dataPath, "Chebovka1.5.2.pak");

            try
            {
                // Сначала ВСЕГДА закрываем игру
                KillProcess("H5_Game");

                // Mode_Modifier (НЕ КРИТИЧЕСКИЙ)
                if (File.Exists(modePak))
                {
                    try
                    {
                        File.Copy(modePak, tempPak, true);
                        File.Copy(tempPak, modePak, true);

                        if (File.Exists(tempPak))
                            File.Delete(tempPak);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            "⚠ Ошибка Mode_Modifier (игнорируется):\n" + ex.Message,
                            "Предупреждение",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning
                        );
                    }
                }
                else
                {
                    Console.WriteLine("Mode_Modifier.pak отсутствует, пропуск");
                }

                // Обновляем архивы
                UpdateZipDate(modePak);

                string universePak = Path.Combine(dataPath, "Universe_mod.pak");
                string textPak = Path.Combine(dataPath, "universe_mod_texts_ru.pak");
                string mapFile = Path.Combine(mapsPath, "FRFB_UNIVERSE.h5m");

                if (!IsPatched(universePak))
                    UpdateZipDate(universePak);

                if (!IsPatched(textPak))
                    UpdateZipDate(textPak);

                if (!IsPatched(mapFile))
                    UpdateZipDate(mapFile);

                // Если режим мода
                if (useMod)
                {
                    // Пробуем скачать/обновить мод с сервера
                    if (modUpdater != null)
                    {
                        SetStatus("Проверка обновлений мода...");
                        bool needsUpdate = false;
                        try
                        {
                            needsUpdate = Task.Run(() => modUpdater.IsUpdateAvailableAsync()).Result;
                        }
                        catch
                        {
                            SetStatus("Сервер недоступен, используется локальная версия");
                        }

                        if (needsUpdate)
                        {
                            SetStatus("Скачивание мода...");
                            ShowDownloadProgress(true);

                            var progress = new Progress<(long downloaded, long total)>(p =>
                            {
                                if (p.total > 0 && downloadProgress != null)
                                {
                                    int pct = (int)(p.downloaded * 100 / p.total);
                                    downloadProgress.Value = Math.Min(pct, 100);
                                    double mb = p.downloaded / (1024.0 * 1024.0);
                                    double totalMb = p.total / (1024.0 * 1024.0);
                                    SetStatus($"Скачивание: {mb:F1} / {totalMb:F1} МБ ({pct}%)");
                                }
                            });

                            bool ok = false;
                            try
                            {
                                ok = Task.Run(() => modUpdater.DownloadModAsync(progress, CancellationToken.None)).Result;
                            }
                            catch { }

                            ShowDownloadProgress(false);

                            if (ok)
                            {
                                SetStatus("Мод обновлён!");
                            }
                            else
                            {
                                SetStatus("Ошибка скачивания, используется локальная версия");
                            }
                        }
                        else
                        {
                            SetStatus("Мод актуален");
                        }
                    }

                    // Определяем имя файла мода
                    string actualModSource = modSource;
                    if (modUpdater != null)
                    {
                        var remote = Task.Run(() => modUpdater.GetRemoteVersionAsync()).Result;
                        if (remote != null)
                        {
                            string serverModFile = Path.Combine(root, remote.file_name);
                            if (File.Exists(serverModFile))
                                actualModSource = serverModFile;
                        }
                    }

                    if (!File.Exists(actualModSource))
                    {
                        MessageBox.Show(
                            "Мод не найден!\n\nПроверьте подключение к серверу или наличие файла мода.",
                            "Ошибка",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error
                        );
                        SetStatus("");
                        return;
                    }

                    try
                    {
                        File.Copy(actualModSource, modTarget, true);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            "Ошибка установки мода:\n" + ex.Message,
                            "Ошибка",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error
                        );
                        SetStatus("");
                        return;
                    }

                    SetStatus("");
                }

                // Запуск
                StartGame(binPath, gameExe);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        void KillProcess(string name)
        {
            foreach (var proc in Process.GetProcessesByName(name))
            {
                try
                {
                    proc.Kill();
                    proc.WaitForExit();
                }
                catch { }
            }
        }

        void StartGame(string workingDir, string exePath)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = workingDir,
                    UseShellExecute = true
                });

                if (!autoSaveActive)
                {
                    Application.Exit();
                }
                else
                {
                    // Скрываем лаунчер — работает в фоне
                    this.Hide();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось запустить игру: " + ex.Message);
            }
        }

        void UpdateZipDate(string zipPath)
        {
            try
            {
                if (!File.Exists(zipPath))
                    return;

                string tempPath = zipPath + "_temp";
                DateTime newDate = new DateTime(2026, 1, 1);

                if (File.Exists(tempPath))
                    File.Delete(tempPath);

                using (var source = ZipFile.OpenRead(zipPath))
                using (var target = ZipFile.Open(tempPath, ZipArchiveMode.Create))
                {
                    foreach (var entry in source.Entries)
                    {
                        var newEntry = target.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                        newEntry.LastWriteTime = newDate;

                        using (var input = entry.Open())
                        using (var output = newEntry.Open())
                        {
                            input.CopyTo(output);
                        }
                    }

                    var flag = target.CreateEntry("_patched.flag");
                    using (var writer = new StreamWriter(flag.Open()))
                    {
                        writer.Write("patched");
                    }
                }

                File.Delete(zipPath);
                File.Move(tempPath, zipPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("ZIP error: " + ex.Message);
            }
        }

        bool IsPatched(string zipPath)
        {
            try
            {
                if (!File.Exists(zipPath))
                    return false;

                using (var zip = ZipFile.OpenRead(zipPath))
                {
                    return zip.Entries.Any(e => e.FullName == "_patched.flag");
                }
            }
            catch
            {
                return false;
            }
        }

        // ===================== КОНФИГ АВТОСОХРАНЕНИЙ =====================

        private void WriteAutoSaveConfig(string status)
        {
            try
            {
                string content = status + "\n"
                    + (autoSaveSessionDate ?? "") + "\n"
                    + autoSaveCounter.ToString();
                File.WriteAllText(autoSaveConfigPath, content);
            }
            catch { }
        }

        private string ReadAutoSaveStatus()
        {
            try
            {
                if (!File.Exists(autoSaveConfigPath))
                    return "off";
                string[] lines = File.ReadAllLines(autoSaveConfigPath);
                if (lines.Length > 0)
                    return lines[0].Trim();
            }
            catch { }
            return "off";
        }

        private void LoadAutoSaveConfig()
        {
            try
            {
                if (!File.Exists(autoSaveConfigPath))
                    return;
                string[] lines = File.ReadAllLines(autoSaveConfigPath);
                if (lines.Length >= 2 && !string.IsNullOrEmpty(lines[1].Trim()))
                    autoSaveSessionDate = lines[1].Trim();
                if (lines.Length >= 3)
                    int.TryParse(lines[2].Trim(), out autoSaveCounter);

                if (autoSaveSessionDate != null)
                {
                    string folder = Path.Combine(root, "Автосохранения", autoSaveSessionDate);
                    if (Directory.Exists(folder))
                        autoSaveFolder = folder;
                }
            }
            catch { }
        }

        // ===================== АВТОСОХРАНЕНИЯ =====================

        private void StartAutoSaveMonitoring()
        {
            lastModified.Clear();
            foreach (string saveFile in GetAllSaveFiles())
            {
                if (File.Exists(saveFile))
                    lastModified[saveFile] = File.GetLastWriteTime(saveFile);
            }

            autoSaveTimer = new System.Windows.Forms.Timer();
            autoSaveTimer.Interval = 2000;
            autoSaveTimer.Tick += AutoSaveTimer_Tick;
            autoSaveTimer.Start();
        }

        private void btnAutoSave_Click(object sender, EventArgs e)
        {
            if (!autoSaveActive)
            {
                // Включаем
                autoSaveActive = true;

                if (autoSaveSessionDate == null)
                {
                    autoSaveCounter = 0;
                    autoSaveFolder = null;
                    autoSaveSessionDate = DateTime.Now.ToString("MM.dd.yy_HH.mm");
                }

                StartAutoSaveMonitoring();
                WriteAutoSaveConfig("on");

                btnAutoSave.BackColor = Color.FromArgb(0, 100, 0);
                btnAutoSave.Text = "Автосохранения ✓";
            }
            else
            {
                // Выключаем
                autoSaveActive = false;
                StopAutoSave();
                WriteAutoSaveConfig("off");

                btnAutoSave.BackColor = Color.FromArgb(40, 40, 40);
                btnAutoSave.Text = "Автосохранения";
            }
        }

        private void StopAutoSave()
        {
            if (autoSaveTimer != null)
            {
                autoSaveTimer.Stop();
                autoSaveTimer.Dispose();
                autoSaveTimer = null;
            }
        }

        private List<string> GetAllSaveFiles()
        {
            var result = new List<string>();
            string profilesPath = Path.Combine(root, "UniverseTeam", "Universe Mod", "Profiles");

            if (!Directory.Exists(profilesPath))
                return result;

            foreach (string profileDir in Directory.GetDirectories(profilesPath))
            {
                string savesDir = Path.Combine(profileDir, "Saves");
                if (!Directory.Exists(savesDir))
                    continue;

                for (int i = 0; i <= 2; i++)
                {
                    string saveFile = Path.Combine(savesDir, "save_" + i + ".sav");
                    result.Add(saveFile);
                }
            }

            return result;
        }

        private void AutoSaveTimer_Tick(object sender, EventArgs e)
        {
            // Проверяем конфиг — другой экземпляр мог выключить
            if (ReadAutoSaveStatus() != "on")
            {
                autoSaveActive = false;
                StopAutoSave();
                Application.Exit();
                return;
            }

            var changedFiles = new List<string>();

            foreach (string saveFile in GetAllSaveFiles())
            {
                if (!File.Exists(saveFile))
                    continue;

                DateTime currentMod = File.GetLastWriteTime(saveFile);

                if (lastModified.ContainsKey(saveFile))
                {
                    if (currentMod > lastModified[saveFile])
                    {
                        changedFiles.Add(saveFile);
                        lastModified[saveFile] = currentMod;
                    }
                }
                else
                {
                    // Файл появился впервые — новое сохранение
                    lastModified[saveFile] = currentMod;
                    changedFiles.Add(saveFile);
                }
            }

            if (changedFiles.Count > 0)
            {
                autoSaveTimer.Stop();

                var delayTimer = new System.Windows.Forms.Timer();
                delayTimer.Interval = 2000;
                delayTimer.Tick += (s, ev) =>
                {
                    delayTimer.Stop();
                    delayTimer.Dispose();

                    // Создаём папку при первом реальном сохранении
                    if (autoSaveFolder == null)
                    {
                        autoSaveFolder = Path.Combine(root, "Автосохранения", autoSaveSessionDate);
                        Directory.CreateDirectory(autoSaveFolder);
                    }

                    foreach (string file in changedFiles)
                    {
                        try
                        {
                            string newName = GetNextSaveName() + ".sav";
                            string destPath = Path.Combine(autoSaveFolder, newName);
                            File.Copy(file, destPath, true);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Ошибка копирования сейва: " + ex.Message);
                        }
                    }

                    WriteAutoSaveConfig("on");

                    if (autoSaveActive && autoSaveTimer != null)
                        autoSaveTimer.Start();
                };
                delayTimer.Start();
            }
        }

        /// <summary>
        /// Нумерация: 111-117, 121-127, 131-137, 141-147, 211-217, ...
        /// Единицы: 1-7, Десятки: 1-4, Сотни: 1+
        /// </summary>
        private string GetNextSaveName()
        {
            int units = (autoSaveCounter % 7) + 1;
            int tens = ((autoSaveCounter / 7) % 4) + 1;
            int hundreds = (autoSaveCounter / 28) + 1;

            autoSaveCounter++;

            return hundreds.ToString() + tens.ToString() + units.ToString();
        }

        private void SetStatus(string text)
        {
            if (statusLabel != null)
            {
                if (statusLabel.InvokeRequired)
                    statusLabel.Invoke(() => statusLabel.Text = text);
                else
                    statusLabel.Text = text;
            }
        }

        private void ShowDownloadProgress(bool visible)
        {
            if (downloadProgress != null)
            {
                if (downloadProgress.InvokeRequired)
                    downloadProgress.Invoke(() => { downloadProgress.Visible = visible; downloadProgress.Value = 0; });
                else
                {
                    downloadProgress.Visible = visible;
                    downloadProgress.Value = 0;
                }
            }
        }

        private async void CheckModUpdateOnLoad()
        {
            if (modUpdater == null)
            {
                SetStatus("Сервер не настроен (server_config.json)");
                return;
            }

            SetStatus("Проверка обновлений...");
            try
            {
                var remote = await modUpdater.GetRemoteVersionAsync();
                if (remote == null)
                {
                    SetStatus("Сервер недоступен");
                    return;
                }

                string localVer = modUpdater.GetLocalVersion() ?? "не установлен";
                bool updateAvailable = await modUpdater.IsUpdateAvailableAsync();

                if (updateAvailable)
                    SetStatus($"Доступна новая версия: {remote.version} (текущая: {localVer})");
                else
                    SetStatus($"Мод {remote.mod_name} v{remote.version} — актуален");
            }
            catch
            {
                SetStatus("Ошибка проверки обновлений");
            }
        }

        private void SetHoverHint(Button btn, string hint)
        {
            btn.MouseEnter += (s, ev) =>
            {
                hintLabel.Text = hint;
                hintLabel.BackColor = Color.FromArgb(120, 0, 0, 0);
            };
            btn.MouseLeave += (s, ev) =>
            {
                hintLabel.Text = "";
                hintLabel.BackColor = Color.Transparent;
            };
        }

        // ===================== ЗАГРУЗКА ФОРМЫ =====================

        private void Form1_Load(object sender, EventArgs e)
        {
            // ФОН
            this.BackgroundImage = Image.FromFile(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bg.jpg"));
            this.BackgroundImageLayout = ImageLayout.Stretch;

            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(600, 430);
            this.Text = "ModLauncher";

            // ОСНОВНАЯ ПАНЕЛЬ (с двойной буферизацией)
            Panel panel = new DoubleBufferedPanel();
            panel.Size = new Size(500, 290);
            panel.Location = new Point(50, 60);
            panel.BackColor = Color.FromArgb(150, 0, 0, 0);
            this.Controls.Add(panel);

            // ЗАГОЛОВОК
            Label title = new Label();
            title.Text = "GAME LAUNCHER";
            title.ForeColor = Color.White;
            title.Font = new Font("Segoe UI", 24, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(
                (panel.Width - 310) / 2, 20);
            panel.Controls.Add(title);

            // Размеры и отступы для сетки 2x2
            int btnW = 220;
            int btnH = 50;
            int gapX = 20;
            int gapY = 15;
            int startX = (panel.Width - btnW * 2 - gapX) / 2;
            int startY = 110;

            // КНОПКА ЗАПУСК ИГРЫ (без изменений)
            Button btnStartPlain = new Button();
            btnStartPlain.Parent = panel;
            btnStartPlain.Text = "Запуск игры";
            btnStartPlain.FlatStyle = FlatStyle.Flat;
            btnStartPlain.FlatAppearance.BorderSize = 0;
            btnStartPlain.BackColor = Color.FromArgb(40, 40, 40);
            btnStartPlain.ForeColor = Color.White;
            btnStartPlain.Font = new Font("Segoe UI", 11, FontStyle.Regular);
            btnStartPlain.Size = new Size(btnW, btnH);
            btnStartPlain.Location = new Point(startX, startY);
            btnStartPlain.Click += btnStartPlain_Click;

            // КНОПКА ЗАПУСК С ОБНОВЛЕНИЕМ
            btnStart.Parent = panel;
            btnStart.Text = "Запуск с обновлением";
            btnStart.FlatStyle = FlatStyle.Flat;
            btnStart.FlatAppearance.BorderSize = 0;
            btnStart.BackColor = Color.FromArgb(40, 40, 40);
            btnStart.ForeColor = Color.White;
            btnStart.Font = new Font("Segoe UI", 11, FontStyle.Regular);
            btnStart.Size = new Size(btnW, btnH);
            btnStart.Location = new Point(startX + btnW + gapX, startY);

            // КНОПКА МОДА
            btnStartMod.Parent = panel;
            btnStartMod.Text = "Запуск мода";
            btnStartMod.FlatStyle = FlatStyle.Flat;
            btnStartMod.FlatAppearance.BorderSize = 0;
            btnStartMod.BackColor = Color.FromArgb(80, 0, 0);
            btnStartMod.ForeColor = Color.White;
            btnStartMod.Font = new Font("Segoe UI", 11, FontStyle.Regular);
            btnStartMod.Size = new Size(btnW, btnH);
            btnStartMod.Location = new Point(startX, startY + btnH + gapY);

            // КНОПКА АВТОСОХРАНЕНИЯ
            btnAutoSave = new Button();
            btnAutoSave.Parent = panel;
            btnAutoSave.FlatStyle = FlatStyle.Flat;
            btnAutoSave.FlatAppearance.BorderSize = 0;
            btnAutoSave.ForeColor = Color.White;
            btnAutoSave.Font = new Font("Segoe UI", 11, FontStyle.Regular);
            btnAutoSave.Size = new Size(btnW, btnH);
            btnAutoSave.Location = new Point(startX + btnW + gapX, startY + btnH + gapY);
            btnAutoSave.Click += btnAutoSave_Click;

            // ПОДСКАЗКА (полупрозрачная, внизу панели)
            hintLabel = new Label();
            hintLabel.Parent = panel;
            hintLabel.Text = "";
            hintLabel.ForeColor = Color.FromArgb(200, 255, 255, 255);
            hintLabel.BackColor = Color.FromArgb(120, 0, 0, 0);
            hintLabel.Font = new Font("Segoe UI", 10, FontStyle.Italic);
            hintLabel.Size = new Size(panel.Width - 40, 40);
            hintLabel.Location = new Point(20, panel.Height - 50);
            hintLabel.TextAlign = ContentAlignment.MiddleCenter;
            hintLabel.BackColor = Color.Transparent;

            // СТАТУС ОБНОВЛЕНИЯ МОДА
            statusLabel = new Label();
            statusLabel.Parent = panel;
            statusLabel.Text = "";
            statusLabel.ForeColor = Color.FromArgb(220, 200, 220, 255);
            statusLabel.BackColor = Color.Transparent;
            statusLabel.Font = new Font("Segoe UI", 9, FontStyle.Regular);
            statusLabel.Size = new Size(panel.Width - 40, 20);
            statusLabel.Location = new Point(20, 80);
            statusLabel.TextAlign = ContentAlignment.MiddleCenter;

            // ПРОГРЕСС-БАР СКАЧИВАНИЯ
            downloadProgress = new ProgressBar();
            downloadProgress.Parent = panel;
            downloadProgress.Size = new Size(panel.Width - 80, 12);
            downloadProgress.Location = new Point(40, 95);
            downloadProgress.Visible = false;
            downloadProgress.Style = ProgressBarStyle.Continuous;

            // Привязываем события наведения
            SetHoverHint(btnStartPlain, "Обычный запуск игры без каких-либо изменений");
            SetHoverHint(btnStart, "Запуск с обновлением архивов и файлов мода");
            SetHoverHint(btnStartMod, "Скачивает/обновляет мод Chebovka с сервера и запускает");
            SetHoverHint(btnAutoSave, "Мониторинг и копирование автосохранений игры");

            // Проверяем обновления мода при загрузке
            CheckModUpdateOnLoad();

            // Восстанавливаем состояние из конфига
            if (ReadAutoSaveStatus() == "on")
            {
                LoadAutoSaveConfig();
                autoSaveActive = true;

                btnAutoSave.BackColor = Color.FromArgb(0, 100, 0);
                btnAutoSave.Text = "Автосохранения ✓";
            }
            else
            {
                btnAutoSave.BackColor = Color.FromArgb(40, 40, 40);
                btnAutoSave.Text = "Автосохранения";
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopAutoSave();
            if (autoSaveActive)
                WriteAutoSaveConfig("off");
            base.OnFormClosing(e);
        }
    }

    public class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            this.DoubleBuffered = true;
            this.SetStyle(
                ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint, true);
            this.UpdateStyles();
        }
    }
}
