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

        // Путь к игре (отдельно от лаунчера)
        private string gamePath = "";
        private string gameConfigPath;

        // Обновление мода
        private ModUpdater? modUpdater;
        private string updateConfigPath;

        // Обновление лаунчера
        private LauncherUpdater? launcherUpdater;
        private Button? btnUpdateLauncher;

        // UI элементы
        private Label? statusLabel;
        private TrainProgressBar? trainProgress;

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
            updateConfigPath = Path.Combine(root, "update_config.json");
            gameConfigPath = Path.Combine(root, "game_path.json");
            LoadGamePath();
            InitModUpdater();
            InitLauncherUpdater();
        }

        private void LoadGamePath()
        {
            try
            {
                if (File.Exists(gameConfigPath))
                {
                    string json = File.ReadAllText(gameConfigPath);
                    using var doc = JsonDocument.Parse(json);
                    gamePath = doc.RootElement.GetProperty("game_path").GetString() ?? "";
                }
            }
            catch { }
        }

        private void SaveGamePath(string path)
        {
            gamePath = path;
            try
            {
                string json = JsonSerializer.Serialize(new { game_path = path }, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(gameConfigPath, json);
            }
            catch { }
        }

        private void InitModUpdater()
        {
            string versionUrl = ReadConfigValue("version_url");
            string modDir = string.IsNullOrEmpty(gamePath) ? root : gamePath;
            if (!string.IsNullOrEmpty(versionUrl))
            {
                modUpdater = new ModUpdater(versionUrl, modDir);
            }
        }

        private void InitLauncherUpdater()
        {
            string launcherUrl = ReadConfigValue("launcher_version_url");
            if (!string.IsNullOrEmpty(launcherUrl))
            {
                launcherUpdater = new LauncherUpdater(launcherUrl, root);
            }
        }

        private string ReadConfigValue(string key)
        {
            try
            {
                if (File.Exists(updateConfigPath))
                {
                    string json = File.ReadAllText(updateConfigPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty(key, out var val))
                        return val.GetString() ?? "";
                }
            }
            catch { }
            return "";
        }

        private string GetGameRoot()
        {
            return string.IsNullOrEmpty(gamePath) ? root : gamePath;
        }

        private void btnStartPlain_Click(object sender, EventArgs e)
        {
            if (!ValidateGamePath()) return;

            if (Process.GetProcessesByName("H5_Game").Length > 0)
            {
                if (autoSaveActive)
                    this.Hide();
                else
                    Application.Exit();
                return;
            }

            string gameRoot = GetGameRoot();
            string binPath = Path.Combine(gameRoot, "bin");
            string gameExe = Path.Combine(binPath, "H5_Game.exe");
            StartGame(binPath, gameExe);
        }

        private bool ValidateGamePath()
        {
            string gameRoot = GetGameRoot();
            string gameExe = Path.Combine(gameRoot, "bin", "H5_Game.exe");
            if (!File.Exists(gameExe))
            {
                MessageBox.Show(
                    "Игра не найдена!\n\nУкажите путь к папке с игрой через кнопку \"Указать путь\".\n\nОжидается: " + gameExe,
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
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
            if (!ValidateGamePath()) return;

            string gameRoot = GetGameRoot();
            string dataPath = Path.Combine(gameRoot, "data");
            string mapsPath = Path.Combine(gameRoot, "maps");
            string binPath = Path.Combine(gameRoot, "bin");

            string modePak = Path.Combine(dataPath, "Mode_Modifier.pak");
            string tempPak = Path.Combine(gameRoot, "Mode_Modifier.pak");

            string gameExe = Path.Combine(binPath, "H5_Game.exe");

            string modSource = Path.Combine(gameRoot, "Chebovka1.5.2.pak");
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
                    // Пробуем скачать/обновить мод с Google Drive
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
                            SetStatus("Не удалось проверить обновления, используется локальная версия");
                        }

                        if (needsUpdate)
                        {
                            SetStatus("Скачивание мода...");
                            ShowTrainProgress(true);

                            var progress = new Progress<(long downloaded, long total)>(p =>
                            {
                                if (p.total > 0 && trainProgress != null)
                                {
                                    int pct = (int)(p.downloaded * 100 / p.total);
                                    trainProgress.Value = Math.Min(pct, 100);
                                    double mb = p.downloaded / (1024.0 * 1024.0);
                                    double totalMb = p.total / (1024.0 * 1024.0);
                                    SetStatus($"Скачивание: {mb:F1} / {totalMb:F1} МБ ({pct}%)");
                                }
                            });

                            (bool ok, string msg) = (false, "");
                            try
                            {
                                (ok, msg) = Task.Run(() => modUpdater.DownloadModAsync(progress, CancellationToken.None)).Result;
                            }
                            catch (Exception ex)
                            {
                                msg = ex.InnerException?.Message ?? ex.Message;
                            }

                            ShowTrainProgress(false);
                            SetStatus(ok ? msg : "Ошибка: " + msg);
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
                            string downloadedModFile = Path.Combine(gameRoot, remote.file_name);
                            if (File.Exists(downloadedModFile))
                                actualModSource = downloadedModFile;
                        }
                    }

                    if (!File.Exists(actualModSource))
                    {
                        MessageBox.Show(
                            "Мод не найден!\n\nПроверьте подключение к интернету или наличие файла мода.",
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
            string profilesPath = Path.Combine(GetGameRoot(), "UniverseTeam", "Universe Mod", "Profiles");

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

        private void ShowTrainProgress(bool visible)
        {
            if (trainProgress != null)
            {
                if (trainProgress.InvokeRequired)
                {
                    trainProgress.Invoke(() =>
                    {
                        trainProgress.Visible = visible;
                        trainProgress.Value = 0;
                        if (visible) trainProgress.StartAnimation();
                        else trainProgress.StopAnimation();
                    });
                }
                else
                {
                    trainProgress.Visible = visible;
                    trainProgress.Value = 0;
                    if (visible) trainProgress.StartAnimation();
                    else trainProgress.StopAnimation();
                }
            }
        }

        private async void CheckModUpdateOnLoad()
        {
            if (modUpdater == null)
            {
                SetStatus("Обновления не настроены (update_config.json)");
                return;
            }

            SetStatus("Проверка обновлений...");
            try
            {
                var remote = await modUpdater.GetRemoteVersionAsync();
                if (remote == null)
                {
                    SetStatus("Не удалось проверить обновления");
                    return;
                }

                string localVer = modUpdater.GetLocalVersion() ?? "не установлен";
                bool updateAvailable = await modUpdater.IsUpdateAvailableAsync();

                if (updateAvailable)
                    SetStatus($"Доступна новая версия мода: {remote.version} (текущая: {localVer})");
                else
                    SetStatus($"Мод {remote.mod_name} v{remote.version} — актуален");
            }
            catch
            {
                SetStatus("Ошибка проверки обновлений");
            }

            // Проверяем обновление лаунчера
            await CheckLauncherUpdateAsync();
        }

        private async Task CheckLauncherUpdateAsync()
        {
            if (launcherUpdater == null) return;

            try
            {
                bool hasUpdate = await launcherUpdater.IsUpdateAvailableAsync();
                if (hasUpdate && btnUpdateLauncher != null)
                {
                    var remote = await launcherUpdater.GetRemoteVersionAsync();
                    string ver = remote?.version ?? "?";
                    btnUpdateLauncher.Text = $"Обновить лаунчер (v{ver})";
                    btnUpdateLauncher.Visible = true;
                }
            }
            catch { }
        }

        private async void BtnUpdateLauncher_Click(object? sender, EventArgs e)
        {
            if (launcherUpdater == null) return;

            if (btnUpdateLauncher != null)
                btnUpdateLauncher.Enabled = false;

            SetStatus("Скачивание обновления лаунчера...");
            ShowTrainProgress(true);

            var progress = new Progress<(long downloaded, long total)>(p =>
            {
                if (p.total > 0 && trainProgress != null)
                {
                    int pct = (int)(p.downloaded * 100 / p.total);
                    trainProgress.Value = Math.Min(pct, 100);
                    double mb = p.downloaded / (1024.0 * 1024.0);
                    double totalMb = p.total / (1024.0 * 1024.0);
                    SetStatus($"Обновление лаунчера: {mb:F1} / {totalMb:F1} МБ ({pct}%)");
                }
            });

            var (ok, result) = await launcherUpdater.DownloadUpdateAsync(progress);

            ShowTrainProgress(false);

            if (ok)
            {
                SetStatus("Перезапуск лаунчера...");
                try
                {
                    launcherUpdater.LaunchUpdaterAndExit(result);
                }
                catch (Exception ex)
                {
                    SetStatus("Ошибка: " + ex.Message);
                    if (btnUpdateLauncher != null) btnUpdateLauncher.Enabled = true;
                }
            }
            else
            {
                SetStatus("Ошибка обновления: " + result);
                if (btnUpdateLauncher != null) btnUpdateLauncher.Enabled = true;
            }
        }

        private void BtnSetGamePath_Click(object? sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog();
            dialog.Description = "Выберите папку с игрой (где находится папка bin с H5_Game.exe)";
            dialog.UseDescriptionForTitle = true;

            if (!string.IsNullOrEmpty(gamePath) && Directory.Exists(gamePath))
                dialog.SelectedPath = gamePath;

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                string selected = dialog.SelectedPath;
                string testExe = Path.Combine(selected, "bin", "H5_Game.exe");
                if (File.Exists(testExe))
                {
                    SaveGamePath(selected);
                    InitModUpdater();
                    SetStatus($"Путь к игре: {selected}");
                }
                else
                {
                    MessageBox.Show(
                        "В выбранной папке не найден bin\\H5_Game.exe.\nВыберите корневую папку игры.",
                        "Неверная папка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
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
            this.ClientSize = new Size(600, 530);
            this.Text = "ModLauncher";

            // ОСНОВНАЯ ПАНЕЛЬ (с двойной буферизацией)
            Panel panel = new DoubleBufferedPanel();
            panel.Size = new Size(500, 400);
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
            int startY = 145;

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

            // КНОПКА УКАЗАТЬ ПУТЬ
            Button btnSetPath = new Button();
            btnSetPath.Parent = panel;
            btnSetPath.Text = "Указать путь";
            btnSetPath.FlatStyle = FlatStyle.Flat;
            btnSetPath.FlatAppearance.BorderSize = 0;
            btnSetPath.BackColor = Color.FromArgb(40, 40, 60);
            btnSetPath.ForeColor = Color.White;
            btnSetPath.Font = new Font("Segoe UI", 9, FontStyle.Regular);
            btnSetPath.Size = new Size(100, 28);
            btnSetPath.Location = new Point(panel.Width - 115, 25);
            btnSetPath.Click += BtnSetGamePath_Click;

            // КНОПКА ОБНОВИТЬ ЛАУНЧЕР (скрыта по умолчанию)
            btnUpdateLauncher = new Button();
            btnUpdateLauncher.Parent = panel;
            btnUpdateLauncher.Text = "Обновить лаунчер";
            btnUpdateLauncher.FlatStyle = FlatStyle.Flat;
            btnUpdateLauncher.FlatAppearance.BorderSize = 1;
            btnUpdateLauncher.FlatAppearance.BorderColor = Color.FromArgb(255, 200, 50);
            btnUpdateLauncher.BackColor = Color.FromArgb(120, 80, 0);
            btnUpdateLauncher.ForeColor = Color.FromArgb(255, 230, 100);
            btnUpdateLauncher.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            btnUpdateLauncher.Size = new Size(panel.Width - 80, 35);
            btnUpdateLauncher.Location = new Point(40, startY + (btnH + gapY) * 2 + 5);
            btnUpdateLauncher.Visible = false;
            btnUpdateLauncher.Click += BtnUpdateLauncher_Click;

            // СТАТУС ОБНОВЛЕНИЯ МОДА (под заголовком)
            statusLabel = new Label();
            statusLabel.Parent = panel;
            statusLabel.Text = "";
            statusLabel.ForeColor = Color.FromArgb(220, 200, 220, 255);
            statusLabel.BackColor = Color.Transparent;
            statusLabel.Font = new Font("Segoe UI", 9, FontStyle.Regular);
            statusLabel.Size = new Size(panel.Width - 40, 20);
            statusLabel.Location = new Point(20, 65);
            statusLabel.TextAlign = ContentAlignment.MiddleCenter;

            // АНИМИРОВАННЫЙ ПРОГРЕСС-БАР (между статусом и кнопками)
            trainProgress = new TrainProgressBar();
            trainProgress.Parent = panel;
            trainProgress.Size = new Size(panel.Width - 60, 40);
            trainProgress.Location = new Point(30, 90);
            trainProgress.Visible = false;
            trainProgress.BackColor = Color.FromArgb(20, 20, 30);

            // Привязываем события наведения
            SetHoverHint(btnStartPlain, "Обычный запуск игры без каких-либо изменений");
            SetHoverHint(btnStart, "Запуск с обновлением архивов и файлов мода");
            SetHoverHint(btnStartMod, "Проверяет обновления мода и запускает игру");
            SetHoverHint(btnAutoSave, "Мониторинг и копирование автосохранений игры");
            SetHoverHint(btnSetPath, "Указать папку с игрой");

            // Показываем текущий путь к игре
            if (!string.IsNullOrEmpty(gamePath))
                SetStatus($"Игра: {gamePath}");

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
