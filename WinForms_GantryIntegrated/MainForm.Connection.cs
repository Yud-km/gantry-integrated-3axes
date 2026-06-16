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
        private bool isLoadingTrajectoryCombo = false;

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
    }
}
