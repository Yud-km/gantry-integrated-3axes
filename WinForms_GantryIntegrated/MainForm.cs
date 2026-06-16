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
    public partial class MainForm : Form
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
}
