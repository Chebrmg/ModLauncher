using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Windows.Forms;

namespace Updater
{
    internal static class Program
    {
        /// <summary>
        /// Updater.exe — заменяет файлы лаунчера и перезапускает его.
        /// 
        /// Аргументы:
        ///   --pid {PID}         PID лаунчера (ждём завершения)
        ///   --zip {path}        Путь к ZIP-архиву с обновлением
        ///   --target {path}     Папка лаунчера (куда копировать файлы)
        ///   --launcher {name}   Имя exe лаунчера для перезапуска
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            string? zipPath = null;
            string? targetDir = null;
            string? launcherExe = null;
            int pid = 0;

            for (int i = 0; i < args.Length - 1; i++)
            {
                switch (args[i].ToLower())
                {
                    case "--pid": int.TryParse(args[i + 1], out pid); i++; break;
                    case "--zip": zipPath = args[i + 1]; i++; break;
                    case "--target": targetDir = args[i + 1]; i++; break;
                    case "--launcher": launcherExe = args[i + 1]; i++; break;
                }
            }

            // Логируем аргументы для отладки
            string debugLog = $"args.Length={args.Length}\n";
            for (int i = 0; i < args.Length; i++)
                debugLog += $"  args[{i}]=\"{args[i]}\"\n";
            debugLog += $"\nzip={zipPath}\ntarget={targetDir}\nlauncher={launcherExe}\npid={pid}";

            try
            {
                string logPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "updater_debug.log");
                File.WriteAllText(logPath, debugLog);
            }
            catch { }

            if (zipPath == null || targetDir == null || launcherExe == null)
            {
                MessageBox.Show(
                    "Ошибка: недостаточно параметров.\n\nИспользование:\nUpdater.exe --pid PID --zip archive.zip --target folder --launcher Launcher.exe",
                    "Updater", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                // Ждём завершения лаунчера
                if (pid > 0)
                {
                    try
                    {
                        var proc = Process.GetProcessById(pid);
                        proc.WaitForExit(10000);
                    }
                    catch { }

                    // Дополнительная пауза
                    Thread.Sleep(1000);
                }

                // Распаковываем ZIP
                string tempExtract = Path.Combine(targetDir, "_update_temp");
                if (Directory.Exists(tempExtract))
                    Directory.Delete(tempExtract, true);

                ZipFile.ExtractToDirectory(zipPath, tempExtract);

                // Ищем корневую папку внутри архива (может быть вложена)
                string sourceDir = tempExtract;
                var dirs = Directory.GetDirectories(tempExtract);
                if (dirs.Length == 1 && Directory.GetFiles(tempExtract).Length == 0)
                    sourceDir = dirs[0];

                // Копируем файлы
                CopyDirectory(sourceDir, targetDir);

                // Удаляем временные файлы
                try
                {
                    Directory.Delete(tempExtract, true);
                    File.Delete(zipPath);
                }
                catch { }

                // Перезапускаем лаунчер
                string launcherPath = Path.Combine(targetDir, launcherExe);
                if (File.Exists(launcherPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = launcherPath,
                        WorkingDirectory = targetDir,
                        UseShellExecute = true
                    });
                }

            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Ошибка обновления:\n" + ex.Message,
                    "Updater", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static void CopyDirectory(string sourceDir, string targetDir)
        {
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(targetDir, Path.GetFileName(file));

                // Не заменяем Updater.exe пока он работает
                if (Path.GetFileName(file).Equals("Updater.exe", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    File.Copy(file, destFile, true);
                }
                catch
                {
                    // Файл занят — пропускаем
                }
            }

            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                string destDir = Path.Combine(targetDir, Path.GetFileName(dir));
                Directory.CreateDirectory(destDir);
                CopyDirectory(dir, destDir);
            }
        }
    }
}
