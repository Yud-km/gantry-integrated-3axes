using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace GantryCraneIntegrated
{
    public class CraneCell
    {
        public string Name { get; set; }
        public string CommandName { get; set; }

        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public bool Mandatory { get; set; }
        public bool SelectedPath { get; set; }

        public CraneCell(string name, string commandName, double x, double y, double z, bool mandatory, bool selectedPath)
        {
            Name = name;
            CommandName = commandName;
            X = x;
            Y = y;
            Z = z;
            Mandatory = mandatory;
            SelectedPath = selectedPath;
        }
    }

    public class MainForm : Form
    {
        private readonly SerialPort serial = new SerialPort();
        private readonly StringBuilder rxBuffer = new StringBuilder();

        private const string NoTrajectoryText = "Default";

        private readonly List<CraneCell> cells = new List<CraneCell>();
        private readonly List<CraneCell> settingCells = new List<CraneCell>();

        private readonly Dictionary<string, Button> mainCellButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, Button> jogCellButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, Button> settingCellButtons = new Dictionary<string, Button>();

        private readonly Dictionary<string, List<CraneCell>> trajectoryProfiles =
            new Dictionary<string, List<CraneCell>>(StringComparer.OrdinalIgnoreCase);

        private readonly Queue<string> commandQueue = new Queue<string>();
        private readonly System.Windows.Forms.Timer commandQueueTimer = new System.Windows.Forms.Timer();


        // khai bao menustrip
        private readonly MenuStrip menuStrip = new MenuStrip();
        private readonly ToolStripMenuItem menuMain = new ToolStripMenuItem("Main");
        private readonly ToolStripMenuItem menuJog = new ToolStripMenuItem("Jog");
        private readonly ToolStripMenuItem menuSetting = new ToolStripMenuItem("Setting");

        private readonly ComboBox cboTrajectory = new ComboBox();
        private readonly Label lblSettingRouteName = new Label();

        private readonly Panel screenHost = new Panel();
        private Panel mainPanel = new Panel();
        private Panel jogPanel = new Panel();
        private Panel settingPanel = new Panel();

        private string currentSettingTrajectoryName = "";

        private readonly ComboBox cboPorts = new ComboBox();
        private readonly ComboBox cboBaud = new ComboBox();

        private readonly Button btnRefresh = new Button();
        private readonly Button btnConnect = new Button();

        private readonly Label lblConnection = new Label();
        private readonly Label lblState = new Label();
        private readonly Label lblX = new Label();
        private readonly Label lblY = new Label();
        private readonly Label lblZ = new Label();
        private readonly Label lblHomed = new Label();
        private readonly Label lblCount = new Label();
        private readonly Label lblAlarm = new Label();

        private Label ledSensor = new Label();
        private Label ledLimitX = new Label();
        private Label ledLimitY = new Label();
        private Label ledLimitZ = new Label();
        private Label ledFan = new Label();
        private Label ledMagnet = new Label();

        private readonly TextBox txtTargetX = new TextBox();
        private readonly TextBox txtTargetY = new TextBox();
        private readonly TextBox txtTargetZ = new TextBox();
        private readonly TextBox txtGotoFeed = new TextBox();

        private readonly TextBox txtStep = new TextBox();
        private readonly TextBox txtFeed = new TextBox();
        private readonly TextBox txtFeedXY = new TextBox();
        private readonly TextBox txtFeedZ = new TextBox();

        private readonly TextBox txtRaw = new TextBox();
        private readonly TextBox txtLog = new TextBox();

        private readonly System.Windows.Forms.Timer readTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer statusTimer = new System.Windows.Forms.Timer();

        private double currentX = 0.0;
        private double currentY = 0.0;
        private double currentZ = 0.0;

        private bool autoRunning = false;
        private bool routeApplyPending = false;
        private bool mcuHomed = false;
        private string machineState = "Disconnected";
        private string lastAlarmMessage = "";

        public MainForm()
        {
            Text = "Gantry Crane Integrated Control";
            StartPosition = FormStartPosition.CenterScreen;

            /*
             * Laptop 15.6 inch, 16:9, 1920x1080:
             * open full screen/maximized so Jog controls and relay buttons are visible.
             */
            Size = new Size(1920, 1080);
            MinimumSize = new Size(1366, 768);
            WindowState = FormWindowState.Maximized;
            Font = new Font("Segoe UI", 10F);

            InitCells();
            ResetSettingCellsToDefaults();

            BuildUi();
            SetupSerial();
            RefreshPorts();

            commandQueueTimer.Interval = 80;
            commandQueueTimer.Tick += (_, _) => SendNextQueuedCommand();

            LoadTrajectoryFiles();
            RefreshTrajectoryCombo();

            readTimer.Interval = 50;
            readTimer.Tick += (_, _) => ReadSerialByTimer();

            statusTimer.Interval = 500;
            statusTimer.Tick += (_, _) =>
            {
                if (serial.IsOpen)
                {
                    SendCommand("STATUS", logTx: false);
                    SendCommand("LIMITS", logTx: false);
                }
            };

            ShowScreen(mainPanel);
        }

        private void InitCells()
        {
            cells.Clear();
            cells.AddRange(CreateDefaultCells());
        }

        private List<CraneCell> CreateBuiltInDefaultCells()
        {
            return new List<CraneCell>
            {
                new CraneCell("HOME", "HOME", 0, 0, 0, mandatory: true, selectedPath: true),
                new CraneCell("1", "1", 135, 0, 0, mandatory: false, selectedPath: true),
                new CraneCell("2", "2", 270, 0, 0, mandatory: false, selectedPath: true),
                new CraneCell("3", "3", 270, 270, 0, mandatory: false, selectedPath: true),
                new CraneCell("4", "4", 135, 270, 0, mandatory: false, selectedPath: true),
                new CraneCell("END", "END", 0, 270, 0, mandatory: true, selectedPath: true)
            };
        }

        private List<CraneCell> CreateDefaultCells()
        {
            /*
             * Default ban đầu là tọa độ cố định trong code.
             * Nếu người dùng đã APPLY khi ComboBox đang chọn Default,
             * chương trình sẽ lưu Default.txt và nạp lại vào trajectoryProfiles.
             * Khi đó Default sẽ lấy dữ liệu đã lưu thay vì hard-code ban đầu.
             */
            if (trajectoryProfiles.TryGetValue(NoTrajectoryText, out List<CraneCell> savedDefault))
            {
                return CloneCells(savedDefault);
            }

            return CreateBuiltInDefaultCells();
        }

        private List<CraneCell> CloneCells(IEnumerable<CraneCell> source)
        {
            var result = new List<CraneCell>();

            foreach (var cell in source)
            {
                result.Add(new CraneCell(
                    cell.Name,
                    cell.CommandName,
                    cell.X,
                    cell.Y,
                    cell.Z,
                    cell.Mandatory,
                    cell.SelectedPath));
            }

            return result;
        }

        private void ResetSettingCellsToDefaults()
        {
            settingCells.Clear();
            settingCells.AddRange(CreateDefaultCells());
        }

        private CraneCell GetCellFromList(List<CraneCell> source, string name)
        {
            foreach (var cell in source)
            {
                if (cell.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    cell.CommandName.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return cell;
                }
            }

            throw new InvalidOperationException("Cell not found: " + name);
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 6,
                ColumnCount = 1
            };

            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));   // UART
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));   // Status
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));  // Sensor / Limit / Relay
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));  // Log

            Controls.Add(root);

            menuStrip.Items.Add(menuMain);
            menuStrip.Items.Add(menuJog);
            menuStrip.Items.Add(menuSetting);

            menuMain.Click += (_, _) => ShowScreen(mainPanel);

            menuJog.Click += (_, _) =>
            {
                if (autoRunning || machineState.Equals("Auto", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        "Máy đang chạy AUTO. Hãy bấm STOP trước khi chuyển sang Jog.",
                        "Không thể chuyển màn hình",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                ShowScreen(jogPanel);
            };

            menuSetting.Click += (_, _) =>
            {
                /*
                 * Setting only edits and saves route files.
                 * It does not command the crane directly.
                 */
                ShowScreen(settingPanel);
            };

            root.Controls.Add(menuStrip, 0, 0);
            root.Controls.Add(BuildConnectionPanel(), 0, 1);
            root.Controls.Add(BuildPositionPanel(), 0, 2);
            root.Controls.Add(BuildSignalPanel(), 0, 3);

            screenHost.Dock = DockStyle.Fill;
            screenHost.Padding = new Padding(6);
            root.Controls.Add(screenHost, 0, 4);

            root.Controls.Add(BuildLogPanel(), 0, 5);

            mainPanel = BuildMainPanel();
            jogPanel = BuildJogPanel();
            settingPanel = BuildSettingPanel();
        }


        private Control BuildConnectionPanel()
        {
            var box = new GroupBox
            {
                Text = "UART",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 10,
                RowCount = 1
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));   // COM label
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));  // COM
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));   // Refresh
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65));   // Baud label
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));  // Baud
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));  // Connect
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));  // Connected text
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));   // Route label
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));  // Route combobox
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));   // dư

            layout.Controls.Add(TextLabel("COM:"), 0, 0);

            cboPorts.DropDownStyle = ComboBoxStyle.DropDownList;
            cboPorts.Dock = DockStyle.Fill;
            layout.Controls.Add(cboPorts, 1, 0);

            btnRefresh.Text = "Refresh";
            btnRefresh.Dock = DockStyle.Fill;
            btnRefresh.Click += (_, _) => RefreshPorts();
            layout.Controls.Add(btnRefresh, 2, 0);

            layout.Controls.Add(TextLabel("Baud:"), 3, 0);

            cboBaud.DropDownStyle = ComboBoxStyle.DropDownList;
            cboBaud.Items.AddRange(new object[] { "9600", "57600", "115200" });
            cboBaud.SelectedItem = "115200";
            cboBaud.Dock = DockStyle.Fill;
            layout.Controls.Add(cboBaud, 4, 0);

            btnConnect.Text = "CONNECT";
            btnConnect.Dock = DockStyle.Fill;
            btnConnect.BackColor = Color.SeaGreen;
            btnConnect.ForeColor = Color.White;
            btnConnect.Click += (_, _) => ToggleConnection();
            layout.Controls.Add(btnConnect, 5, 0);

            lblConnection.Text = "Disconnected";
            lblConnection.ForeColor = Color.DarkRed;
            lblConnection.Dock = DockStyle.Fill;
            lblConnection.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(lblConnection, 6, 0);

            // chon quy dao
            layout.Controls.Add(TextLabel("Route:"), 7, 0);

            cboTrajectory.DropDownStyle = ComboBoxStyle.DropDownList;
            cboTrajectory.Dock = DockStyle.Fill;
            cboTrajectory.SelectedIndexChanged += (_, _) => ApplySelectedTrajectoryNow();

            layout.Controls.Add(cboTrajectory, 8, 0);


            //button Cell: dong bo toa toa tu vdk voi winform 
            var btnReadCells = new Button
            {
                Text = "Đọc CELLS",
                Dock = DockStyle.Fill
            };

            btnReadCells.Click += (_, _) => SendCommand("CELLS");
            layout.Controls.Add(btnReadCells, 9, 0);

            box.Controls.Add(layout);
            return box;
        }

        private bool isLoadingTrajectoryCombo = false;

        private void ApplySelectedTrajectoryNow()
        {
            if (isLoadingTrajectoryCombo)
            {
                return;
            }

            string selectedName = cboTrajectory.SelectedItem as string;

            if (string.IsNullOrWhiteSpace(selectedName))
            {
                return;
            }

            /*
             * Route can be changed when the machine is Idle or disconnected.
             * While Auto/Home/Homing/Jog is running, route change is blocked.
             *
             * After one AUTO cycle finishes and the crane is at HOME waiting
             * for object, MCU reports State = Idle. In that state, this
             * function is allowed to send new CELL/PATH immediately. When the
             * sensor detects object again, MCU runs with the new route without
             * pressing START AUTO again.
             */
            if (serial.IsOpen &&
                (autoRunning ||
                 machineState.Equals("Auto", StringComparison.OrdinalIgnoreCase) ||
                 machineState.Equals("Home", StringComparison.OrdinalIgnoreCase) ||
                 machineState.Equals("Homing", StringComparison.OrdinalIgnoreCase) ||
                 machineState.Equals("Jog", StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(
                    "Máy đang chạy. Chỉ được đổi quỹ đạo khi State = Idle.",
                    "Không thể đổi quỹ đạo",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            List<CraneCell> selectedCells;

            if (selectedName.Equals(NoTrajectoryText, StringComparison.OrdinalIgnoreCase))
            {
                /* Default = HOME -> 1 -> 2 -> 3 -> 4 -> END. */
                selectedCells = CreateDefaultCells();
            }
            else
            {
                if (!trajectoryProfiles.TryGetValue(selectedName, out selectedCells))
                {
                    MessageBox.Show("Không tìm thấy quỹ đạo: " + selectedName);
                    return;
                }

                selectedCells = CloneCells(selectedCells);
            }

            /* Update WinForms display immediately. */
            cells.Clear();
            cells.AddRange(CloneCells(selectedCells));
            UpdateCellViews();

            if (!serial.IsOpen)
            {
                routeApplyPending = false;
                Log("Route selected locally: " + selectedName);
                return;
            }

            /*
             * Chưa CHECK HOME thì chỉ cập nhật trên WinForms, chưa gửi xuống MCU.
             * Sau khi MCU báo home:ok thì mới gửi ROUTE/CELL/PATH/APPLYROUTE.
             */
            if (!mcuHomed)
            {
                routeApplyPending = false;
                Log("Route selected locally. MCU not homed yet, wait CHECK HOME: " + selectedName);
                return;
            }

            /*
             * Khi chọn quỹ đạo trong ComboBox cũng gửi ROUTE/CELL/PATH/APPLYROUTE ngay.
             */
            routeApplyPending = true;
            QueueCommands(BuildRouteCommands(selectedName, selectedCells));

            Log("Route sent to Arduino: " + selectedName);
        }

        private void StartAutoWithCurrentTrajectory()
        {
            /*
             * Hàm cũ giữ lại để tránh lỗi tham chiếu.
             * Logic mới: START AUTO chỉ gửi AUTO.
             */
            if (routeApplyPending || commandQueue.Count > 0)
            {
                MessageBox.Show(
                    "Quỹ đạo đang được gửi xuống vi điều khiển. Hãy đợi RX< route:applied rồi START AUTO.",
                    "Đợi cập nhật quỹ đạo",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            SendCommand("AUTO");
        }

        private List<string> BuildRouteCommands(string routeName, IEnumerable<CraneCell> routeCells)
        {
            var commands = new List<string>();

            commands.Add("ROUTE " + SanitizeRouteNameForMcu(routeName));

            foreach (var cell in routeCells)
            {
                commands.Add(BuildCellCommand(cell));
            }

            foreach (var cell in routeCells)
            {
                if (!cell.Mandatory)
                {
                    commands.Add($"PATH {cell.CommandName} {(cell.SelectedPath ? "ON" : "OFF")}");
                }
            }

            /*
             * Lệnh cuối báo cho MCU biết đã gửi đủ ROUTE/CELL/PATH.
             * MCU chỉ build lại auto_route sau lệnh này.
             */
            commands.Add("APPLYROUTE");

            return commands;
        }

        private string SanitizeRouteNameForMcu(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return NoTrajectoryText;
            }

            /* LCD is 16x2, keep route name short and token-safe. */
            string result = SanitizeTrajectoryName(name).Replace(' ', '_');

            if (result.Length > 10)
            {
                result = result.Substring(0, 10);
            }

            return result;
        }

        private Control BuildPositionPanel()
        {
            var box = new GroupBox
            {
                Text = "Trạng thái",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 12,
                RowCount = 1
            };

            for (int i = 0; i < 12; i++)
            {
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 8.33f));
            }

            layout.Controls.Add(TextLabel("State:"), 0, 0);
            layout.Controls.Add(lblState, 1, 0);

            layout.Controls.Add(TextLabel("X:"), 2, 0);
            layout.Controls.Add(lblX, 3, 0);

            layout.Controls.Add(TextLabel("Y:"), 4, 0);
            layout.Controls.Add(lblY, 5, 0);

            layout.Controls.Add(TextLabel("Z:"), 6, 0);
            layout.Controls.Add(lblZ, 7, 0);

            layout.Controls.Add(TextLabel("Homed:"), 8, 0);
            layout.Controls.Add(lblHomed, 9, 0);

            layout.Controls.Add(TextLabel("Count:"), 10, 0);
            layout.Controls.Add(lblCount, 11, 0);

            SetValueLabel(lblState, "Idle");
            SetValueLabel(lblX, "0.00");
            SetValueLabel(lblY, "0.00");
            SetValueLabel(lblZ, "0.00");
            SetValueLabel(lblHomed, "0");
            SetValueLabel(lblCount, "0");

            box.Controls.Add(layout);
            return box;
        }

        private Control BuildSignalPanel()
        {
            var box = new GroupBox
            {
                Text = "Sensor / Limit / Relay",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 7,
                RowCount = 1
            };

            for (int i = 0; i < 7; i++)
            {
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14.28f));
            }

            ledSensor = Led("OBJECT\nA2");
            ledLimitX = Led("LIMIT X\nD9");
            ledLimitY = Led("LIMIT Y\nD10");
            ledLimitZ = Led("LIMIT Z+\nD11");
            ledFan = Led("FAN");
            ledMagnet = Led("MAGNET");

            lblAlarm.Dock = DockStyle.Fill;
            lblAlarm.TextAlign = ContentAlignment.MiddleCenter;
            lblAlarm.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            lblAlarm.BackColor = Color.White;
            lblAlarm.BorderStyle = BorderStyle.FixedSingle;
            lblAlarm.Text = "Alarm\nNONE";

            layout.Controls.Add(ledSensor, 0, 0);
            layout.Controls.Add(ledLimitX, 1, 0);
            layout.Controls.Add(ledLimitY, 2, 0);
            layout.Controls.Add(ledLimitZ, 3, 0);
            layout.Controls.Add(ledFan, 4, 0);
            layout.Controls.Add(ledMagnet, 5, 0);
            layout.Controls.Add(lblAlarm, 6, 0);

            box.Controls.Add(layout);
            return box;
        }

        private Panel BuildMainPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill
            };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };

            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));

            panel.Controls.Add(root);

            var gridBox = new GroupBox
            {
                Text = "Sơ đồ 6 ô - Main chỉ hiển thị",
                Dock = DockStyle.Fill,
                Padding = new Padding(12)
            };

            gridBox.Controls.Add(BuildCellGrid(mainCellButtons, enableContextMenu: false));
            root.Controls.Add(gridBox, 0, 0);

            var actionBox = new GroupBox
            {
                Text = "Điều khiển AUTO",
                Dock = DockStyle.Fill,
                Padding = new Padding(12)
            };

            var actions = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 9,
                Height = 390
            };

            for (int i = 0; i < 9; i++)
            {
                actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            }

            var btnHome = ActionButton("CHECK HOME", Color.SteelBlue);
            btnHome.Click += (_, _) => SendCommand("HOME");

            var btnAuto = ActionButton("START AUTO", Color.SeaGreen);
            btnAuto.Click += (_, _) =>
            {
                /*
                 * START AUTO chỉ chạy.
                 * Việc cập nhật tọa độ/quỹ đạo phải thực hiện bằng nút APPLY QUỸ ĐẠO trước đó.
                 */
                if (routeApplyPending || commandQueue.Count > 0)
                {
                    MessageBox.Show(
                        "Quỹ đạo đang được gửi xuống vi điều khiển. Hãy đợi RX< route:applied rồi START AUTO.",
                        "Đợi cập nhật quỹ đạo",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                SendCommand("AUTO");
            };

            var btnStop = ActionButton("STOP", Color.Firebrick);
            btnStop.Click += (_, _) =>
            {
                commandQueueTimer.Stop();
                commandQueue.Clear();

                SendCommand("STOP");
                autoRunning = false;
            };

            var btnStatus = new Button { Text = "STATUS", Dock = DockStyle.Fill };
            btnStatus.Click += (_, _) => SendCommand("STATUS");

            var btnClear = new Button { Text = "CLEAR ALARM", Dock = DockStyle.Fill };
            btnClear.Click += (_, _) =>
            {
                lastAlarmMessage = "";
                SendCommand("CLEAR");
            };

            var btnReset = new Button { Text = "COUNT RESET", Dock = DockStyle.Fill };
            btnReset.Click += (_, _) => SendCommand("COUNTRESET");
            /*
            cboTrajectory.Dock = DockStyle.Fill;
            cboTrajectory.DropDownStyle = ComboBoxStyle.DropDownList;
            cboTrajectory.SelectedIndexChanged += (_, _) =>
            {
                string selectedName = cboTrajectory.SelectedItem as string;

                if (string.IsNullOrWhiteSpace(selectedName) ||
                    selectedName.Equals(NoTrajectoryText, StringComparison.OrdinalIgnoreCase))
                {
                    cells.Clear();
                    cells.AddRange(CreateDefaultCells());
                    UpdateCellViews();
                }
            };
            */


            actions.Controls.Add(btnHome, 0, 0);
            actions.Controls.Add(btnAuto, 0, 1);
            actions.Controls.Add(btnStop, 0, 2);
            actions.Controls.Add(btnStatus, 0, 3);
            actions.Controls.Add(btnClear, 0, 4);
            actions.Controls.Add(btnReset, 0, 5);

            actionBox.Controls.Add(actions);
            root.Controls.Add(actionBox, 1, 0);

            return panel;
        }




        // tao panel setting
        private Panel BuildSettingPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill
            };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };

            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));

            panel.Controls.Add(root);

            var gridBox = new GroupBox
            {
                Text = "Setting - tạo quỹ đạo và tọa độ ô",
                Dock = DockStyle.Fill,
                Padding = new Padding(12)
            };

            gridBox.Controls.Add(BuildSettingCellGrid());
            root.Controls.Add(gridBox, 0, 0);

            var actionBox = new GroupBox
            {
                Text = "Lưu quỹ đạo",
                Dock = DockStyle.Fill,
                Padding = new Padding(12)
            };

            var actions = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 9,
                Height = 410
            };

            for (int i = 0; i < 9; i++)
            {
                actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            }

            lblSettingRouteName.Text = "Chưa tạo tên quỹ đạo";
            lblSettingRouteName.Dock = DockStyle.Fill;
            lblSettingRouteName.TextAlign = ContentAlignment.MiddleLeft;
            lblSettingRouteName.Font = new Font("Segoe UI", 10F, FontStyle.Bold);

            var btnCreate = ActionButton("CREATE", Color.SteelBlue);
            btnCreate.Click += (_, _) => CreateNewTrajectoryDraft();

            var btnAdd = ActionButton("ADD", Color.SeaGreen);
            btnAdd.Click += (_, _) => SaveCurrentTrajectoryDraft();

            var btnDelete = ActionButton("DELETE SELECTED ROUTE", Color.Firebrick);
            btnDelete.Click += (_, _) => DeleteSelectedTrajectory();

            var btnResetDraft = new Button
            {
                Text = "RESET DEFAULT CELLS",
                Dock = DockStyle.Fill
            };
            btnResetDraft.Click += (_, _) =>
            {
                ResetSettingCellsToDefaults();
                UpdateSettingCellViews();
            };

            var note = new Label
            {
                Text =
                    "Cách dùng: CREATE đặt tên → chuột phải vào ô để set tọa độ/quỹ đạo → ADD để lưu file .txt. " +
                    "Chọn quỹ đạo trên ComboBox rồi DELETE để xóa quỹ đạo và file .txt. Default không xóa được.",
                Dock = DockStyle.Fill,
                ForeColor = Color.DimGray,
                TextAlign = ContentAlignment.TopLeft
            };

            actions.Controls.Add(lblSettingRouteName, 0, 0);
            actions.Controls.Add(btnCreate, 0, 1);
            actions.Controls.Add(btnAdd, 0, 2);
            actions.Controls.Add(btnDelete, 0, 3);
            actions.Controls.Add(btnResetDraft, 0, 4);
            actions.Controls.Add(note, 0, 5);
            actions.SetRowSpan(note, 3);

            actionBox.Controls.Add(actions);
            root.Controls.Add(actionBox, 1, 0);

            return panel;
        }

        private TableLayoutPanel BuildSettingCellGrid()
        {
            settingCellButtons.Clear();

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 2,
                Padding = new Padding(10)
            };

            for (int i = 0; i < 3; i++)
            {
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            }

            for (int i = 0; i < 2; i++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            }

            AddSettingCellButton(grid, GetCellFromList(settingCells, "HOME"), 0, 0);
            AddSettingCellButton(grid, GetCellFromList(settingCells, "1"), 1, 0);
            AddSettingCellButton(grid, GetCellFromList(settingCells, "2"), 2, 0);

            AddSettingCellButton(grid, GetCellFromList(settingCells, "END"), 0, 1);
            AddSettingCellButton(grid, GetCellFromList(settingCells, "4"), 1, 1);
            AddSettingCellButton(grid, GetCellFromList(settingCells, "3"), 2, 1);

            UpdateSettingCellViews();

            return grid;
        }

        private void AddSettingCellButton(TableLayoutPanel grid, CraneCell cell, int col, int row)
        {
            var button = new Button
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(6),
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                BackColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Tag = cell
            };

            button.ContextMenuStrip = CreateSettingCellContextMenu(button);

            settingCellButtons[cell.Name] = button;
            grid.Controls.Add(button, col, row);
        }

        private ContextMenuStrip CreateSettingCellContextMenu(Button button)
        {
            var menu = new ContextMenuStrip();

            var setPath = new ToolStripMenuItem("Set quỹ đạo / bỏ chọn");
            var setCoordinate = new ToolStripMenuItem("Set tọa độ...");
            var setFromCurrent = new ToolStripMenuItem("Lấy tọa độ hiện tại");

            setPath.Click += (_, _) =>
            {
                var cell = (CraneCell)button.Tag;

                if (cell.Mandatory)
                {
                    MessageBox.Show("HOME và END là ô bắt buộc đi qua.", "Thông báo");
                    return;
                }

                cell.SelectedPath = !cell.SelectedPath;
                UpdateSettingCellViews();
            };

            setCoordinate.Click += (_, _) =>
            {
                var cell = (CraneCell)button.Tag;

                using (var dialog = new CoordinateDialog(cell, currentX, currentY, currentZ))
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        cell.X = dialog.ResultX;
                        cell.Y = dialog.ResultY;
                        cell.Z = dialog.ResultZ;

                        UpdateSettingCellViews();
                    }
                }
            };

            setFromCurrent.Click += (_, _) =>
            {
                var cell = (CraneCell)button.Tag;

                cell.X = currentX;
                cell.Y = currentY;
                cell.Z = currentZ;

                UpdateSettingCellViews();
            };

            menu.Items.Add(setPath);
            menu.Items.Add(setCoordinate);
            menu.Items.Add(setFromCurrent);

            return menu;
        }

        private void UpdateSettingCellViews()
        {
            foreach (var pair in settingCellButtons)
            {
                CraneCell cell = GetCellFromList(settingCells, pair.Key);
                Button button = pair.Value;
                button.Tag = cell;

                string pathMark = cell.SelectedPath ? "✓" : " ";
                if (cell.Mandatory) pathMark = "★";

                button.Text =
                    $"{cell.Name}\n{pathMark}\nX{cell.X:0} Y{cell.Y:0}\nZ{cell.Z:0}";

                /*
                 * Setting screen is only for route editing.
                 * Do not change color when crane reaches the cell.
                 */
                button.BackColor = Color.White;
            }
        }

        private void CreateNewTrajectoryDraft()
        {
            using (var dialog = new TrajectoryNameDialog())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                string name = SanitizeTrajectoryName(dialog.TrajectoryName);

                if (name.Length == 0)
                {
                    MessageBox.Show("Tên quỹ đạo không hợp lệ.", "Lỗi",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string filePath = GetTrajectoryFilePath(name);

                if (File.Exists(filePath))
                {
                    DialogResult overwrite = MessageBox.Show(
                        "Quỹ đạo này đã tồn tại. Bạn có muốn ghi đè khi bấm ADD không?",
                        "Quỹ đạo đã tồn tại",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (overwrite != DialogResult.Yes)
                    {
                        return;
                    }
                }

                currentSettingTrajectoryName = name;
                ResetSettingCellsToDefaults();
                UpdateSettingCellViews();

                lblSettingRouteName.Text = "Đang tạo: " + currentSettingTrajectoryName;
            }
        }

        private void ApplyCurrentCellsToSelectedTrajectory()
        {
            string selectedName = cboTrajectory.SelectedItem as string;

            if (string.IsNullOrWhiteSpace(selectedName))
            {
                selectedName = NoTrajectoryText;
            }

            /*
             * Chỉ cho APPLY khi máy rảnh. Nếu máy đang Auto/Home/Homing/Jog
             * thì không cập nhật quỹ đạo để tránh thay đổi dữ liệu giữa lúc chạy.
             */
            if (serial.IsOpen &&
                (autoRunning ||
                 machineState.Equals("Auto", StringComparison.OrdinalIgnoreCase) ||
                 machineState.Equals("Home", StringComparison.OrdinalIgnoreCase) ||
                 machineState.Equals("Homing", StringComparison.OrdinalIgnoreCase) ||
                 machineState.Equals("Jog", StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(
                    "Máy đang chạy. Chỉ APPLY quỹ đạo khi State = Idle.",
                    "Không thể APPLY",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            List<CraneCell> updatedCells = CloneCells(cells);

            /*
             * HOME và END luôn bắt buộc. Dòng này bảo vệ dữ liệu nếu người dùng
             * hoặc file text sửa nhầm selectedPath của HOME/END.
             */
            foreach (var cell in updatedCells)
            {
                if (cell.Mandatory)
                {
                    cell.SelectedPath = true;
                }
            }

            try
            {
                /*
                 * Nếu ComboBox đang là Default thì vẫn cho lưu Default.txt.
                 * Lần sau chọn Default hoặc khởi động lại app, Default sẽ lấy
                 * tọa độ/quỹ đạo đã APPLY thay vì dùng mặc định hard-code.
                 */
                trajectoryProfiles[selectedName] = CloneCells(updatedCells);
                SaveTrajectoryToFile(selectedName, updatedCells);

                string oldSelected = selectedName;
                RefreshTrajectoryCombo();
                cboTrajectory.SelectedItem = oldSelected;

                /*
                 * Gửi lại ROUTE + CELL + PATH + APPLYROUTE xuống Arduino ngay nếu đang kết nối.
                 * Khi MCU trả về route:applied thì routeApplyPending sẽ được xóa.
                 */
                if (serial.IsOpen)
                {
                    routeApplyPending = true;
                    QueueCommands(BuildRouteCommands(selectedName, updatedCells));
                    Log("Route applying to MCU: " + selectedName);
                }
                else
                {
                    Log("Route applied locally: " + selectedName);
                }

                UpdateCellViews();

                MessageBox.Show(
                    serial.IsOpen
                        ? "Đã lưu file và đang gửi quỹ đạo xuống vi điều khiển. Hãy đợi log RX< route:applied rồi START AUTO."
                        : "Đã APPLY và lưu quỹ đạo: " + selectedName,
                    "APPLY OK",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Không thể APPLY quỹ đạo.\n" + ex.Message,
                    "APPLY ERROR",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void SaveCurrentTrajectoryDraft()
        {
            if (string.IsNullOrWhiteSpace(currentSettingTrajectoryName))
            {
                MessageBox.Show("Hãy bấm CREATE và đặt tên quỹ đạo trước.", "Thiếu tên quỹ đạo",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SaveTrajectoryToFile(currentSettingTrajectoryName, settingCells);

            trajectoryProfiles[currentSettingTrajectoryName] = CloneCells(settingCells);
            RefreshTrajectoryCombo();
            cboTrajectory.SelectedItem = currentSettingTrajectoryName;

            MessageBox.Show(
                "Đã lưu quỹ đạo: " + currentSettingTrajectoryName,
                "ADD OK",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void DeleteSelectedTrajectory()
        {
            string selectedName = cboTrajectory.SelectedItem as string;

            if (string.IsNullOrWhiteSpace(selectedName))
            {
                MessageBox.Show("Chưa chọn quỹ đạo để xóa.", "DELETE",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (selectedName.Equals(NoTrajectoryText, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Không được xóa quỹ đạo Default.", "DELETE",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!trajectoryProfiles.ContainsKey(selectedName))
            {
                MessageBox.Show("Không tìm thấy quỹ đạo: " + selectedName, "DELETE",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult confirm = MessageBox.Show(
                "Bạn có chắc muốn xóa quỹ đạo '" + selectedName + "' không?\n" +
                "File .txt của quỹ đạo này cũng sẽ bị xóa.",
                "Xác nhận xóa quỹ đạo",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
            {
                return;
            }

            try
            {
                string filePath = GetTrajectoryFilePath(selectedName);

                trajectoryProfiles.Remove(selectedName);

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                if (currentSettingTrajectoryName.Equals(selectedName, StringComparison.OrdinalIgnoreCase))
                {
                    currentSettingTrajectoryName = "";
                    lblSettingRouteName.Text = "Chưa tạo tên quỹ đạo";
                    ResetSettingCellsToDefaults();
                    UpdateSettingCellViews();
                }

                /* Sau khi xóa, quay về Default để tránh ComboBox còn chọn quỹ đạo không tồn tại. */
                RefreshTrajectoryCombo();
                cboTrajectory.SelectedItem = NoTrajectoryText;

                MessageBox.Show(
                    "Đã xóa quỹ đạo: " + selectedName,
                    "DELETE OK",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Không thể xóa quỹ đạo.\n" + ex.Message,
                    "DELETE ERROR",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private Panel BuildJogPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill
            };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };

            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));

            panel.Controls.Add(root);

            var gridBox = new GroupBox
            {
                Text = "Jog - Chuột phải vào ô để set quỹ đạo / set tọa độ",
                Dock = DockStyle.Fill,
                Padding = new Padding(12)
            };

            gridBox.Controls.Add(BuildCellGrid(jogCellButtons, enableContextMenu: true));
            root.Controls.Add(gridBox, 0, 0);

            var right = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 6,
                ColumnCount = 1,
                AutoScroll = true
            };

            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 185));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            right.Controls.Add(BuildGotoPanel(), 0, 0);
            right.Controls.Add(BuildJogSettingsPanel(), 0, 1);
            right.Controls.Add(BuildApplyTrajectoryPanel(), 0, 2);
            right.Controls.Add(BuildJogButtonsPanel(), 0, 3);
            right.Controls.Add(BuildRelayPanel(), 0, 4);
            right.Controls.Add(BuildRawPanel(), 0, 5);

            root.Controls.Add(right, 1, 0);

            return panel;
        }

        private Button ActionButton(string text, Color color)
        {
            return new Button
            {
                Text = text,
                Dock = DockStyle.Fill,
                BackColor = color,
                ForeColor = Color.White,
                Font = new Font(Font, FontStyle.Bold)
            };
        }

        private TableLayoutPanel BuildCellGrid(Dictionary<string, Button> dictionary, bool enableContextMenu)
        {
            dictionary.Clear();

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 2,
                Padding = new Padding(10)
            };

            for (int i = 0; i < 3; i++)
            {
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            }

            for (int i = 0; i < 2; i++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            }

            AddCellButton(grid, dictionary, GetCell("HOME"), 0, 0, enableContextMenu);
            AddCellButton(grid, dictionary, GetCell("1"), 1, 0, enableContextMenu);
            AddCellButton(grid, dictionary, GetCell("2"), 2, 0, enableContextMenu);

            AddCellButton(grid, dictionary, GetCell("END"), 0, 1, enableContextMenu);
            AddCellButton(grid, dictionary, GetCell("4"), 1, 1, enableContextMenu);
            AddCellButton(grid, dictionary, GetCell("3"), 2, 1, enableContextMenu);

            UpdateCellViews();

            return grid;
        }

        private void AddCellButton(TableLayoutPanel grid, Dictionary<string, Button> dictionary, CraneCell cell, int col, int row, bool enableContextMenu)
        {
            var button = new Button
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(6),
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                BackColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Tag = cell
            };

            if (enableContextMenu)
            {
                button.ContextMenuStrip = CreateCellContextMenu(button);
            }

            dictionary[cell.Name] = button;
            grid.Controls.Add(button, col, row);
        }

        private ContextMenuStrip CreateCellContextMenu(Button button)
        {
            var menu = new ContextMenuStrip();

            var setPath = new ToolStripMenuItem("Set quỹ đạo / bỏ chọn");
            var setCoordinate = new ToolStripMenuItem("Set tọa độ...");
            var setFromCurrent = new ToolStripMenuItem("Lấy tọa độ hiện tại");

            setPath.Click += (_, _) =>
            {
                var cell = (CraneCell)button.Tag;

                if (cell.Mandatory)
                {
                    MessageBox.Show("HOME và END là ô bắt buộc đi qua.", "Thông báo");
                    return;
                }

                cell.SelectedPath = !cell.SelectedPath;

                SendCommand($"PATH {cell.CommandName} {(cell.SelectedPath ? "ON" : "OFF")}");
                UpdateCellViews();
            };

            setCoordinate.Click += (_, _) =>
            {
                var cell = (CraneCell)button.Tag;

                using (var dialog = new CoordinateDialog(cell, currentX, currentY, currentZ))
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        cell.X = dialog.ResultX;
                        cell.Y = dialog.ResultY;
                        cell.Z = dialog.ResultZ;

                        SendCellToArduino(cell);
                        UpdateCellViews();
                    }
                }
            };

            setFromCurrent.Click += (_, _) =>
            {
                var cell = (CraneCell)button.Tag;

                cell.X = currentX;
                cell.Y = currentY;
                cell.Z = currentZ;

                SendCellToArduino(cell);
                UpdateCellViews();
            };

            menu.Items.Add(setPath);
            menu.Items.Add(setCoordinate);
            menu.Items.Add(setFromCurrent);

            return menu;
        }

        private GroupBox BuildGotoPanel()
        {
            var box = new GroupBox
            {
                Text = "Chạy tới tọa độ",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 6,
                RowCount = 2
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 35));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 35));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));

            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

            txtTargetX.Text = "0";
            txtTargetY.Text = "0";
            txtTargetZ.Text = "0";
            txtGotoFeed.Text = "6000";

            layout.Controls.Add(TextLabel("X:"), 0, 0);
            layout.Controls.Add(txtTargetX, 1, 0);

            layout.Controls.Add(TextLabel("Y:"), 2, 0);
            layout.Controls.Add(txtTargetY, 3, 0);

            layout.Controls.Add(TextLabel("Z:"), 0, 1);
            layout.Controls.Add(txtTargetZ, 1, 1);

            layout.Controls.Add(TextLabel("F:"), 2, 1);
            layout.Controls.Add(txtGotoFeed, 3, 1);


            //button goto: di chuyen toi toa do o mode jog
            var btnGoto = new Button
            {
                Text = "GOTO X Y Z",
                Dock = DockStyle.Fill,
                BackColor = Color.SteelBlue,
                ForeColor = Color.White
            };

            btnGoto.Click += (_, _) =>
            {
                if (!CanJogNow()) return;

                SendCommand(
                    $"GOTO X{Number(txtTargetX.Text)} Y{Number(txtTargetY.Text)} Z{Number(txtTargetZ.Text)} F{Number(txtGotoFeed.Text)}");
            };

            layout.Controls.Add(btnGoto, 5, 0);
            layout.SetRowSpan(btnGoto, 2);

            box.Controls.Add(layout);
            return box;
        }

        private GroupBox BuildJogSettingsPanel()
        {
            var box = new GroupBox
            {
                Text = "Setting tốc độ",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 3
            };

            for (int i = 0; i < 4; i++)
            {
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            }

            txtStep.Text = "5";

            // XY Speed is shared by Jog X/Y and Auto X/Y.
            txtFeed.Text = "6000";

            // Z Speed is shared by Jog Z and Auto Z.
            txtFeedZ.Text = "1800";

            layout.Controls.Add(TextLabel("Step:"), 0, 0);
            layout.Controls.Add(txtStep, 1, 0);

            layout.Controls.Add(TextLabel("XY Speed:"), 2, 0);
            layout.Controls.Add(txtFeed, 3, 0);

            layout.Controls.Add(TextLabel("Z Speed:"), 0, 1);
            layout.Controls.Add(txtFeedZ, 1, 1);

            /*
            var note = new Label
            {
                Text = "XY: Jog X/Y + Auto X/Y    Z: Jog Z + Auto Z",
                Dock = DockStyle.Fill,
                ForeColor = Color.DimGray,
                TextAlign = ContentAlignment.MiddleLeft
            };

            layout.Controls.Add(note, 2, 1);
            layout.SetColumnSpan(note, 2);

            */

            var btnApply = new Button
            {
                Text = "APPLY SPEED",
                Dock = DockStyle.Fill
            };

            btnApply.Click += (_, _) => ApplySpeed();

            layout.Controls.Add(btnApply, 2, 1);
            layout.SetColumnSpan(btnApply, 1);

            box.Controls.Add(layout);
            return box;
        }

        private GroupBox BuildApplyTrajectoryPanel()
        {
            var box = new GroupBox
            {
                Text = "Áp dụng quỹ đạo đang sửa",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 1
            };

            var btnApplyRoute = ActionButton("APPLY QUỸ ĐẠO", Color.DarkOrange);
            btnApplyRoute.Click += (_, _) => ApplyCurrentCellsToSelectedTrajectory();

            layout.Controls.Add(btnApplyRoute, 0, 0);
            box.Controls.Add(layout);

            return box;
        }

        private GroupBox BuildJogButtonsPanel()
        {
            var box = new GroupBox
            {
                Text = "Jog từng trục",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 3
            };

            for (int i = 0; i < 5; i++)
            {
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            }

            for (int i = 0; i < 3; i++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
            }

            grid.Controls.Add(new Label(), 0, 0);
            grid.Controls.Add(new Label(), 1, 0);
            grid.Controls.Add(JogButton("Y+", "Y+"), 2, 0);
            grid.Controls.Add(JogButton("Z+", "Z+"), 3, 0);
            grid.Controls.Add(new Label(), 4, 0);

            grid.Controls.Add(JogButton("X-", "X-"), 0, 1);
            grid.Controls.Add(new Label(), 1, 1);
            grid.Controls.Add(new Label
            {
                Text = "XY",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 16F, FontStyle.Bold)
            }, 2, 1);
            grid.Controls.Add(JogButton("X+", "X+"), 3, 1);
            grid.Controls.Add(new Label(), 4, 1);

            grid.Controls.Add(new Label(), 0, 2);
            grid.Controls.Add(new Label(), 1, 2);
            grid.Controls.Add(JogButton("Y-", "Y-"), 2, 2);
            grid.Controls.Add(JogButton("Z-", "Z-"), 3, 2);

            var btnStop = new Button
            {
                Text = "STOP",
                Dock = DockStyle.Fill,
                BackColor = Color.Firebrick,
                ForeColor = Color.White,
                Font = new Font(Font, FontStyle.Bold)
            };

            btnStop.Click += (_, _) =>
            {
                SendCommand("STOP");
                autoRunning = false;
            };

            grid.Controls.Add(btnStop, 4, 2);

            box.Controls.Add(grid);
            return box;
        }

        private GroupBox BuildRelayPanel()
        {
            var box = new GroupBox
            {
                Text = "Điều khiển quạt / nam châm thủ công",
                Dock = DockStyle.Fill,
                Padding = new Padding(6)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1
            };

            for (int i = 0; i < 4; i++)
            {
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            }

            var btnFanOn = new Button { Text = "FAN ON", Dock = DockStyle.Fill };
            btnFanOn.Click += (_, _) => SendCommand("FANON");

            var btnFanOff = new Button { Text = "FAN OFF", Dock = DockStyle.Fill };
            btnFanOff.Click += (_, _) => SendCommand("FANOFF");

            var btnMagOn = new Button { Text = "MAGNET ON", Dock = DockStyle.Fill };
            btnMagOn.Click += (_, _) => SendCommand("MAGON");

            var btnMagOff = new Button { Text = "MAGNET OFF", Dock = DockStyle.Fill };
            btnMagOff.Click += (_, _) => SendCommand("MAGOFF");

            layout.Controls.Add(btnFanOn, 0, 0);
            layout.Controls.Add(btnFanOff, 1, 0);
            layout.Controls.Add(btnMagOn, 2, 0);
            layout.Controls.Add(btnMagOff, 3, 0);

            box.Controls.Add(layout);
            return box;
        }

        private GroupBox BuildRawPanel()
        {
            var box = new GroupBox
            {
                Text = "Gửi lệnh thủ công",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                RowCount = 1,
                Height = 38
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));

            txtRaw.Text = "STATUS";
            txtRaw.Dock = DockStyle.Fill;

            var btnSend = new Button
            {
                Text = "SEND",
                Dock = DockStyle.Fill
            };

            btnSend.Click += (_, _) => SendCommand(txtRaw.Text);

            layout.Controls.Add(txtRaw, 0, 0);
            layout.Controls.Add(btnSend, 1, 0);

            box.Controls.Add(layout);
            return box;
        }

        private Control BuildLogPanel()
        {
            var box = new GroupBox
            {
                Text = "Log TX/RX",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            txtLog.Dock = DockStyle.Fill;
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.BackColor = Color.FromArgb(18, 18, 18);
            txtLog.ForeColor = Color.WhiteSmoke;
            txtLog.Font = new Font("Consolas", 10F);

            box.Controls.Add(txtLog);
            return box;
        }

        private Button JogButton(string text, string command)
        {
            var btn = new Button
            {
                Text = text,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold)
            };

            btn.Click += (_, _) =>
            {
                if (!CanJogNow()) return;

                ApplySpeed();
                SendCommand(command);
            };

            return btn;
        }

        private bool CanJogNow()
        {
            if (autoRunning || machineState.Equals("Auto", StringComparison.OrdinalIgnoreCase) ||
                machineState.Equals("Home", StringComparison.OrdinalIgnoreCase) ||
                machineState.Equals("Homing", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Máy đang chạy. Hãy bấm STOP trước khi Jog.", "Không thể Jog",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            return true;
        }

        private void ApplySpeed()
        {
            /*
             * Unified speed setting:
             * FEED  = XY speed for Jog X/Y and Auto X/Y.
             * FEEDZ = Z speed for Jog Z and Auto Z.
             */
            SendCommand("STEP " + Number(txtStep.Text));
            SendCommand("FEED " + Number(txtFeed.Text));
            SendCommand("FEEDZ " + Number(txtFeedZ.Text));
        }

        private void SyncConfigToArduino()
        {
            ApplySpeed();

            foreach (var cell in cells)
            {
                SendCellToArduino(cell);

                if (!cell.Mandatory)
                {
                    SendCommand($"PATH {cell.CommandName} {(cell.SelectedPath ? "ON" : "OFF")}");
                }
            }
        }

        private void SendCellToArduino(CraneCell cell)
        {
            SendCommand(BuildCellCommand(cell));
        }



        private void StartAutoWithSelectedTrajectory()
        {
            /* Kept for compatibility. Route is applied by ComboBox change. */
            SendCommand("AUTO");
        }

        private string BuildCellCommand(CraneCell cell)
        {
            return $"CELL {cell.CommandName} X{Number(cell.X)} Y{Number(cell.Y)} Z{Number(cell.Z)}";
        }

        private void QueueCommands(IEnumerable<string> commands)
        {
            commandQueueTimer.Stop();
            commandQueue.Clear();

            foreach (string command in commands)
            {
                if (!string.IsNullOrWhiteSpace(command))
                {
                    commandQueue.Enqueue(command.Trim());
                }
            }

            SendNextQueuedCommand();

            if (commandQueue.Count > 0)
            {
                commandQueueTimer.Start();
            }
        }

        private void SendNextQueuedCommand()
        {
            if (commandQueue.Count == 0)
            {
                commandQueueTimer.Stop();
                return;
            }

            SendCommand(commandQueue.Dequeue());

            if (commandQueue.Count == 0)
            {
                commandQueueTimer.Stop();
            }
        }


        //ham doc file text 
        private void LoadTrajectoryFiles()
        {
            trajectoryProfiles.Clear();

            string folder = GetTrajectoryFolder();

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            foreach (string filePath in Directory.GetFiles(folder, "*.txt"))
            {
                if (TryLoadTrajectoryFile(filePath, out string name, out List<CraneCell> loadedCells))
                {
                    trajectoryProfiles[name] = loadedCells;
                }
            }
        }

        private bool TryLoadTrajectoryFile(string filePath, out string name, out List<CraneCell> loadedCells)
        {
            name = Path.GetFileNameWithoutExtension(filePath);
            loadedCells = new List<CraneCell>();

            try
            {
                string text = File.ReadAllText(filePath, Encoding.UTF8);

                var regex = new Regex(
                    @"\{\s*""(?<name>[^""]+)""\s*,\s*" +
                    @"(?<x>[-+]?\d+(?:\.\d+)?)f?\s*,\s*" +
                    @"(?<y>[-+]?\d+(?:\.\d+)?)f?\s*,\s*" +
                    @"(?<z>[-+]?\d+(?:\.\d+)?)f?\s*,\s*" +
                    @"(?<mandatory>[01])\s*,\s*(?<selected>[01])\s*\}");

                foreach (Match match in regex.Matches(text))
                {
                    string cellName = match.Groups["name"].Value;

                    double x = double.Parse(match.Groups["x"].Value, CultureInfo.InvariantCulture);
                    double y = double.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture);
                    double z = double.Parse(match.Groups["z"].Value, CultureInfo.InvariantCulture);

                    bool mandatory = match.Groups["mandatory"].Value == "1";
                    bool selected = match.Groups["selected"].Value == "1";

                    loadedCells.Add(new CraneCell(
                        cellName,
                        cellName,
                        x,
                        y,
                        z,
                        mandatory,
                        selected));
                }

                if (loadedCells.Count != 6)
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }


        // tao file text luu thong tin cua cac cell trong quy dao
        private void SaveTrajectoryToFile(string name, List<CraneCell> routeCells)
        {
            string folder = GetTrajectoryFolder();

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            string filePath = GetTrajectoryFilePath(name);

            using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
            {     
                for (int i = 0; i < routeCells.Count; i++)
                {
                    CraneCell cell = routeCells[i];

                    string comma = (i == routeCells.Count - 1) ? "" : ",";

                    writer.WriteLine(
                        $"{{\"{cell.Name}\", {Number(cell.X)}f, {Number(cell.Y)}f, {Number(cell.Z)}f, {(cell.Mandatory ? 1 : 0)}, {(cell.SelectedPath ? 1 : 0)}}}{comma}");
                }
            }
        }


        //duong dan luu file text
        private string GetTrajectoryFolder()
        {
            /*
             * Files are saved in the folder that contains the running WinForms app.
             * In Visual Studio Debug this is usually bin\\Debug\\net8.0-windows.
             */
            //return Application.StartupPath; // luu truc tiep trong bin 

            //luu theo duong dan 
            string folder = @"D:\GRBL\Gantry_Integrated_Ver1.0\WinForms_GantryIntegrated\trajectory";

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            return folder;

        }

        private string GetTrajectoryFilePath(string name)
        {
            return Path.Combine(GetTrajectoryFolder(), SanitizeTrajectoryName(name) + ".txt");
        }

        private string SanitizeTrajectoryName(string name)
        {
            string result = (name ?? "").Trim();

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(c, '_');
            }

            return result;
        }

        private void RefreshTrajectoryCombo()
        {
            string old = cboTrajectory.SelectedItem as string;

            isLoadingTrajectoryCombo = true;

            cboTrajectory.Items.Clear();
            cboTrajectory.Items.Add(NoTrajectoryText);

            foreach (string name in trajectoryProfiles.Keys)
            {
                /*
                 * Default đã được add cố định ở trên.
                 * Nếu có Default.txt thì vẫn lưu trong trajectoryProfiles,
                 * nhưng không add lần 2 vào ComboBox.
                 */
                if (!name.Equals(NoTrajectoryText, StringComparison.OrdinalIgnoreCase))
                {
                    cboTrajectory.Items.Add(name);
                }
            }

            if (!string.IsNullOrWhiteSpace(old) && cboTrajectory.Items.Contains(old))
            {
                cboTrajectory.SelectedItem = old;
            }
            else
            {
                cboTrajectory.SelectedItem = NoTrajectoryText;
            }

            isLoadingTrajectoryCombo = false;

            /* Update UI; if serial is open, also send route to MCU. */
            ApplySelectedTrajectoryNow();
        }

        private void ShowScreen(Control panel)
        {
            screenHost.Controls.Clear();
            panel.Dock = DockStyle.Fill;
            screenHost.Controls.Add(panel);
        }

        private CraneCell GetCell(string name)
        {
            foreach (var cell in cells)
            {
                if (cell.Name == name)
                {
                    return cell;
                }
            }

            throw new InvalidOperationException("Cell not found: " + name);
        }

        private Label TextLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight
            };
        }

        private Label Led(string text)
        {
            return new Label
            {
                Text = text + "\nOFF",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        private void SetValueLabel(Label label, string text)
        {
            label.Text = text;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            label.ForeColor = Color.DarkBlue;
        }

        private void SetLed(Label label, string caption, bool on)
        {
            label.Text = caption + "\n" + (on ? "ON" : "OFF");
            label.BackColor = on ? Color.LightGreen : Color.White;
        }

        private void SetTextIfNotFocused(TextBox box, string value)
        {
            /*
             * STATUS is received every 500ms.
             * Do not overwrite speed fields while the user is typing.
             */
            if (!box.Focused)
            {
                box.Text = value;
            }
        }

        private void RefreshPorts()
        {
            string oldPort = cboPorts.SelectedItem as string;

            cboPorts.Items.Clear();
            cboPorts.Items.AddRange(SerialPort.GetPortNames());

            if (oldPort != null && cboPorts.Items.Contains(oldPort))
            {
                cboPorts.SelectedItem = oldPort;
            }
            else if (cboPorts.Items.Count > 0)
            {
                cboPorts.SelectedIndex = 0;
            }
        }

        private void SetupSerial()
        {
            serial.Encoding = Encoding.ASCII;
            serial.ReadTimeout = 200;
            serial.WriteTimeout = 200;
            serial.DtrEnable = true;
            serial.RtsEnable = true;
        }

        private void ToggleConnection()
        {
            if (serial.IsOpen)
            {
                statusTimer.Stop();
                readTimer.Stop();
                serial.Close();

                btnConnect.Text = "CONNECT";
                btnConnect.BackColor = Color.SeaGreen;
                lblConnection.Text = "Disconnected";
                lblConnection.ForeColor = Color.DarkRed;
                machineState = "Disconnected";

                Log("Disconnected");
                mcuHomed = false;
                routeApplyPending = false;
                return;
            }

            if (cboPorts.SelectedItem == null)
            {
                MessageBox.Show("Chưa chọn COM.", "Lỗi");
                return;
            }

            try
            {
                serial.PortName = cboPorts.SelectedItem.ToString();
                serial.BaudRate = int.Parse(cboBaud.SelectedItem.ToString());
                serial.DataBits = 8;
                serial.StopBits = StopBits.One;
                serial.Parity = Parity.None;
                serial.Handshake = Handshake.None;

                serial.Open();

                btnConnect.Text = "DISCONNECT";
                btnConnect.BackColor = Color.Firebrick;
                lblConnection.Text = "Connected " + serial.PortName;
                lblConnection.ForeColor = Color.Green;

                readTimer.Start();
                statusTimer.Start();

                Log("Connected " + serial.PortName);

                SendCommand("CELLS");
                SendCommand("STATUS");
                SendCommand("LIMITS");

                /* Push the currently selected route to Arduino after connecting. */
                ApplySelectedTrajectoryNow();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Connect error");
                Log("Connect error: " + ex.Message);
            }
        }

        private void SendCommand(string command, bool logTx = true)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return;
            }

            command = command.Trim();

            if (!serial.IsOpen)
            {
                Log("NOT CONNECTED: " + command);
                return;
            }

            try
            {
                serial.Write(command + "\n");

                if (logTx)
                {
                    Log("TX> " + command);
                }
            }
            catch (Exception ex)
            {
                Log("TX ERROR: " + ex.Message);
            }
        }

        private void ReadSerialByTimer()
        {
            if (!serial.IsOpen)
            {
                return;
            }

            try
            {
                string data = serial.ReadExisting();

                foreach (char c in data)
                {
                    if (c == '\r')
                    {
                        continue;
                    }

                    if (c == '\n')
                    {
                        string line = rxBuffer.ToString().Trim();
                        rxBuffer.Clear();

                        if (line.Length > 0)
                        {
                            ProcessRxLine(line);
                        }
                    }
                    else
                    {
                        rxBuffer.Append(c);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("RX ERROR: " + ex.Message);
            }
        }

        private void ProcessRxLine(string line)
        {
            Log("RX< " + line);

            if (line.StartsWith("<") && line.EndsWith(">"))
            {
                ParseStatus(line);
                return;
            }

            if (line.StartsWith("CELL:", StringComparison.OrdinalIgnoreCase))
            {
                ParseCell(line.Substring(5));
                return;
            }

            if (line.StartsWith("LIMITS:", StringComparison.OrdinalIgnoreCase))
            {
                ParseLimits(line.Substring(7));
                return;
            }

            if (line.Equals("route:applied", StringComparison.OrdinalIgnoreCase))
            {
                routeApplyPending = false;
                Log("Route apply completed on MCU.");
                return;
            }

            if (line.Equals("auto:start", StringComparison.OrdinalIgnoreCase))
            {
                /*
                 * auto:start means AUTO is armed.
                 * Actual running/busy state is updated by STATUS.
                 */
                autoRunning = false;
                return;
            }

            if (line.Equals("stop:ok", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("cycle:done", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("auto:fail", StringComparison.OrdinalIgnoreCase))
            {
                autoRunning = false;
                return;
            }

            if (line.StartsWith("home:ok", StringComparison.OrdinalIgnoreCase))
            {
                mcuHomed = true;
                lblHomed.Text = "1";

                /*
                 * Sau khi CHECK HOME xong mới gửi quỹ đạo hiện tại xuống MCU.
                 */
                ApplySelectedTrajectoryNow();

                MessageBox.Show(
                    "Check home xong. Đã bắt đầu cập nhật quỹ đạo hiện tại xuống vi điều khiển.",
                    "Home OK",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            if (line.StartsWith("home:fail", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(line, "Home failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (line.StartsWith("ALARM:", StringComparison.OrdinalIgnoreCase))
            {
                ShowAlarm(line.Substring(6).Trim());
                return;
            }

            if (line.StartsWith("error:auto_running", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Máy đang chạy AUTO/HOME. Hãy bấm STOP trước.", "Busy",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (line.StartsWith("error:not_homed", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Chưa CHECK HOME. Hãy bấm CHECK HOME trước.", "Not homed",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ParseStatus(string line)
        {
            string content = line.Trim('<', '>');
            string[] parts = content.Split('|');

            if (parts.Length > 0)
            {
                machineState = parts[0];
                lblState.Text = machineState;
                autoRunning = machineState.Equals("Auto", StringComparison.OrdinalIgnoreCase);
            }

            foreach (string part in parts)
            {
                if (part.StartsWith("MPos:", StringComparison.OrdinalIgnoreCase))
                {
                    ParseMPos(part.Substring(5));
                }
                else if (part.StartsWith("Step:", StringComparison.OrdinalIgnoreCase))
                {
                    SetTextIfNotFocused(txtStep, part.Substring(5));
                }
                else if (part.StartsWith("Feed:", StringComparison.OrdinalIgnoreCase))
                {
                    SetTextIfNotFocused(txtFeed, part.Substring(5));
                }
                else if (part.StartsWith("AutoFeedXY:", StringComparison.OrdinalIgnoreCase))
                {
                    SetTextIfNotFocused(txtFeed, part.Substring(11));
                }
                else if (part.StartsWith("AutoFeedZ:", StringComparison.OrdinalIgnoreCase))
                {
                    SetTextIfNotFocused(txtFeedZ, part.Substring(10));
                }
                else if (part.StartsWith("Homed:", StringComparison.OrdinalIgnoreCase))
                {
                    string homedValue = part.Substring(6).Trim();

                    lblHomed.Text = homedValue;
                    mcuHomed = homedValue == "1";
                }
                else if (part.StartsWith("Sensor:", StringComparison.OrdinalIgnoreCase) ||
                         part.StartsWith("Object:", StringComparison.OrdinalIgnoreCase))
                {
                    string value = part.Substring(part.IndexOf(':') + 1);
                    SetLed(ledSensor, "OBJECT\nA2", value.Trim() == "1");
                }
                else if (part.StartsWith("X:", StringComparison.OrdinalIgnoreCase))
                {
                    SetLed(ledLimitX, "LIMIT X\nD9", part.Substring(2).Trim() == "1");
                }
                else if (part.StartsWith("Y:", StringComparison.OrdinalIgnoreCase))
                {
                    SetLed(ledLimitY, "LIMIT Y\nD10", part.Substring(2).Trim() == "1");
                }
                else if (part.StartsWith("ZP:", StringComparison.OrdinalIgnoreCase))
                {
                    SetLed(ledLimitZ, "LIMIT Z+\nD11", part.Substring(3).Trim() == "1");
                }
                else if (part.StartsWith("Fan:", StringComparison.OrdinalIgnoreCase))
                {
                    SetLed(ledFan, "FAN", part.Substring(4).Trim() == "1");
                }
                else if (part.StartsWith("Magnet:", StringComparison.OrdinalIgnoreCase))
                {
                    SetLed(ledMagnet, "MAGNET", part.Substring(7).Trim() == "1");
                }
else if (part.StartsWith("Count:", StringComparison.OrdinalIgnoreCase))
                {
                    lblCount.Text = part.Substring(6);
                }
                else if (part.StartsWith("Cell:", StringComparison.OrdinalIgnoreCase))
                {
                    HighlightCurrentCell(part.Substring(5));
                }
                else if (part.StartsWith("Alarm:", StringComparison.OrdinalIgnoreCase))
                {
                    string alarm = part.Substring(6);
                    lblAlarm.Text = "Alarm\n" + alarm;
                    lblAlarm.BackColor = string.Equals(alarm, "NONE", StringComparison.OrdinalIgnoreCase)
                        ? Color.White
                        : Color.MistyRose;
                }
            }
        }

        private void ParseMPos(string payload)
        {
            string[] values = payload.Split(',');

            if (values.Length < 3)
            {
                return;
            }

            double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out currentX);
            double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out currentY);
            double.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out currentZ);

            lblX.Text = currentX.ToString("0.00", CultureInfo.InvariantCulture);
            lblY.Text = currentY.ToString("0.00", CultureInfo.InvariantCulture);
            lblZ.Text = currentZ.ToString("0.00", CultureInfo.InvariantCulture);

            UpdateCellViews();
        }

        private void ParseCell(string payload)
        {
            string[] parts = payload.Split(',');

            if (parts.Length < 6)
            {
                return;
            }

            string name = parts[0];
            CraneCell cell = GetCellByCommand(name);

            if (cell == null)
            {
                return;
            }

            double x;
            double y;
            double z;

            if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out x)) cell.X = x;
            if (double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out y)) cell.Y = y;
            if (double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out z)) cell.Z = z;

            cell.SelectedPath = parts[4].Trim() == "1";
            cell.Mandatory = parts[5].Trim() == "1";

            UpdateCellViews();
        }

        private void ParseLimits(string payload)
        {
            string[] parts = payload.Split(',');

            foreach (string part in parts)
            {
                string[] kv = part.Split('=');

                if (kv.Length != 2)
                {
                    continue;
                }

                bool on = kv[1].Trim() == "1";

                switch (kv[0].Trim())
                {
                    case "X":
                        SetLed(ledLimitX, "LIMIT X\nD9", on);
                        break;

                    case "Y":
                        SetLed(ledLimitY, "LIMIT Y\nD10", on);
                        break;

                    case "ZP":
                        SetLed(ledLimitZ, "LIMIT Z+\nD11", on);
                        break;
                }
            }
        }

        private CraneCell GetCellByCommand(string commandName)
        {
            foreach (var cell in cells)
            {
                if (cell.CommandName.Equals(commandName, StringComparison.OrdinalIgnoreCase) ||
                    cell.Name.Equals(commandName, StringComparison.OrdinalIgnoreCase))
                {
                    return cell;
                }
            }

            return null;
        }

        private void HighlightCurrentCell(string cellName)
        {
            foreach (var kv in mainCellButtons)
            {
                kv.Value.BackColor = kv.Key.Equals(cellName, StringComparison.OrdinalIgnoreCase)
                    ? Color.LightGreen
                    : Color.White;
            }

            foreach (var kv in jogCellButtons)
            {
                kv.Value.BackColor = kv.Key.Equals(cellName, StringComparison.OrdinalIgnoreCase)
                    ? Color.LightGreen
                    : Color.White;
            }
        }

        private void UpdateCellViews()
        {
            UpdateCellButtons(mainCellButtons);
            UpdateCellButtons(jogCellButtons);
        }

        private void UpdateCellButtons(Dictionary<string, Button> buttons)
        {
            foreach (var pair in buttons)
            {
                CraneCell cell = GetCell(pair.Key);
                Button button = pair.Value;
                button.Tag = cell;

                string pathMark = cell.SelectedPath ? "✓" : " ";
                if (cell.Mandatory) pathMark = "★";

                button.Text =
                    $"{cell.Name}\n{pathMark}\nX{cell.X:0} Y{cell.Y:0}\nZ{cell.Z:0}";

                if (Math.Abs(currentX - cell.X) <= 1.0 &&
                    Math.Abs(currentY - cell.Y) <= 1.0)
                {
                    button.BackColor = Color.LightGreen;
                }
                else
                {
                    button.BackColor = Color.White;
                }
            }
        }

        private void ShowAlarm(string alarm)
        {
            lblAlarm.Text = "Alarm\n" + alarm;
            lblAlarm.BackColor = Color.MistyRose;

            if (alarm != lastAlarmMessage)
            {
                lastAlarmMessage = alarm;

                MessageBox.Show(
                    "Cầu trục đã chạm limit hoặc bị cảnh báo:\n" + alarm,
                    "Alarm",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private string Number(string text)
        {
            double value;

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            {
                return value.ToString("0.###", CultureInfo.InvariantCulture);
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return value.ToString("0.###", CultureInfo.InvariantCulture);
            }

            return "0";
        }

        private string Number(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private void Log(string text)
        {
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            commandQueueTimer.Stop();
            statusTimer.Stop();
            readTimer.Stop();

            if (serial.IsOpen)
            {
                serial.Close();
            }

            base.OnFormClosing(e);
        }
    }


    public class TrajectoryNameDialog : Form
    {
        private readonly TextBox txtName = new TextBox();

        public string TrajectoryName { get; private set; } = "";

        public TrajectoryNameDialog()
        {
            Text = "Create trajectory";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(360, 160);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Padding = new Padding(12)
            };

            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

            root.Controls.Add(new Label
            {
                Text = "Tên:",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight
            }, 0, 0);

            txtName.Dock = DockStyle.Fill;
            txtName.Text = "";
            root.Controls.Add(txtName, 1, 0);

            var note = new Label
            {
                Text = "Ví dụ: xanh, do, xam",
                Dock = DockStyle.Fill,
                ForeColor = Color.DimGray,
                TextAlign = ContentAlignment.MiddleLeft
            };

            root.Controls.Add(note, 1, 1);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft
            };

            var btnOk = new Button { Text = "OK", Width = 90 };
            var btnCancel = new Button { Text = "Cancel", Width = 90 };

            btnOk.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(txtName.Text))
                {
                    MessageBox.Show("Hãy nhập tên quỹ đạo.");
                    return;
                }

                TrajectoryName = txtName.Text.Trim();
                DialogResult = DialogResult.OK;
                Close();
            };

            btnCancel.Click += (_, _) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            buttons.Controls.Add(btnOk);
            buttons.Controls.Add(btnCancel);

            root.Controls.Add(buttons, 0, 2);
            root.SetColumnSpan(buttons, 2);

            Controls.Add(root);
        }
    }

    public class CoordinateDialog : Form
    {
        private readonly TextBox txtX = new TextBox();
        private readonly TextBox txtY = new TextBox();
        private readonly TextBox txtZ = new TextBox();

        public double ResultX { get; private set; }
        public double ResultY { get; private set; }
        public double ResultZ { get; private set; }

        public CoordinateDialog(CraneCell cell, double currentX, double currentY, double currentZ)
        {
            Text = "Set tọa độ ô " + cell.Name;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(360, 230);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5,
                Padding = new Padding(12)
            };

            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            for (int i = 0; i < 5; i++)
            {
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            }

            txtX.Text = cell.X.ToString("0.###", CultureInfo.InvariantCulture);
            txtY.Text = cell.Y.ToString("0.###", CultureInfo.InvariantCulture);
            txtZ.Text = cell.Z.ToString("0.###", CultureInfo.InvariantCulture);

            root.Controls.Add(new Label { Text = "X:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, 0);
            root.Controls.Add(txtX, 1, 0);

            root.Controls.Add(new Label { Text = "Y:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, 1);
            root.Controls.Add(txtY, 1, 1);

            root.Controls.Add(new Label { Text = "Z:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, 2);
            root.Controls.Add(txtZ, 1, 2);

            var btnCurrent = new Button { Text = "Lấy tọa độ hiện tại", Dock = DockStyle.Fill };
            btnCurrent.Click += (_, _) =>
            {
                txtX.Text = currentX.ToString("0.###", CultureInfo.InvariantCulture);
                txtY.Text = currentY.ToString("0.###", CultureInfo.InvariantCulture);
                txtZ.Text = currentZ.ToString("0.###", CultureInfo.InvariantCulture);
            };

            root.Controls.Add(btnCurrent, 0, 3);
            root.SetColumnSpan(btnCurrent, 2);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft
            };

            var btnOk = new Button { Text = "Apply", Width = 90 };
            var btnCancel = new Button { Text = "Cancel", Width = 90 };

            btnOk.Click += (_, _) =>
            {
                double x;
                double y;
                double z;

                if (!double.TryParse(txtX.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out x) &&
                    !double.TryParse(txtX.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out x))
                {
                    MessageBox.Show("X không hợp lệ.");
                    return;
                }

                if (!double.TryParse(txtY.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out y) &&
                    !double.TryParse(txtY.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out y))
                {
                    MessageBox.Show("Y không hợp lệ.");
                    return;
                }

                if (!double.TryParse(txtZ.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out z) &&
                    !double.TryParse(txtZ.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out z))
                {
                    MessageBox.Show("Z không hợp lệ.");
                    return;
                }

                ResultX = x;
                ResultY = y;
                ResultZ = z;
                DialogResult = DialogResult.OK;
                Close();
            };

            btnCancel.Click += (_, _) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            buttons.Controls.Add(btnOk);
            buttons.Controls.Add(btnCancel);

            root.Controls.Add(buttons, 0, 4);
            root.SetColumnSpan(buttons, 2);

            Controls.Add(root);
        }
    }
}
