// Комментарии всегда выносятся в отдельные строки.

using System;
using System.IO;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.Media;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WOW_launcher
{
    public partial class Form1 : Form
    {
        private readonly string baseDir;
        private SoundPlayer soundPlayer;
        private bool isMusicPlaying = true;
        private Button btnMusicToggle;
        private Button btnRunLastConfig;

        // Шрифты WoW и системный шрифт для настроек
        private PrivateFontCollection pfc = new PrivateFontCollection();
        private Font wowFontTitle;
        private Font wowFontButton;
        private Font systemFontSettings;
        private Font systemFontHeader;

        // Кастомный курсор
        private Cursor customCursor;
        private const int WM_SETCURSOR = 0x0020;
        private const int WM_ENTERSIZEMOVE = 0x0231;
        private const int WM_EXITSIZEMOVE = 0x0232;

        // Импорт функции Win32 API для загрузки полноцветных курсоров
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadCursorFromFile(string path);

        // Импорт функции MCI для независимого воспроизведения кликов поверх музыки
        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        private static extern int mciSendString(string lpstrCommand, string lpstrReturnString, int uReturnLength, IntPtr hwndCallback);

        private string tempClickPath;

        // Поля для плавного параллакс-эффекта фона
        private Image bgImage;
        private PointF currentParallax = new PointF(0, 0);
        private PointF targetParallax = new PointF(0, 0);
        private const int ParallaxMargin = 18;

        // Состояние меню (true = главное меню, false = конструктор)
        private bool isMainMode = true;

        // Элементы меню и расширенные чекбоксы кастомной сборки
        private List<Control> mainMenuControls = new List<Control>();
        private List<Control> builderMenuControls = new List<Control>();

        private CheckBox chkVulkan;
        private CheckBox chkReshade;
        private CheckBox chkVulkanReshade;
        private CheckBox chkCleanCache;

        // Словарь всех чекбоксов HD-модификаций
        private Dictionary<string, CheckBox> hdCheckBoxes = new Dictionary<string, CheckBox>();

        private readonly string configFilePath;

        // Структура и список снежинок
        private class Snowflake
        {
            public float X { get; set; }
            public float Y { get; set; }
            public float Size { get; set; }
            public float SpeedY { get; set; }
            public float SpeedX { get; set; }
            public int Alpha { get; set; }
        }

        private List<Snowflake> snowflakes = new List<Snowflake>();
        private Timer snowTimer;
        private Random random = new Random();

        public Form1()
        {
            baseDir = AppDomain.CurrentDomain.BaseDirectory;
            configFilePath = Path.Combine(baseDir, "launcher_config.txt");

            // Создаем временный файл для независимого проигрывания клика через MCI
            ExtractTempClickFile();

            // Начальный размер окна 1296x768 с возможностью изменять размер
            this.Text = "WowLK 3.3.5a by ApodeZ";
            this.Size = new Size(1296, 768);
            this.MinimumSize = new Size(1024, 700);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;

            // Установка иконки приложения
            try
            {
                if (Properties.Resources.wow != null)
                {
                    using (MemoryStream ms = new MemoryStream(Properties.Resources.wow))
                    {
                        this.Icon = new Icon(ms);
                    }
                }
            }
            catch
            {
                // Игнорируем ошибки иконки
            }

            // Установка кастомного цветного курсора из ресурсов проекта
            ApplyWarcraftCursor();

            // Включаем двойную буферизацию
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            this.UpdateStyles();

            // Инициализация шрифтов
            InitWoWFonts();

            // Загрузка фона
            try
            {
                if (Properties.Resources.bg != null)
                {
                    bgImage = Properties.Resources.bg;
                }
            }
            catch
            {
                this.BackColor = Color.FromArgb(15, 23, 42);
            }

            this.MouseMove += Form1_MouseMove;
            this.Resize += Form1_Resize;

            // Заголовок окна (желтый)
            Label title = new Label
            {
                Text = "WowLK 3.3.5a by ApodeZ",
                Font = wowFontTitle,
                ForeColor = Color.FromArgb(248, 183, 0),
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(54, 42),
                UseCompatibleTextRendering = true
            };
            this.Controls.Add(title);

            // Кнопка музыки в правом верхнем углу
            btnMusicToggle = new Button
            {
                Text = "Музыка: [ON]",
                Font = wowFontButton,
                ForeColor = Color.FromArgb(240, 225, 180),
                BackColor = Color.FromArgb(200, 15, 23, 42),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(210, 52),
                Location = new Point(this.ClientSize.Width - 265, 42),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Cursor = customCursor ?? Cursors.Hand,
                UseCompatibleTextRendering = true
            };
            btnMusicToggle.FlatAppearance.BorderSize = 1;
            btnMusicToggle.FlatAppearance.BorderColor = Color.FromArgb(190, 150, 90);
            btnMusicToggle.Click += (s, e) => {
                PlayClickSound();
                ToggleMusic(s, e);
            };
            this.Controls.Add(btnMusicToggle);

            // Инициализация интерфейса меню
            InitMenus();

            // Загрузка сохраненных настроек
            LoadConfig();

            // Запуск снега и музыки
            InitSnow();
            PlayBackgroundMusic();
        }

        private void Form1_Resize(object sender, EventArgs e)
        {
            this.Invalidate();
        }

        private void ExtractTempClickFile()
        {
            try
            {
                UnmanagedMemoryStream clickStream = Properties.Resources.click;
                if (clickStream != null)
                {
                    tempClickPath = Path.Combine(Path.GetTempPath(), "wow_click_temp.wav");
                    byte[] buffer = new byte[clickStream.Length];
                    clickStream.Read(buffer, 0, (int)clickStream.Length);
                    File.WriteAllBytes(tempClickPath, buffer);
                }
            }
            catch
            {
                tempClickPath = null;
            }
        }

        private void PlayClickSound()
        {
            try
            {
                if (!string.IsNullOrEmpty(tempClickPath) && File.Exists(tempClickPath))
                {
                    mciSendString("close ClickSound", null, 0, IntPtr.Zero);
                    mciSendString($"open \"{tempClickPath}\" type waveaudio alias ClickSound", null, 0, IntPtr.Zero);
                    mciSendString("play ClickSound", null, 0, IntPtr.Zero);
                }
            }
            catch
            {
                // Игнорируем ошибки клика
            }
        }

        private void ApplyWarcraftCursor()
        {
            try
            {
                byte[] curBytes = Properties.Resources.wow_cur;
                if (curBytes != null && curBytes.Length > 0)
                {
                    string tempCurPath = Path.Combine(Path.GetTempPath(), "wow_cursor_temp.cur");
                    File.WriteAllBytes(tempCurPath, curBytes);

                    IntPtr hCursor = LoadCursorFromFile(tempCurPath);
                    if (hCursor != IntPtr.Zero)
                    {
                        customCursor = new Cursor(hCursor);
                        this.Cursor = customCursor;
                    }
                }
            }
            catch
            {
                // Системный курсор по умолчанию
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_SETCURSOR && customCursor != null)
            {
                Cursor.Current = customCursor;
                m.Result = (IntPtr)1;
                return;
            }

            if (m.Msg == WM_ENTERSIZEMOVE)
            {
                if (snowTimer != null) snowTimer.Stop();
            }
            else if (m.Msg == WM_EXITSIZEMOVE)
            {
                if (snowTimer != null && isMainMode) snowTimer.Start();
            }

            base.WndProc(ref m);
        }

        private void InitWoWFonts()
        {
            try
            {
                byte[] fontData = Properties.Resources.ofont_ru_Friz_Quadrata;
                IntPtr fontPtr = Marshal.AllocCoTaskMem(fontData.Length);
                Marshal.Copy(fontData, 0, fontPtr, fontData.Length);
                pfc.AddMemoryFont(fontPtr, fontData.Length);
                Marshal.FreeCoTaskMem(fontPtr);

                wowFontTitle = new Font(pfc.Families[0], 29, FontStyle.Bold);
                wowFontButton = new Font(pfc.Families[0], 14, FontStyle.Bold);
            }
            catch
            {
                wowFontTitle = new Font("Georgia", 26, FontStyle.Bold);
                wowFontButton = new Font("Georgia", 14, FontStyle.Bold);
            }

            systemFontSettings = new Font("Segoe UI", 11f, FontStyle.Regular);
            systemFontHeader = new Font("Segoe UI", 12f, FontStyle.Bold);
        }

        private void InitMenus()
        {
            // --- Главное меню ---
            Button btnMode1 = CreateMenuButton("1. Классика (DirectX 9)", 168, (s, e) => {
                PlayClickSound();
                RunCustomBuild(false, false, false, new Dictionary<string, bool>(), true);
            });
            Button btnMode2 = CreateMenuButton("2. Vulkan edition", 264, (s, e) => {
                PlayClickSound();
                RunCustomBuild(true, false, false, new Dictionary<string, bool>(), true);
            });

            Button btnMode3 = CreateMenuButton("3. Сборка от ApodeZ", 360, (s, e) => {
                PlayClickSound();
                var apodezMods = new Dictionary<string, bool>
                {
                    { "A", true }, { "E", true }, { "I", true }, { "M", true },
                    { "S", true }, { "U", true }, { "W", true }, { "Z", true },
                    { "H", true }, { "L", true }, { "R", true }, { "T", true }
                };
                RunCustomBuild(false, false, true, apodezMods, true);
            });

            Button btnCustomMenu = CreateMenuButton("Конструктор сборки", 456, (s, e) => {
                PlayClickSound();
                ToggleMenu(false);
            });
            btnCustomMenu.FlatAppearance.BorderColor = Color.FromArgb(56, 189, 248);

            // Кнопка поддержать проект (Donatty) в левом нижнем углу
            Button btnDonate = new Button
            {
                Text = "☕ Поддержать проект",
                Font = wowFontButton,
                ForeColor = Color.FromArgb(210, 190, 140),
                BackColor = Color.FromArgb(150, 15, 23, 42),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(260, 62),
                Location = new Point(54, 624),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                Cursor = customCursor ?? Cursors.Hand,
                UseCompatibleTextRendering = true
            };
            btnDonate.FlatAppearance.BorderSize = 1;
            btnDonate.FlatAppearance.BorderColor = Color.FromArgb(140, 110, 70);

            btnDonate.MouseEnter += (s, e) => {
                btnDonate.BackColor = Color.FromArgb(200, 25, 35, 60);
                btnDonate.ForeColor = Color.FromArgb(248, 183, 0);
            };
            btnDonate.MouseLeave += (s, e) => {
                btnDonate.BackColor = Color.FromArgb(150, 15, 23, 42);
                btnDonate.ForeColor = Color.FromArgb(210, 190, 140);
            };

            btnDonate.Click += (s, e) => {
                PlayClickSound();
                try
                {
                    Process.Start(new ProcessStartInfo("https://donatty.com/apodez") { UseShellExecute = true });
                }
                catch
                {
                    MessageBox.Show("Не удалось открыть ссылку на Donatty в браузере.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            // Кнопка GitHub рядом с донатом (левый нижний угол)
            Button btnGitHub = new Button
            {
                Text = "GitHub",
                Font = wowFontButton,
                ForeColor = Color.FromArgb(210, 190, 140),
                BackColor = Color.FromArgb(150, 15, 23, 42),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(180, 62),
                Location = new Point(324, 624),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                Cursor = customCursor ?? Cursors.Hand,
                UseCompatibleTextRendering = true
            };
            btnGitHub.FlatAppearance.BorderSize = 1;
            btnGitHub.FlatAppearance.BorderColor = Color.FromArgb(140, 110, 70);

            btnGitHub.MouseEnter += (s, e) => {
                btnGitHub.BackColor = Color.FromArgb(200, 25, 35, 60);
                btnGitHub.ForeColor = Color.FromArgb(248, 183, 0);
            };
            btnGitHub.MouseLeave += (s, e) => {
                btnGitHub.BackColor = Color.FromArgb(150, 15, 23, 42);
                btnGitHub.ForeColor = Color.FromArgb(210, 190, 140);
            };

            btnGitHub.Click += (s, e) => {
                PlayClickSound();
                try
                {
                    Process.Start(new ProcessStartInfo("https://github.com/Apodez-dev/WowLK-3.3.5-launcher") { UseShellExecute = true });
                }
                catch
                {
                    MessageBox.Show("Не удалось открыть ссылку на GitHub в браузере.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            // Кнопка запуска последней конфигурации (в правом нижнем углу)
            btnRunLastConfig = new Button
            {
                Text = File.Exists(configFilePath) ? "⚡ Запустить последнюю конфигурацию" : "▶ Играть",
                Font = wowFontButton,
                ForeColor = Color.FromArgb(240, 225, 180),
                BackColor = Color.FromArgb(200, 15, 23, 42),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(468, 62),
                Location = new Point(756, 624),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Cursor = customCursor ?? Cursors.Hand,
                UseCompatibleTextRendering = true
            };
            btnRunLastConfig.FlatAppearance.BorderSize = 1;
            btnRunLastConfig.FlatAppearance.BorderColor = Color.FromArgb(80, 200, 120);
            btnRunLastConfig.Click += (s, e) => {
                PlayClickSound();
                RunSavedConfig();
            };

            mainMenuControls.Add(btnMode1);
            mainMenuControls.Add(btnMode2);
            mainMenuControls.Add(btnMode3);
            mainMenuControls.Add(btnCustomMenu);
            mainMenuControls.Add(btnDonate);
            mainMenuControls.Add(btnGitHub);
            mainMenuControls.Add(btnRunLastConfig);

            foreach (var ctrl in mainMenuControls)
                this.Controls.Add(ctrl);

            // --- Меню конструктора кастомной сборки ---
            Label lblBuilderTitle = new Label
            {
                Text = "Конфигуратор пользовательской сборки",
                Font = wowFontButton,
                ForeColor = Color.FromArgb(248, 183, 0),
                BackColor = Color.Transparent,
                AutoSize = true,
                UseCompatibleTextRendering = true
            };
            lblBuilderTitle.HandleCreated += (s, e) => {
                lblBuilderTitle.Location = new Point(48 + (1200 - lblBuilderTitle.Width) / 2, 125);
            };
            builderMenuControls.Add(lblBuilderTitle);

            // РАЗДЕЛ 1: Рендер и дополнительные опции
            Label lblSection1 = new Label
            {
                Text = "Рендер и дополнительные опции",
                Font = systemFontHeader,
                ForeColor = Color.FromArgb(248, 183, 0),
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(65, 160),
                UseCompatibleTextRendering = true
            };
            builderMenuControls.Add(lblSection1);

            chkVulkan = CreateSystemCheckBox("Включить Vulkan (DXVK рендерер)", 65, 195);
            chkReshade = CreateSystemCheckBox("Включить ReShade (старый режим)", 430, 195);
            chkVulkanReshade = CreateSystemCheckBox("ReShade + Vulkan (работает только через лаунчер)", 65, 230);
            chkCleanCache = CreateSystemCheckBox("Автоматически очищать папку Cache", 840, 195);

            builderMenuControls.Add(chkVulkan);
            builderMenuControls.Add(chkReshade);
            builderMenuControls.Add(chkVulkanReshade);
            builderMenuControls.Add(chkCleanCache);

            chkReshade.CheckedChanged += (s, e) => {
                if (chkReshade.Checked)
                {
                    chkVulkan.Checked = false;
                    chkVulkan.Enabled = false;
                    chkVulkanReshade.Checked = false;
                    chkVulkanReshade.Enabled = false;
                }
                else
                {
                    chkVulkan.Enabled = true;
                    chkVulkanReshade.Enabled = true;
                }
            };

            chkVulkanReshade.CheckedChanged += (s, e) => {
                if (chkVulkanReshade.Checked)
                {
                    chkVulkan.Checked = false;
                    chkVulkan.Enabled = false;
                    chkReshade.Checked = false;
                    chkReshade.Enabled = false;
                }
                else
                {
                    chkVulkan.Enabled = true;
                    chkReshade.Enabled = true;
                }
            };

            // РАЗДЕЛ 2: Настройки текстур и анимаций
            Label lblSection2 = new Label
            {
                Text = "Настройки текстур и анимаций",
                Font = systemFontHeader,
                ForeColor = Color.FromArgb(248, 183, 0),
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(65, 270),
                UseCompatibleTextRendering = true
            };
            builderMenuControls.Add(lblSection2);

            var modDescriptions = new List<Tuple<string, string>>
            {
                Tuple.Create("A", "HD модели существ и маунтов [CANON] (patch-ruRU-A)"),
                Tuple.Create("B", "HD модели существ и маунтов [UNCANON] (patch-ruRU-B)"),
                Tuple.Create("E", "Текстуры экипировки (patch-ruRU-E)"),
                Tuple.Create("H", "HD модели персонажей (patch-ruRU-H)"),
                Tuple.Create("I", "Оружие и броня (patch-ruRU-I)"),
                Tuple.Create("L", "Загрузочные меню и фоны (patch-ruRU-L)"),
                Tuple.Create("M", "Карты классических подземелий (patch-ruRU-M)"),
                Tuple.Create("R", "Каноничные фоны загрузок (patch-ruRU-R)"),
                Tuple.Create("S", "Заклинания и способности (patch-ruRU-S)"),
                Tuple.Create("T", "HD текстуры мира (patch-ruRU-T)"),
                Tuple.Create("U", "Текстуры полей сражения (patch-ruRU-U)"),
                Tuple.Create("V", "Заклинания Кодекс Зератта [Чернокнижник] (patch-ruRU-V)"),
                Tuple.Create("W", "Замена анимации тринькета (patch-ruRU-W)"),
                Tuple.Create("X", "Обновленные ловушки охотника (patch-ruRU-X)"),
                Tuple.Create("Z", "HD Вода (patch-ruRU-Z)")
            };

            int startTop = 305;
            int stepY = 32;

            for (int i = 0; i < modDescriptions.Count; i++)
            {
                string tag = modDescriptions[i].Item1;
                string desc = modDescriptions[i].Item2;

                int col = i % 2;
                int row = i / 2;

                int posX = col == 0 ? 65 : 624;
                int posY = startTop + (row * stepY);

                CheckBox chkMod = CreateSystemCheckBox(desc, posX, posY);
                hdCheckBoxes[tag] = chkMod;
                builderMenuControls.Add(chkMod);
            }

            if (hdCheckBoxes.ContainsKey("B") && hdCheckBoxes.ContainsKey("A"))
            {
                hdCheckBoxes["B"].CheckedChanged += (s, e) => {
                    if (hdCheckBoxes["B"].Checked)
                    {
                        hdCheckBoxes["A"].Checked = true;
                        hdCheckBoxes["A"].Enabled = false;
                    }
                    else
                    {
                        hdCheckBoxes["A"].Enabled = true;
                    }
                };
            }

            // Кнопка сохранения настроек
            Button btnSaveConfig = new Button
            {
                Text = "Сохранить",
                Font = wowFontButton,
                ForeColor = Color.FromArgb(240, 225, 180),
                BackColor = Color.FromArgb(40, 70, 110),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(216, 58),
                Location = new Point(65, 594),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                Cursor = customCursor ?? Cursors.Hand,
                UseCompatibleTextRendering = true
            };
            btnSaveConfig.FlatAppearance.BorderSize = 1;
            btnSaveConfig.FlatAppearance.BorderColor = Color.FromArgb(90, 150, 200);
            btnSaveConfig.Click += (s, e) => {
                PlayClickSound();
                SaveConfig();
                MessageBox.Show("Конфигурация успешно сохранена!", "Сохранение", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            // Кнопка запуска кастомной сборки
            Button btnLaunchCustom = new Button
            {
                Text = "Запустить кастомную сборку",
                Font = wowFontButton,
                ForeColor = Color.FromArgb(240, 225, 180),
                BackColor = Color.FromArgb(40, 100, 50),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(354, 58),
                Location = new Point(294, 594),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                Cursor = customCursor ?? Cursors.Hand,
                UseCompatibleTextRendering = true
            };
            btnLaunchCustom.FlatAppearance.BorderSize = 1;
            btnLaunchCustom.FlatAppearance.BorderColor = Color.FromArgb(80, 200, 100);
            btnLaunchCustom.Click += (s, e) => {
                PlayClickSound();
                SaveConfig();
                RunSavedConfig();
            };

            // Кнопка возврата в главное меню
            Button btnBack = new Button
            {
                Text = "← Назад в меню",
                Font = wowFontButton,
                ForeColor = Color.FromArgb(240, 225, 180),
                BackColor = Color.FromArgb(80, 30, 40),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(252, 58),
                Location = new Point(660, 594),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                Cursor = customCursor ?? Cursors.Hand,
                UseCompatibleTextRendering = true
            };
            btnBack.FlatAppearance.BorderSize = 1;
            btnBack.FlatAppearance.BorderColor = Color.FromArgb(190, 150, 90);
            btnBack.Click += (s, e) => {
                PlayClickSound();
                ToggleMenu(true);
            };

            builderMenuControls.Add(btnSaveConfig);
            builderMenuControls.Add(btnLaunchCustom);
            builderMenuControls.Add(btnBack);

            foreach (var ctrl in builderMenuControls)
            {
                ctrl.Visible = false;
                this.Controls.Add(ctrl);
            }
        }

        private void ToggleMenu(bool showMain)
        {
            isMainMode = showMain;

            foreach (var ctrl in mainMenuControls)
                ctrl.Visible = showMain;

            foreach (var ctrl in builderMenuControls)
                ctrl.Visible = !showMain;

            this.Invalidate();
        }

        private Button CreateMenuButton(string text, int top, EventHandler onClick)
        {
            Button btn = new Button
            {
                Text = text,
                Font = wowFontButton,
                ForeColor = Color.FromArgb(240, 225, 180),
                BackColor = Color.FromArgb(200, 15, 23, 42),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(456, 70),
                Location = new Point(54, top),
                Cursor = customCursor ?? Cursors.Hand,
                UseCompatibleTextRendering = true
            };
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = Color.FromArgb(190, 150, 90);
            btn.Click += onClick;

            btn.MouseMove += (s, e) =>
            {
                Point p = btn.PointToScreen(e.Location);
                Point clientP = this.PointToClient(p);
                CalculateTargetParallax(clientP);
            };

            return btn;
        }

        private CheckBox CreateSystemCheckBox(string text, int left, int top)
        {
            CheckBox chk = new CheckBox
            {
                Text = text,
                Font = systemFontSettings,
                ForeColor = Color.FromArgb(241, 245, 249),
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(left, top),
                Cursor = customCursor ?? Cursors.Hand,
                UseVisualStyleBackColor = true,
                UseCompatibleTextRendering = true
            };
            return chk;
        }

        private void SaveConfig()
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(configFilePath, false))
                {
                    writer.WriteLine($"Vulkan={chkVulkan.Checked}");
                    writer.WriteLine($"Reshade={chkReshade.Checked}");
                    writer.WriteLine($"VulkanReshade={chkVulkanReshade.Checked}");
                    writer.WriteLine($"CleanCache={chkCleanCache.Checked}");
                    writer.WriteLine($"MusicPlaying={isMusicPlaying}");

                    foreach (var pair in hdCheckBoxes)
                    {
                        writer.WriteLine($"HD_{pair.Key}={pair.Value.Checked}");
                    }
                }

                if (btnRunLastConfig != null)
                {
                    btnRunLastConfig.Text = "⚡ Запустить последнюю конфигурацию";
                }
            }
            catch { }
        }

        private void LoadConfig()
        {
            try
            {
                if (!File.Exists(configFilePath))
                {
                    if (chkVulkan != null) chkVulkan.Checked = false;
                    if (chkReshade != null) chkReshade.Checked = false;
                    if (chkVulkanReshade != null) chkVulkanReshade.Checked = false;
                    if (chkCleanCache != null) chkCleanCache.Checked = true;

                    foreach (var pair in hdCheckBoxes)
                    {
                        pair.Value.Checked = false;
                    }
                    return;
                }

                string[] lines = File.ReadAllLines(configFilePath);
                foreach (string line in lines)
                {
                    string[] parts = line.Split('=');
                    if (parts.Length != 2) continue;

                    string key = parts[0].Trim();
                    bool val = bool.TryParse(parts[1], out bool res) && res;

                    if (key == "Vulkan" && chkVulkan != null)
                        chkVulkan.Checked = val;
                    else if (key == "Reshade" && chkReshade != null)
                        chkReshade.Checked = val;
                    else if (key == "VulkanReshade" && chkVulkanReshade != null)
                        chkVulkanReshade.Checked = val;
                    else if (key == "CleanCache" && chkCleanCache != null)
                        chkCleanCache.Checked = val;
                    else if (key == "MusicPlaying")
                    {
                        isMusicPlaying = val;
                        if (btnMusicToggle != null)
                        {
                            btnMusicToggle.Text = isMusicPlaying ? "Музыка: [ON]" : "Музыка: [OFF]";
                        }
                    }
                    else if (key.StartsWith("HD_"))
                    {
                        string tag = key.Substring(3);
                        if (hdCheckBoxes.ContainsKey(tag))
                        {
                            hdCheckBoxes[tag].Checked = val;
                        }
                    }
                }
            }
            catch { }
        }

        private void RunSavedConfig()
        {
            var selectedMods = new Dictionary<string, bool>();

            foreach (var pair in hdCheckBoxes)
            {
                selectedMods[pair.Key] = pair.Value.Checked;
            }

            if (selectedMods.ContainsKey("B") && selectedMods["B"])
            {
                selectedMods["C"] = true;
            }

            RunCustomBuild(chkVulkan.Checked, chkReshade.Checked, chkVulkanReshade.Checked, selectedMods, chkCleanCache.Checked);
        }

        private void RunCustomBuild(bool useVulkan, bool useReshade, bool useVulkanReshade, Dictionary<string, bool> activeHdMods, bool cleanCache)
        {
            try
            {
                if (soundPlayer != null) soundPlayer.Stop();
                if (snowTimer != null) snowTimer.Stop();

                if (cleanCache)
                {
                    string cacheDir = Path.Combine(baseDir, "Cache");
                    if (Directory.Exists(cacheDir))
                    {
                        Directory.Delete(cacheDir, true);
                    }
                }

                string dataDir = Path.Combine(baseDir, "Data");
                string dataRuRuDir = Path.Combine(dataDir, "ruRU");
                string vulkanDir = Path.Combine(baseDir, "Vulkan");
                string reshadeSourceDir = Path.Combine(baseDir, "Reshade");
                string vulkanReshadeSourceDir = Path.Combine(baseDir, "Reshade+vulkan");

                RemoveVulkanDlls(baseDir);
                RemoveReshadeFiles(baseDir, reshadeSourceDir);

                string targetD3D9 = Path.Combine(baseDir, "d3d9.dll");
                if (File.Exists(targetD3D9))
                {
                    try { File.Delete(targetD3D9); } catch { }
                }

                if (useVulkanReshade)
                {
                    if (Directory.Exists(vulkanReshadeSourceDir))
                    {
                        string[] filesToCopy = new[]
                        {
                            "d3d9.dll",
                            "dxvk.conf",
                            "ReShade.ini",
                            "ReShade32.dll",
                            "ReShade32.json",
                            "ReShadePreset.ini"
                        };

                        foreach (string fileName in filesToCopy)
                        {
                            string sourceFile = Path.Combine(vulkanReshadeSourceDir, fileName);
                            string destFile = Path.Combine(baseDir, fileName);

                            if (File.Exists(sourceFile))
                            {
                                File.Copy(sourceFile, destFile, overwrite: true);
                            }
                        }

                        string sourceShaders = Path.Combine(vulkanReshadeSourceDir, "reshade-shaders");
                        string destShaders = Path.Combine(baseDir, "reshade-shaders");
                        if (Directory.Exists(sourceShaders) && !Directory.Exists(destShaders))
                        {
                            CopyDirectoryRecursive(sourceShaders, destShaders);
                        }
                    }
                }
                else if (useReshade)
                {
                    ApplyReshadeFiles(reshadeSourceDir, baseDir);
                }
                else if (useVulkan)
                {
                    ApplyVulkanFiles(vulkanDir, baseDir);
                }

                ApplyHdComponentStates(dataRuRuDir, activeHdMods);

                string wowExe = Path.Combine(baseDir, "Wow.exe");
                if (File.Exists(wowExe))
                {
                    ProcessStartInfo psi = new ProcessStartInfo(wowExe)
                    {
                        WorkingDirectory = baseDir,
                        UseShellExecute = false
                    };

                    if (useVulkanReshade)
                    {
                        psi.EnvironmentVariables["VK_LAYER_PATH"] = baseDir;
                        psi.EnvironmentVariables["VK_INSTANCE_LAYERS"] = "VK_LAYER_RESHADE_32";
                    }

                    Process.Start(psi);
                    Application.Exit();
                }
                else
                {
                    MessageBox.Show("Файл Wow.exe не найден в текущей папке!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при обработке файлов: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Form1_MouseMove(object sender, MouseEventArgs e)
        {
            CalculateTargetParallax(e.Location);
        }

        private void CalculateTargetParallax(Point mousePosition)
        {
            float targetX = ((float)mousePosition.X / this.ClientSize.Width - 0.5f) * -ParallaxMargin;
            float targetY = ((float)mousePosition.Y / this.ClientSize.Height - 0.5f) * -ParallaxMargin;

            targetParallax = new PointF(targetX, targetY);
        }

        private void InitSnow()
        {
            for (int i = 0; i < 650; i++)
            {
                snowflakes.Add(new Snowflake
                {
                    X = random.Next(-240, 1296),
                    Y = random.Next(0, 768),
                    Size = (float)(random.NextDouble() * 4.5 + 1.2),
                    SpeedY = (float)(random.NextDouble() * 1.6 + 0.8),
                    SpeedX = (float)(random.NextDouble() * 2.5 + 1.5),
                    Alpha = random.Next(60, 240)
                });
            }

            snowTimer = new Timer();
            snowTimer.Interval = 20;
            snowTimer.Tick += (s, e) =>
            {
                UpdateSnow();
                UpdateParallaxSmooth();
                this.Invalidate();
            };
            snowTimer.Start();
        }

        private void UpdateParallaxSmooth()
        {
            currentParallax.X += (targetParallax.X - currentParallax.X) * 0.1f;
            currentParallax.Y += (targetParallax.Y - currentParallax.Y) * 0.1f;
        }

        private void UpdateSnow()
        {
            foreach (var flake in snowflakes)
            {
                flake.Y += flake.SpeedY;
                flake.X += flake.SpeedX + (float)(Math.Sin((flake.Y + flake.X) / 25) * 1.2);

                if (flake.Y > this.ClientSize.Height)
                {
                    flake.Y = -15;
                    flake.X = random.Next(-240, this.ClientSize.Width);
                }

                if (flake.X > this.ClientSize.Width)
                {
                    flake.X = -15;
                    flake.Y = random.Next(-50, this.ClientSize.Height);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            if (bgImage != null)
            {
                Rectangle targetRect = new Rectangle(
                    (int)(-ParallaxMargin / 2 + currentParallax.X),
                    (int)(-ParallaxMargin / 2 + currentParallax.Y),
                    this.ClientSize.Width + ParallaxMargin,
                    this.ClientSize.Height + ParallaxMargin
                );
                g.DrawImage(bgImage, targetRect);
            }

            if (isMainMode)
            {
                foreach (var flake in snowflakes)
                {
                    using (Brush brush = new SolidBrush(Color.FromArgb(flake.Alpha, Color.White)))
                    {
                        g.FillEllipse(brush, flake.X, flake.Y, flake.Size, flake.Size);
                    }
                }
            }

            if (!isMainMode)
            {
                using (Brush bgBrush = new SolidBrush(Color.FromArgb(248, 10, 14, 26)))
                {
                    g.FillRectangle(bgBrush, 48, 108, 1200, 564);
                }
                using (Pen borderPen = new Pen(Color.FromArgb(190, 150, 90), 2))
                {
                    g.DrawRectangle(borderPen, 48, 108, 1200, 564);
                }
            }
        }

        private void PlayBackgroundMusic()
        {
            try
            {
                UnmanagedMemoryStream audioStream = Properties.Resources.lk;
                if (audioStream != null)
                {
                    soundPlayer = new SoundPlayer(audioStream);
                    if (isMusicPlaying)
                    {
                        soundPlayer.PlayLooping();
                    }
                }
            }
            catch
            {
                // Игнорируем ошибки аудиопотока
            }
        }

        private void ToggleMusic(object sender, EventArgs e)
        {
            if (soundPlayer == null) return;

            if (isMusicPlaying)
            {
                soundPlayer.Stop();
                btnMusicToggle.Text = "Музыка: [OFF]";
                isMusicPlaying = false;
            }
            else
            {
                soundPlayer.PlayLooping();
                btnMusicToggle.Text = "Музыка: [ON]";
                isMusicPlaying = true;
            }

            SaveConfig();
        }

        private void RemoveVulkanDlls(string targetDir)
        {
            string d3d9 = Path.Combine(targetDir, "d3d9.dll");
            string dxgi = Path.Combine(targetDir, "dxgi.dll");

            try { if (File.Exists(d3d9) && !File.Exists(Path.Combine(targetDir, "Reshade", "d3d9.dll"))) File.Delete(d3d9); } catch { }
            try { if (File.Exists(dxgi)) File.Delete(dxgi); } catch { }
        }

        private void ApplyVulkanFiles(string vulkanDir, string targetDir)
        {
            if (!Directory.Exists(vulkanDir)) return;

            string d3d9Src = Path.Combine(vulkanDir, "d3d9.dll");
            string dxgiSrc = Path.Combine(vulkanDir, "dxgi.dll");

            try { if (File.Exists(d3d9Src)) File.Copy(d3d9Src, Path.Combine(targetDir, "d3d9.dll"), true); } catch { }
            try { if (File.Exists(dxgiSrc)) File.Copy(dxgiSrc, Path.Combine(targetDir, "dxgi.dll"), true); } catch { }
        }

        private void ApplyReshadeFiles(string sourceDir, string targetDir)
        {
            if (!Directory.Exists(sourceDir)) return;

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(file);
                string destFile = Path.Combine(targetDir, fileName);
                try { File.Copy(file, destFile, true); } catch { }
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(dir);
                string destDir = Path.Combine(targetDir, dirName);
                CopyDirectoryRecursive(dir, destDir);
            }
        }

        private void RemoveReshadeFiles(string targetDir, string sourceDir)
        {
            if (!Directory.Exists(sourceDir)) return;

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(file);
                string targetFile = Path.Combine(targetDir, fileName);
                try { if (File.Exists(targetFile)) File.Delete(targetFile); } catch { }
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(dir);
                string targetDirSub = Path.Combine(targetDir, dirName);
                try { if (Directory.Exists(targetDirSub)) Directory.Delete(targetDirSub, true); } catch { }
            }
        }

        private void CopyDirectoryRecursive(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
            {
                string dest = Path.Combine(target, Path.GetFileName(file));
                File.Copy(file, dest, true);
            }
            foreach (string subDir in Directory.GetDirectories(source))
            {
                string dest = Path.Combine(target, Path.GetFileName(subDir));
                CopyDirectoryRecursive(subDir, dest);
            }
        }

        private void ApplyHdComponentStates(string dataRuRuDir, Dictionary<string, bool> activeMods)
        {
            if (!Directory.Exists(dataRuRuDir)) return;

            string[] allTags = new string[] { "A", "B", "C", "E", "H", "I", "L", "M", "R", "S", "T", "U", "V", "W", "X", "Z" };

            foreach (string tag in allTags)
            {
                bool shouldEnable = activeMods.ContainsKey(tag) && activeMods[tag];

                string activeFile = Path.Combine(dataRuRuDir, $"patch-ruRU-{tag}.mpq");
                string activeFileUpper = Path.Combine(dataRuRuDir, $"patch-ruRU-{tag}.MPQ");

                string realActive = File.Exists(activeFile) ? activeFile : (File.Exists(activeFileUpper) ? activeFileUpper : null);
                string disabledFile = (realActive ?? activeFile) + ".off";

                try
                {
                    if (shouldEnable)
                    {
                        if (File.Exists(disabledFile) && realActive == null)
                        {
                            string restorePath = disabledFile.Substring(0, disabledFile.Length - 4);
                            File.Move(disabledFile, restorePath);
                        }
                    }
                    else
                    {
                        if (realActive != null)
                        {
                            if (File.Exists(disabledFile)) File.Delete(disabledFile);
                            File.Move(realActive, disabledFile);
                        }
                    }
                }
                catch { }
            }
        }
    }
}