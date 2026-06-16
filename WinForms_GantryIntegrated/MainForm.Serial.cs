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
    }
}
