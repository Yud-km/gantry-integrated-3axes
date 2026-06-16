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

private void ShowScreen(Control panel)
        {
            screenHost.Controls.Clear();
            panel.Dock = DockStyle.Fill;
            screenHost.Controls.Add(panel);
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
    }
}
