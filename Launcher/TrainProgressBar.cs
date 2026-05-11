using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Launcher
{
    /// <summary>
    /// Кастомный прогресс-бар с анимацией: поезд догоняет бегущего человека.
    /// Человек бежит впереди, поезд движется за ним по мере прогресса.
    /// </summary>
    public class TrainProgressBar : Control
    {
        private int _value = 0;
        private int _maximum = 100;
        private readonly System.Windows.Forms.Timer _animTimer;
        private int _runnerFrame = 0;
        private int _trainWheelFrame = 0;
        private int _smokeOffset = 0;

        public TrainProgressBar()
        {
            this.DoubleBuffered = true;
            this.SetStyle(
                ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint, true);

            _animTimer = new System.Windows.Forms.Timer();
            _animTimer.Interval = 120;
            _animTimer.Tick += (s, e) =>
            {
                _runnerFrame = (_runnerFrame + 1) % 4;
                _trainWheelFrame = (_trainWheelFrame + 1) % 3;
                _smokeOffset = (_smokeOffset + 1) % 6;
                Invalidate();
            };
        }

        public int Value
        {
            get => _value;
            set
            {
                _value = Math.Max(0, Math.Min(value, _maximum));
                Invalidate();
            }
        }

        public int Maximum
        {
            get => _maximum;
            set { _maximum = Math.Max(1, value); Invalidate(); }
        }

        public void StartAnimation() => _animTimer.Start();
        public void StopAnimation() => _animTimer.Stop();

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = this.Width;
            int h = this.Height;

            // Фон — рельсы
            DrawRails(g, w, h);

            float progress = (float)_value / _maximum;
            int trainX = (int)((w - 60) * progress);
            int runnerX = trainX + 55 + (int)((w - trainX - 80) * 0.3f);

            // Ограничиваем позицию бегущего
            if (runnerX > w - 25) runnerX = w - 25;
            if (runnerX < trainX + 45) runnerX = trainX + 45;

            // При 100% — поезд догнал
            if (progress >= 0.99f)
            {
                runnerX = trainX + 50;
            }

            // Рисуем дым от поезда
            DrawSmoke(g, trainX + 5, h / 2 - 18);

            // Рисуем поезд
            DrawTrain(g, trainX, h / 2 - 8);

            // Рисуем бегущего человека
            DrawRunner(g, runnerX, h / 2 - 14);

            // Процент
            string pctText = $"{_value}%";
            using var font = new Font("Segoe UI", 8, FontStyle.Bold);
            var textSize = g.MeasureString(pctText, font);
            float textX = (w - textSize.Width) / 2;
            float textY = h - textSize.Height - 1;
            g.DrawString(pctText, font, Brushes.White, textX, textY);
        }

        private void DrawRails(Graphics g, int w, int h)
        {
            int railY = h / 2 + 6;

            // Земля
            using var groundBrush = new SolidBrush(Color.FromArgb(60, 40, 20));
            g.FillRectangle(groundBrush, 0, railY + 2, w, h - railY - 2);

            // Рельсы
            using var railPen = new Pen(Color.FromArgb(140, 140, 140), 2);
            g.DrawLine(railPen, 0, railY, w, railY);
            g.DrawLine(railPen, 0, railY + 4, w, railY + 4);

            // Шпалы
            using var sleeperPen = new Pen(Color.FromArgb(100, 70, 40), 2);
            for (int x = 0; x < w; x += 12)
            {
                g.DrawLine(sleeperPen, x, railY - 1, x, railY + 5);
            }
        }

        private void DrawSmoke(Graphics g, int x, int y)
        {
            int alpha = 80;
            for (int i = 0; i < 3; i++)
            {
                int ox = -i * 8 - (_smokeOffset * 2);
                int oy = -i * 5 - _smokeOffset;
                int size = 6 + i * 3;
                using var smokeBrush = new SolidBrush(Color.FromArgb(alpha, 200, 200, 200));
                g.FillEllipse(smokeBrush, x + ox, y + oy, size, size);
                alpha -= 20;
            }
        }

        private void DrawTrain(Graphics g, int x, int y)
        {
            // Корпус
            using var bodyBrush = new SolidBrush(Color.FromArgb(180, 30, 30));
            g.FillRectangle(bodyBrush, x, y, 40, 16);

            // Кабина
            using var cabBrush = new SolidBrush(Color.FromArgb(220, 50, 50));
            g.FillRectangle(cabBrush, x + 28, y - 6, 14, 22);

            // Труба
            using var chimBrush = new SolidBrush(Color.FromArgb(60, 60, 60));
            g.FillRectangle(chimBrush, x + 4, y - 8, 6, 8);

            // Окно кабины
            using var winBrush = new SolidBrush(Color.FromArgb(180, 220, 255));
            g.FillRectangle(winBrush, x + 31, y - 3, 8, 6);

            // Колёса (анимация вращения)
            DrawWheel(g, x + 8, y + 14);
            DrawWheel(g, x + 22, y + 14);
            DrawWheel(g, x + 36, y + 14);
        }

        private void DrawWheel(Graphics g, int cx, int cy)
        {
            int r = 4;
            using var wheelBrush = new SolidBrush(Color.FromArgb(40, 40, 40));
            g.FillEllipse(wheelBrush, cx - r, cy - r, r * 2, r * 2);

            using var axlePen = new Pen(Color.FromArgb(160, 160, 160), 1);
            double angle = _trainWheelFrame * Math.PI * 2 / 3;
            int sx = cx + (int)(Math.Cos(angle) * (r - 1));
            int sy = cy + (int)(Math.Sin(angle) * (r - 1));
            g.DrawLine(axlePen, cx, cy, sx, sy);
        }

        private void DrawRunner(Graphics g, int x, int y)
        {
            using var bodyPen = new Pen(Color.White, 2);
            using var headBrush = new SolidBrush(Color.FromArgb(255, 220, 180));

            // Голова
            g.FillEllipse(headBrush, x - 3, y, 8, 8);

            // Тело
            g.DrawLine(bodyPen, x + 1, y + 8, x + 1, y + 18);

            // Руки и ноги — анимация бега
            int frame = _runnerFrame;
            switch (frame)
            {
                case 0:
                    g.DrawLine(bodyPen, x + 1, y + 11, x - 5, y + 15); // левая рука назад
                    g.DrawLine(bodyPen, x + 1, y + 11, x + 7, y + 14); // правая рука вперёд
                    g.DrawLine(bodyPen, x + 1, y + 18, x - 3, y + 26); // левая нога назад
                    g.DrawLine(bodyPen, x + 1, y + 18, x + 6, y + 25); // правая нога вперёд
                    break;
                case 1:
                    g.DrawLine(bodyPen, x + 1, y + 11, x - 2, y + 16);
                    g.DrawLine(bodyPen, x + 1, y + 11, x + 4, y + 16);
                    g.DrawLine(bodyPen, x + 1, y + 18, x, y + 26);
                    g.DrawLine(bodyPen, x + 1, y + 18, x + 3, y + 26);
                    break;
                case 2:
                    g.DrawLine(bodyPen, x + 1, y + 11, x + 7, y + 15); // левая рука вперёд
                    g.DrawLine(bodyPen, x + 1, y + 11, x - 5, y + 14); // правая рука назад
                    g.DrawLine(bodyPen, x + 1, y + 18, x + 6, y + 25); // левая нога вперёд
                    g.DrawLine(bodyPen, x + 1, y + 18, x - 3, y + 26); // правая нога назад
                    break;
                case 3:
                    g.DrawLine(bodyPen, x + 1, y + 11, x + 4, y + 16);
                    g.DrawLine(bodyPen, x + 1, y + 11, x - 2, y + 16);
                    g.DrawLine(bodyPen, x + 1, y + 18, x + 3, y + 26);
                    g.DrawLine(bodyPen, x + 1, y + 18, x, y + 26);
                    break;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animTimer.Stop();
                _animTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
