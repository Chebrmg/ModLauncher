using System;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace Launcher
{
    public partial class Form1 : Form
    {
        string root = AppDomain.CurrentDomain.BaseDirectory;

        public Form1()
        {
            InitializeComponent();
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
                // 1️⃣ Копируем только Mode_Modifier
                // 🟢 Mode_Modifier (НЕ КРИТИЧЕСКИЙ, лаунчер не падает)
if (File.Exists(modePak))
{
    try
    {
        File.Copy(modePak, tempPak, true);

        KillProcess("H5_Game");

        File.Copy(tempPak, modePak, true);

        if (File.Exists(tempPak))
            File.Delete(tempPak);
    }
    catch (Exception ex)
    {
        // ❗ просто лог, без краша
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
    // 🟢 файла нет — это НЕ ошибка
    Console.WriteLine("Mode_Modifier.pak отсутствует, пропуск");
}

                // 4️⃣ 🔥 Обновляем ВСЕ архивы
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

                // 5️⃣ Если режим мода
                if (useMod)
                {
                    // ❗ Chebovka — КРИТИЧЕСКИЙ файл
                    if (!File.Exists(modSource))
                    {
                        MessageBox.Show(
                            "❌ Критическая ошибка:\nChebovka1.5.2.pak не найден!",
                            "Ошибка",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error
                        );

                        // 🔥 завершение лаунчера
                        Application.Exit();
                        return;
                    }

                    try
                    {
                        File.Copy(modSource, modTarget, true);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            "❌ Ошибка установки мода:\n" + ex.Message,
                            "Ошибка",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error
                        );

                        Application.Exit();
                        return;
                    }
                }

                // 6️⃣ Запуск
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

                // 🔥 закрываем лаунчер после запуска игры
                Application.Exit();
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

                    // 🔥 ДОБАВЛЯЕМ ФЛАГ
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

        private void Form1_Load(object sender, EventArgs e)
        {
            // 🖼 ФОН
            this.BackgroundImage = Image.FromFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bg.jpg"));
            this.BackgroundImageLayout = ImageLayout.Stretch;

            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.Black;

            // 🧱 ПАНЕЛЬ (стеклянный блок)
            Panel panel = new Panel();
            panel.Size = new Size(500, 300);
            panel.Location = new Point(50, 50);
            panel.BackColor = Color.FromArgb(150, 0, 0, 0);
            this.Controls.Add(panel);

            // 📝 ЗАГОЛОВОК
            Label title = new Label();
            title.Text = "GAME LAUNCHER";
            title.ForeColor = Color.White;
            title.Font = new Font("Segoe UI", 18, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(20, 20);
            panel.Controls.Add(title);

            // 📝 СТАТУС
            Label status = new Label();
            status.Text = "Status: Ready";
            status.ForeColor = Color.White;
            status.Font = new Font("Segoe UI", 10);
            status.AutoSize = true;
            status.Location = new Point(20, 70);
            panel.Controls.Add(status);

            // 🔘 КНОПКИ СТИЛЬ
            btnStart.FlatStyle = FlatStyle.Flat;
            btnStart.BackColor = Color.FromArgb(40, 40, 40);
            btnStart.ForeColor = Color.White;
            btnStart.FlatAppearance.BorderSize = 0;
            btnStart.Size = new Size(150, 40);
            btnStart.Location = new Point(20, 200);

            btnStartMod.FlatStyle = FlatStyle.Flat;
            btnStartMod.BackColor = Color.FromArgb(70, 20, 20);
            btnStartMod.ForeColor = Color.White;
            btnStartMod.FlatAppearance.BorderSize = 0;
            btnStartMod.Size = new Size(150, 40);
            btnStartMod.Location = new Point(200, 200);

            // 📦 КНОПКИ ВНУТРЬ ПАНЕЛИ
            panel.Controls.Add(btnStart);
            panel.Controls.Add(btnStartMod);
        }
    }
}