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
    }
}
